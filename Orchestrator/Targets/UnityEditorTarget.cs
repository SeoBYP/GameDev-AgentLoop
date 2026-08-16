using System.Text;
using System.Text.Json;
using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Targets;

/// <summary>
/// `unity` CLI + com.unity.pipeline 을 통해 "실행 중인 에디터"에 생성물을 적용·검증하는 손.
///
/// 이 프로젝트의 핵심 인에이블러(DESIGN.md §1): pipeline 패키지가 에디터 안에서 도는 로컬 서버를
/// 열어, 재컴파일·콘솔·플레이모드 등을 CLI 명령으로 조작·관찰하게 해준다. 여기서는
///   ② 적용 = Assets/ 밑에 파일 쓰기 + `recompile` 명령으로 리컴파일 트리거
///   ③ 검증 = `recompile_status` 를 폴링해 완료 대기 + 컴파일 에러 수집
/// 를 구현한다. 검증이 1급 시민(D4).
///
/// pipeline 명령 계약(0.4.0-exp.1, `unity command` 로 확인):
///   recompile         → 강제 리컴파일(비포커스에서도 동작). 즉시 반환 후 recompile_status 로 폴링.
///   recompile_status  → { status: idle|triggered|compiling|completed|up_to_date, failed: bool, errors: [] }
///   eval              → Roslyn 으로 C# 즉시 실행(Phase 2 런타임 assert 훅으로 남겨둠).
/// `--json` 봉투: { success, command, data: { result, success, ... }, errors, warnings }.
/// recompile_status 의 data.result 는 JSON "문자열"이라 한 번 더 파싱한다.
/// </summary>
public sealed class UnityEditorTarget : IExecTarget
{
    private readonly string _unityExe;
    private readonly string _projectPath;
    private readonly int _timeoutSec;
    private readonly string _label;
    private readonly bool _allowUnsafeEval;   // 가드 우회(명시적 opt-in)

    public string Name => _label;

    public bool Supports(VerifyKind kind) =>
        kind is VerifyKind.Compile or VerifyKind.RuntimeAssert or VerifyKind.Performance or VerifyKind.Tests;

    public string LabelFor(VerifyKind kind) => kind switch
    {
        VerifyKind.Compile => "컴파일",
        VerifyKind.RuntimeAssert => "플레이모드 assert",
        VerifyKind.Performance => "성능 예산",
        VerifyKind.Tests => "테스트 러너",
        _ => kind.ToString(),
    };

    public string ConnectionHint =>
        "Unity pipeline 서버에 연결할 수 없습니다.\n" +
        "  → 대상 프로젝트를 Unity 에디터에서 열고, `unity pipeline list` 의 '서버 연결 가능' 이 true 인지 확인하세요.";

