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

    public string Name => _label;

    public UnityEditorTarget(string unityExe, string projectPath, string label, int timeoutSec = 120)
    {
        _unityExe = unityExe;
        _projectPath = projectPath;
        _label = label;
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
        VerifyKind.PlayModeAssert => VerifyPlayModeAsync(
            spec.AssertCode ?? throw new ArgumentException("PlayModeAssert 는 AssertCode 가 필요합니다.", nameof(spec)), ct),
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
            var res = await RunCommandAsync("eval", ct, assertCode);
            var (ok, message) = ParseEvalOutcome(res.StdOut);

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