    /// <summary>Unity 에디터 타깃의 생성 규격 — C# 스크립트 + 플레이모드 assert 스니펫.</summary>
    public string GenerationBrief => """
        TARGET: Unity 6 (6000.x) Editor, C#. Assume UnityEngine is available.
        - Put runtime scripts under Assets/Scripts/ (assembly `AgentLoop.Runtime`).

        - PREFER writing a PlayMode test over the ASSERT block below. Tests persist in the repo
          and guard against regressions, and they can span multiple frames. Emit it as a FILE:
        FILE: Assets/Tests/PlayMode/<TypeName>Tests.cs
        ```csharp
        using System.Collections;
        using NUnit.Framework;
        using UnityEngine;
        using UnityEngine.TestTools;

        public class <TypeName>Tests
        {
            [Test]
            public void Clamps_At_Zero() { /* build the component, act, Assert.AreEqual(...) */ }

            [UnityTest]
            public IEnumerator Reaches_Target_Over_Frames()
            {
                // yield return null; advances one frame — use this for multi-frame scenarios
                yield return null;
            }
        }
        ```
          Rules for tests:
            * The test assembly `AgentLoop.Tests` already exists and references `AgentLoop.Runtime`.
              Just add the .cs file under Assets/Tests/PlayMode/ — do NOT create an .asmdef.
            * Create objects with `new GameObject()` + `AddComponent<T>()`; clean up with
              `Object.DestroyImmediate(go)` (or `Object.Destroy` inside `[UnityTest]`).
            * Cover the edge cases the goal implies (clamping, bounds, invalid input).
            * Use `[UnityTest]` + `yield return null` when behavior unfolds over frames
              (movement, cooldowns, timers) — that is the only way to verify it honestly.
            * **Input-driven behavior**: derive the test class from `InputTestFixture` and inject
              virtual input instead of faking it with direct method calls:

              public class JumpTests : InputTestFixture
              {
                  [UnityTest]
                  public IEnumerator Space_Triggers_Jump()
                  {
                      var keyboard = InputSystem.AddDevice<Keyboard>();
                      var go = new GameObject();
                      var jump = go.AddComponent<Jumper>();

                      Press(keyboard.spaceKey);
                      yield return null;              // let the input be processed
                      Release(keyboard.spaceKey);
                      yield return null;

                      Assert.IsTrue(jump.HasJumped);
                      Object.Destroy(go);
                  }
              }

              Available helpers: `Press`/`Release`/`PressAndRelease` (ButtonControl),
              `Set(control, value)` (sticks/axes), `SetTouch(...)`.
              Devices: `InputSystem.AddDevice<Keyboard>()` / `<Mouse>()` / `<Gamepad>()`.
              `using UnityEngine.InputSystem;` is required; the test assembly already references it.
            * Do NOT emit an ASSERT block when you write tests.

        - If (and only if) you do NOT write tests, emit EXACTLY ONE runtime check as:
        ASSERT:
        ```csharp
        <C# statements ending in a return>
        ```
          The snippet is executed inside the Unity Editor IN PLAY MODE via Roslyn (`unity command eval`).
          Rules for the snippet:
            * Return the string "OK" when the behavior is correct; otherwise return a SHORT string
              explaining what was expected vs. what actually happened.
            * Exercise the behavior the goal actually asks for, including edge cases
              (clamping, bounds, invalid input) — not just that the type exists.
            * It runs in play mode, so Awake/OnEnable DO run. Build objects with
              `new UnityEngine.GameObject()` + `AddComponent<T>()`, and clean up with
              `UnityEngine.Object.DestroyImmediate(go)` before returning.
            * Use fully qualified UnityEngine names. Do not use `using` directives.
            * No file I/O, no scene loading, no coroutines, no waiting across frames.

        - Then emit EXACTLY ONE performance budget as:
        PERF:
        ```json
        { "component": "<TypeName>", "call": "target.Tick(0.016f)", "iterations": 20000, "maxTotalMs": 12 }
        ```
          The loop builds the measurement itself: it creates the component in play mode, calls `call`
          once to warm up, then times `iterations` repetitions and compares against `maxTotalMs`.
          Rules:
            * `call` must use the variable name `target` for the component instance.
            * Choose the HOT PATH — the method that would run every frame (e.g. a `Tick(float)`),
              not a one-off setup method. Prefer exposing frame work as a callable method.
            * Optional `"setup"`: one statement run before measuring (e.g. `target.SetTarget(...)`).
            * Budget guidance: allocation-free work is roughly 0.1µs/call on a desktop editor;
              allocating per call is 5-10x slower. Pick a budget that a clean implementation
              passes comfortably but a per-call-allocating one does not.
        """;

    public UnityEditorTarget(
        string unityExe,
        string projectPath,
        string label,
        bool allowUnsafeEval = false,
        int timeoutSec = 120)
    {
        _unityExe = unityExe;
        _projectPath = projectPath;
        _label = label;
        _allowUnsafeEval = allowUnsafeEval;
        _timeoutSec = timeoutSec;
    }

    // ── ② 적용 ───────────────────────────────────────────────────────────────
    public async Task<ApplyResult> ApplyAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct)
    {
        if (edits.Count == 0)
            return new ApplyResult(false, "적용할 파일 편집이 없습니다(파싱된 FILE 블록 0개).");

        var root = Path.GetFullPath(_projectPath);
        var written = new List<string>();
        foreach (var edit in edits)
        {
            var full = Path.GetFullPath(Path.Combine(_projectPath, edit.RelativePath));

            // 프로젝트 루트를 벗어나는 경로 쓰기 방지(경로 탈출 방어).
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return new ApplyResult(false, $"프로젝트 밖 경로 거부: {edit.RelativePath}");

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, edit.Content, new UTF8Encoding(false), ct);
            written.Add(edit.RelativePath);
        }

        // 리컴파일 트리거(비동기). 실제 완료 대기·에러 수집은 VerifyAsync 가 한다.
        await RunCommandAsync("recompile", ct);

        return new ApplyResult(true, $"{written.Count}개 파일 적용 + 리컴파일 트리거: {string.Join(", ", written)}");
    }

    // ── ③ 검증 ────────────────────────────────────────────────────────────────
    public Task<VerifyResult> VerifyAsync(VerifySpec spec, CancellationToken ct) => spec.Kind switch
    {
        VerifyKind.Compile => VerifyCompileAsync(ct),
        VerifyKind.RuntimeAssert => VerifyPlayModeAsync(
            spec.AssertCode ?? throw new ArgumentException("RuntimeAssert 는 AssertCode 가 필요합니다.", nameof(spec)), ct),
        VerifyKind.Performance => VerifyPerformanceAsync(
            spec.AssertCode ?? throw new ArgumentException("Performance 는 PERF 명세가 필요합니다.", nameof(spec)), ct),
        VerifyKind.Tests => VerifyTestsAsync(spec.AssertCode, ct),
        _ => throw new NotSupportedException($"지원하지 않는 검증: {spec.Kind}"),
    };

    // ── ③-a 컴파일 검증 ────────────────────────────────────────────────────────
    private async Task<VerifyResult> VerifyCompileAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_timeoutSec);

        // 방금 트리거한 리컴파일이 'compiling' 으로 전이할 여유를 준다
        // (직전 스텝의 stale 'completed' 를 읽는 레이스 방지).
        await Task.Delay(1200, ct);

        while (DateTime.UtcNow < deadline)
        {
            var res = await RunCommandAsync("recompile_status", ct);
            var status = ParseRecompileStatus(res.StdOut);

            if (status is not null && status.Terminal)
            {
                return status.Failed || status.Errors.Count > 0
                    ? new VerifyResult(false, res.StdOut, status.Errors)
                    : new VerifyResult(true, res.StdOut, Array.Empty<string>());
            }
            await Task.Delay(800, ct);
        }

        return new VerifyResult(false, "recompile_status 폴링 타임아웃", new[] { "<recompile timeout>" });
    }

    // ── ③-b 플레이모드 런타임 검증 ──────────────────────────────────────────────
    // 이 프로젝트의 핵심 차별점: "컴파일 통과"를 넘어 "의도대로 동작하는가"까지 본다.
    //   editor_play → (playMode == playing 대기) → eval 로 assert 실행 → editor_stop(항상)
    // assert 스니펫은 통과 시 "OK", 실패 시 사유 문자열을 return 하도록 출력 계약으로 강제한다.
    private async Task<VerifyResult> VerifyPlayModeAsync(string assertCode, CancellationToken ct)
    {
        // 실행 **전에** 위험한 호출을 거른다. 이건 모델의 잘못이므로 피드백으로 되돌린다.
        var unsafeHits = _allowUnsafeEval ? Array.Empty<string>() : SnippetGuard.Inspect(assertCode);
        if (unsafeHits.Count > 0)
            return new VerifyResult(false, "", unsafeHits.Select(h => "안전 위반: " + h).ToArray());

        // 리컴파일 직후엔 도메인 리로드가 끝나야 플레이모드 진입이 받아들여진다.
        // 이걸 안 기다리면 진입이 조용히 거부되고, 루프가 그걸 "코드가 틀렸다"로 오해한다.
        await WaitUntilReadyAsync(ct);

        if (!await EnterPlayModeAsync(ct))
        {
            // 인프라 실패는 모델 탓이 아니다 → 피드백으로 되돌리지 않고 루프를 중단시킨다.
            throw new InvalidOperationException(
                "플레이모드 진입 실패 — 에디터가 준비되지 않았거나 진입이 거부되었습니다(에디터 상태를 확인하세요).");
        }

        try
        {
            var (res, message) = await EvalWithRetryAsync(assertCode, ct);
            var ok = message.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                     message.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            return ok
                ? new VerifyResult(true, res.StdOut, Array.Empty<string>())
                : new VerifyResult(false, res.StdOut, new[] { message });
        }
        finally
        {
            // 검증이 실패하든 취소되든 에디터를 반드시 플레이모드에서 빼낸다(부작용 방지).
            await RunCommandAsync("editor_stop", CancellationToken.None);
        }
    }

    // ── ③-d 테스트 러너 검증 ────────────────────────────────────────────────────
    // RuntimeAssert 가 "한 번 쓰고 버리는 스니펫"이라면, 이건 **레포에 남는 테스트**다.
    // `[UnityTest]` 코루틴이면 여러 프레임에 걸친 시나리오까지 검증된다.
    //
    // 계약(실측): PlayMode 테스트는 플레이모드 진입 시 도메인 리로드가 HTTP 요청을 끊어서
    //   **동기 실행이 불가능**하다. CLI 도 그렇게 안내한다 →
    //   `run_tests --async_tests` 로 시작하고 `test_status` 를 폴링해야 한다.
    //   test_status 의 data.result 는 JSON **문자열**이라 한 번 더 파싱한다.
    private async Task<VerifyResult> VerifyTestsAsync(string? filter, CancellationToken ct)
    {
        await WaitUntilReadyAsync(ct);

        var args = new List<string> { "run_tests", "--mode", "PlayMode", "--async_tests" };
        if (!string.IsNullOrWhiteSpace(filter))
        {
            args.Add("--filter");
            args.Add(filter!);
        }
        await RunCommandAsync(args[0], ct, args.Skip(1).ToArray());

        var deadline = DateTime.UtcNow.AddSeconds(_timeoutSec);
        await Task.Delay(1500, ct);

        while (DateTime.UtcNow < deadline)
        {
            var res = await RunCommandAsync("test_status", ct);
            var report = ParseTestStatus(res.StdOut);

            if (report is { Completed: true })
            {
                if (report.Total == 0)
                    return new VerifyResult(false, res.StdOut,
                        new[] { "실행된 테스트가 없습니다. 테스트 파일이 Assets/Tests/PlayMode/ 에 있는지 확인하세요." });

                var log = $"테스트 {report.Passed}/{report.Total} 통과";
                return report.Failed == 0
                    ? new VerifyResult(true, log, Array.Empty<string>())
                    : new VerifyResult(false, log, report.Failures);
            }
            await Task.Delay(1500, ct);
        }

        return new VerifyResult(false, "test_status 폴링 타임아웃", new[] { "<test timeout>" });
    }

    private sealed record TestReport(bool Completed, int Total, int Passed, int Failed, IReadOnlyList<string> Failures);

    private static TestReport? ParseTestStatus(string stdout)
    {
        try
        {
            using var outer = JsonDocument.Parse(stdout.Trim());
            if (!outer.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("result", out var result))
                return null;

            // data.result 는 JSON 문자열로 온다.
            using var inner = result.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(result.GetString() ?? "{}")
                : null;
            var obj = inner?.RootElement ?? result;

            var status = obj.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (status != "completed")
                return new TestReport(false, 0, 0, 0, Array.Empty<string>());

            int total = 0, passed = 0, failed = 0;
            if (obj.TryGetProperty("summary", out var sum))
            {
                total = sum.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                passed = sum.TryGetProperty("passed", out var p) ? p.GetInt32() : 0;
                failed = sum.TryGetProperty("failed", out var f) ? f.GetInt32() : 0;
            }

            // 실패한 테스트만 뽑아 모델이 고칠 수 있게 이름 + 메시지로 만든다.
            var failures = new List<string>();
            if (obj.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in results.EnumerateArray())
                {
                    var s = r.TryGetProperty("Status", out var rs) ? rs.GetString() : null;
                    if (s is null || s.Equals("Passed", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = r.TryGetProperty("FullName", out var fn) ? fn.GetString() : "(이름 없음)";
                    var msg = r.TryGetProperty("Message", out var m) && m.ValueKind == JsonValueKind.String
                        ? Flatten(m.GetString() ?? "")
                        : "";
                    failures.Add(msg.Length > 0 ? $"{name}: {msg}" : $"{name}: {s}");
                }
            }

            return new TestReport(true, total, passed, failed, failures);
        }
        catch
        {
            return null;
        }
    }

    // ── ③-c 성능 검증 (프로파일링) ──────────────────────────────────────────────
    // "동작 정상 ≠ 충분히 빠름". 핫패스를 실제로 N회 돌려 경과 시간을 재고 예산과 비교한다.
    // 측정 스니펫은 오케스트레이터(PerfHarness)가 만든다 — 백엔드는 무엇을 부를지와 예산만 선언한다.
    private async Task<VerifyResult> VerifyPerformanceAsync(string perfJson, CancellationToken ct)
    {
        PerfSpec spec;
        try
        {
            spec = PerfHarness.Parse(perfJson);
        }
        catch (Exception ex)
        {
            return new VerifyResult(false, perfJson, new[] { $"PERF 블록 해석 실패: {ex.Message}" });
        }

        var snippet = PerfHarness.BuildSnippet(spec);
        var unsafeHits = _allowUnsafeEval ? Array.Empty<string>() : SnippetGuard.Inspect(snippet);
        if (unsafeHits.Count > 0)
            return new VerifyResult(false, "", unsafeHits.Select(h => "안전 위반: " + h).ToArray());

        await WaitUntilReadyAsync(ct);
        if (!await EnterPlayModeAsync(ct))
            throw new InvalidOperationException("플레이모드 진입 실패 — 성능 측정을 수행할 수 없습니다.");

        try
        {
            var (res, raw) = await EvalWithRetryAsync(snippet, ct);

            if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var elapsedMs))
            {
                return new VerifyResult(false, res.StdOut, new[] { $"성능 측정 실행 실패: {raw}" });
            }

            // 프로파일링 맥락(드로우콜·메모리·프레임타임)도 함께 남긴다 — 판정 기준은 아니지만 진단에 쓰인다.
            var stats = await ReadPerformanceStatsAsync(ct);
            var perCall = elapsedMs / spec.Iterations * 1000.0; // 마이크로초
            var log = $"{spec.Component}: {spec.Iterations}회 {elapsedMs:F2}ms (호출당 {perCall:F2}µs)  {stats}";

            return elapsedMs <= spec.MaxTotalMs
                ? new VerifyResult(true, log, Array.Empty<string>())
                : new VerifyResult(false, log, new[]
                {
                    $"성능 예산 초과 — {spec.Component} 를 {spec.Iterations}회 호출하는 데 " +
                    $"{elapsedMs:F2}ms 걸렸습니다(예산 {spec.MaxTotalMs:F2}ms, 호출당 {perCall:F2}µs). " +
                    "핫패스에서 매 호출 할당하거나 불필요한 작업을 하고 있지 않은지 확인하세요.",
                });
        }
        finally
        {
            await RunCommandAsync("editor_stop", CancellationToken.None);
        }
    }

    /// <summary>
    /// eval 실행 + 1회 재시도. 반환은 (원본 응답, 해석된 값/메시지).
    ///
    /// 왜 재시도가 필요한가(실측): 테스트 러너처럼 플레이모드 진입·이탈로 **도메인 리로드**를 유발한 직후엔
    /// eval 의 컴파일 컨텍스트가 새 어셈블리(`AgentLoop.Runtime`)를 아직 모를 수 있다.
    /// 그때 "The type or namespace name 'X' could not be found" 로 실패하는데, 잠시 뒤엔 정상 해석된다.
    /// 모델의 코드 잘못이 아닌 인프라 타이밍이므로 조용히 한 번 더 시도한다.
    /// </summary>
    private async Task<(ProcessResult Res, string Value)> EvalWithRetryAsync(string code, CancellationToken ct)
    {
        var res = await RunCommandAsync("eval", ct, code);
        var (_, value) = ParseEvalOutcome(res.StdOut);

        if (LooksLikeStaleAssembly(value))
        {
            await Task.Delay(2500, ct);
            await WaitUntilReadyAsync(ct);
            res = await RunCommandAsync("eval", ct, code);
            (_, value) = ParseEvalOutcome(res.StdOut);
        }
        return (res, value);
    }

    private static bool LooksLikeStaleAssembly(string message) =>
        message.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("are you missing", StringComparison.OrdinalIgnoreCase);

    // ── 시각 증거 캡처 ──────────────────────────────────────────────────────────
    /// <summary>
    /// 게임 뷰를 PNG 로 캡처해 <paramref name="destinationPath"/> 에 남긴다.
    ///
    /// 주의(실측): `capture_game_view --save_path` 는 절대경로를 주더라도 **Assets/ 아래로 가둔다**
    /// (pipeline 의 authoring root 제약). 그래서 Assets/ 밑 임시 폴더로 찍은 뒤 밖으로 옮기고,
    /// Unity 가 만든 .meta 까지 정리한다.
    ///
    /// 이건 **합격 판정이 아니라 증거 수집**이다. 기준 이미지 없이 "화면이 맞다"를 판정할 수는 없고,
    /// 다만 렌더 결과가 사실상 비어 있으면(PNG 가 극단적으로 작으면) 경고를 남긴다.
    /// </summary>
    public async Task<string?> CaptureEvidenceAsync(string destinationPath, CancellationToken ct)
    {
        const string tempDir = "__agentloop_capture";
        var relative = $"{tempDir}/shot.png";
        var stagedFull = Path.Combine(_projectPath, "Assets", tempDir, "shot.png");

        try
        {
            var res = await RunCommandAsync("capture_game_view", ct,
                "--width", "960", "--height", "540", "--save_path", relative);

            if (!File.Exists(stagedFull))
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(stagedFull, destinationPath, overwrite: true);

            var bytes = new FileInfo(destinationPath).Length;
            CleanupStaging(Path.Combine(_projectPath, "Assets", tempDir));

            // 균일한 화면(아무것도 안 그려짐)은 PNG 가 극단적으로 작게 압축된다 — 경고용 휴리스틱.
            return bytes < 2048 ? $"{destinationPath} (⚠ {bytes}B — 화면이 비어 있을 수 있음)" : destinationPath;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static void CleanupStaging(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            var meta = dir + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
        catch { /* 정리 실패는 무시 */ }
    }

    /// <summary>프로파일링 통계(드로우콜·메모리·프레임타임)를 한 줄로 요약한다.</summary>
    private async Task<string> ReadPerformanceStatsAsync(CancellationToken ct)
    {
        try
        {
            var res = await RunCommandAsync("get_performance_stats", ct);
            using var doc = JsonDocument.Parse(res.StdOut.Trim());
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("result", out var r))
                return "";

            var draws = r.TryGetProperty("render", out var render) &&
                        render.TryGetProperty("drawCalls", out var dc) ? dc.ToString() : "?";
            var mono = r.TryGetProperty("memory", out var mem) &&
                       mem.TryGetProperty("monoUsedBytes", out var mu) && mu.TryGetInt64(out var b)
                       ? $"{b / (1024.0 * 1024.0):F0}MB" : "?";
            var cpu = r.TryGetProperty("frameTiming", out var ft) &&
                      ft.TryGetProperty("cpuFrameTimeMs", out var cf) ? cf.ToString() : "?";

            return $"[프로파일: drawCalls={draws} mono={mono} cpuFrame={cpu}ms]";
        }
        catch
        {
            return "";
        }
    }

    // 진입 실패는 대개 일시적(리로드 타이밍)이므로 한 번 정리 후 재시도한다.
    private async Task<bool> EnterPlayModeAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await RunCommandAsync("editor_play", ct);
            if (await WaitForPlayModeAsync("playing", PlayModeEnterTimeoutSec, ct))
                return true;

            await RunCommandAsync("editor_stop", CancellationToken.None);
            await WaitUntilReadyAsync(ct);
        }
        return false;
    }

    private const int PlayModeEnterTimeoutSec = 30;

    private async Task<bool> WaitForPlayModeAsync(string expected, int timeoutSec, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            var res = await RunCommandAsync("editor_status", ct);
            if (ReadStatusField(res.StdOut, "playMode") == expected)
                return true;
            await Task.Delay(700, ct);
        }
        return false;
    }

    /// <summary>컴파일·도메인리로드가 끝나 에디터가 명령을 받을 수 있을 때까지 기다린다.</summary>
    private async Task WaitUntilReadyAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            var res = await RunCommandAsync("editor_status", ct);
            var compiling = ReadStatusField(res.StdOut, "compiling");
            var reloading = ReadStatusField(res.StdOut, "domainReloadInProgress");
            var status = ReadStatusField(res.StdOut, "status");

            if (status == "ready" && compiling == "False" && reloading == "False")
                return;

            await Task.Delay(700, ct);
        }
        // 타임아웃이어도 진입을 시도해 본다(재시도 로직이 한 번 더 커버).
    }

    // editor_status 의 data.result.<field> 를 문자열로 읽는다
    // (playMode: "playing"|"stopped", compiling/domainReloadInProgress: True|False, status: "ready" 등).
    private static string? ReadStatusField(string stdout, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim());
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty(field, out var v))
                return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        }
        catch { /* 파싱 실패 = 아직 응답 불가 */ }
        return null;
    }

    // eval 결과를 (통과여부, 메시지)로 해석한다.
    //   실패 봉투: { success:false, errors:[{ message }] }              ← 컴파일/런타임 예외
    //   성공 봉투: { data:{ result:{ result:<반환값>, error:<...> } } }  ← 스니펫의 return 값
    // 반환값이 "OK"/true 면 통과, 그 외 문자열은 실패 사유로 그대로 피드백한다.
    private static (bool Ok, string Message) ParseEvalOutcome(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim());
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var outer) && outer.ValueKind == JsonValueKind.False)
            {
                var msg = "eval 실패";
                if (root.TryGetProperty("errors", out var errs) &&
                    errs.ValueKind == JsonValueKind.Array &&
                    errs.GetArrayLength() > 0 &&
                    errs[0].TryGetProperty("message", out var m))
                    msg = m.GetString() ?? msg;
                return (false, Flatten(msg));
            }

            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("result", out var evalObj) &&
                evalObj.ValueKind == JsonValueKind.Object)
            {
                if (evalObj.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                    return (false, Flatten(err.GetString() ?? "eval error"));

                if (evalObj.TryGetProperty("result", out var val))
                {
                    var text = val.ValueKind switch
                    {
                        JsonValueKind.String => val.GetString() ?? "",
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "<null>",
                        _ => val.ToString(),
                    };
                    var t = text.Trim();
                    var ok = t.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                             t.Equals("true", StringComparison.OrdinalIgnoreCase);
                    return (ok, t.Length == 0 ? "<assert 반환값 없음>" : t);
                }
            }
        }
        catch { /* 아래 폴백 */ }

        return (false, "eval 결과를 해석하지 못했습니다.");
    }

    private static string Flatten(string s) =>
        s.Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>
    /// 전제 확인: pipeline 서버에 연결되나(에디터가 열려 있고 서버가 떠 있나).
    /// recompile_status 가 파싱되면 연결된 것 — 안 되면 AI 호출 전에 빠르게 실패시키는 용도.
    /// </summary>
    public async Task<bool> IsConnectedAsync(CancellationToken ct)
    {
        try
        {
            var res = await RunCommandAsync("recompile_status", ct);
            return ParseRecompileStatus(res.StdOut) is not null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    // ── pipeline 명령/eval 원시 호출 ───────────────────────────────────────────
    private Task<ProcessResult> RunCommandAsync(string command, CancellationToken ct, params string[] extraArgs)
    {
        var args = new List<string> { "command", command };
        args.AddRange(extraArgs);
        args.AddRange(new[] { "--project-path", _projectPath, "--json", "--no-banner", "--timeout", _timeoutSec.ToString() });
        return ProcessRunner.RunAsync(_unityExe, args, workingDir: _projectPath, ct);
    }

    /// <summary>C# 을 에디터에서 즉시 실행(Phase 2 플레이모드 assert 훅). 반환값 문자열을 돌려준다.</summary>
    public async Task<string> EvalAsync(string csharp, CancellationToken ct)
    {
        var res = await RunCommandAsync("eval", ct, csharp);
        return ExtractDataResultRaw(res.StdOut);
    }

    // ── JSON 파싱 ──────────────────────────────────────────────────────────────
    private sealed record RecompileStatus(string Status, bool Failed, IReadOnlyList<string> Errors)
    {
        public bool Terminal => Status is "completed" or "up_to_date" or "idle";
    }

    // recompile_status 의 --json 봉투에서 { status, failed, errors } 를 뽑는다.
    // data.result 가 JSON "문자열"이므로(escaped) 한 번 더 파싱한다.
    private static RecompileStatus? ParseRecompileStatus(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim());
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("result", out var result))
                return null;

            using var inner = result.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(result.GetString() ?? "{}")
                : null;
            var obj = inner?.RootElement ?? result;

            var status = obj.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            var failed = obj.TryGetProperty("failed", out var f) && f.ValueKind == JsonValueKind.True;
            var errors = new List<string>();
            if (obj.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
                foreach (var item in e.EnumerateArray())
                    if (item.GetString() is { Length: > 0 } msg)
                        errors.Add(msg);

            return new RecompileStatus(status, failed, errors);
        }
        catch
        {
            return null;
        }
    }

    // eval 등의 data.result 를 원문 문자열로 뽑는다(문자열/객체 모두 허용).
    private static string ExtractDataResultRaw(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim());
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("result", out var result))
                return result.ValueKind == JsonValueKind.String ? result.GetString() ?? "" : result.ToString();
        }
        catch { /* JSON 아니면 원문 */ }
        return stdout;
    }

    /// <summary>unity.exe 경로 해석: 환경변수 UNITY_EXE → LocalAppData 기본경로 → PATH의 "unity".</summary>
    public static string ResolveUnityExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("UNITY_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var guess = Path.Combine(local, "Unity", "bin", "unity.exe");
        if (File.Exists(guess))
            return guess;

        return "unity"; // PATH 에 있길 기대
    }
}
