using Orchestrator.Contracts;
using Orchestrator.Skills;
using Orchestrator.Util;

namespace Orchestrator.Loop;

/// <summary>
/// 루프 소유자. DESIGN.md §5 의 5단계를 그대로 구현한다:
///
///   목표(자연어)
///    → ① 생성   backend.CompleteAsync           (텍스트/파일편집 out)
///    → ② 적용   target.ApplyAsync               (파일쓰기 + 리컴파일)
///    → ③ 검증   target.VerifyAsync              (컴파일 에러?)
///    → ④ 피드백  에러를 History에 넣어 백엔드로 되돌림
///    → ⑤ 판정   통과 → 종료 / 실패 → ①로 (maxSteps 가드)
///
/// 이 클래스가 "루프는 우리 것"(D1)의 실체다. 백엔드/타깃은 인터페이스 뒤에 있고,
/// 적용·검증·재시도·판정의 흐름은 전적으로 여기서 소유한다.
/// </summary>
public sealed class AgentLoop
{
    private readonly IAgentBackend _backend;
    private readonly IExecTarget _target;
    private readonly LoopOptions _options;
    private readonly IReadOnlyList<Skill> _skills;
    private readonly Action<string> _log;

    public AgentLoop(
        IAgentBackend backend,
        IExecTarget target,
        LoopOptions options,
        IReadOnlyList<Skill>? skills = null,
        Action<string>? log = null)
    {
        _backend = backend;
        _target = target;
        _options = options;
        _skills = skills ?? Array.Empty<Skill>();
        _log = log ?? Console.WriteLine;
    }

    public async Task<LoopResult> RunAsync(string goal, CancellationToken ct)
    {
        _log($"목표: {goal}");
        _log($"백엔드: {_backend.Name}   타깃: {_target.Name}   maxSteps: {_options.MaxSteps}");
        _log(_skills.Count > 0
            ? $"스킬: {string.Join(", ", _skills.Select(s => s.Name))} ({_skills.Sum(s => s.Checks.Count)}개 검사)"
            : "스킬: 없음 (--skills off)");

        var system = BuildSystemPrompt(_target, _skills);
        var history = new List<Turn> { new(Role.User, BuildInitialPrompt(goal)) };

        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            _log($"\n──────── step {step}/{_options.MaxSteps} ────────");

            // ① 생성
            var reply = await _backend.CompleteAsync(new AgentContext(system, history), ct);
            history.Add(new Turn(Role.Assistant, reply.Text));
            _log($"① 생성  → 파일 편집 {reply.Edits.Count}개");

            if (reply.Edits.Count == 0)
            {
                _log("   (파싱된 FILE 블록 없음 — 형식 재요청)");
                history.Add(new Turn(Role.User, NoEditsFeedback));
                continue;
            }

            // ①-b 스킬 정적 검사 — 프로젝트에 적용하기 **전에** 품질 위반을 거른다.
            // 지침을 프롬프트로 주는 데서 그치지 않고 검사로 강제하는 게 Phase 3 의 핵심이다.
            if (_skills.Count > 0)
            {
                var violations = SkillLibrary.Inspect(_skills, reply.Edits);
                if (violations.Count > 0)
                {
                    _log($"①-b 검사 → 스킬 위반 {violations.Count}건 ❌ (적용하지 않음)");
                    foreach (var v in violations.Take(5))
                        _log($"      · {v}");

                    // ④ 피드백 → 다음 스텝 ①로
                    history.Add(new Turn(Role.User, BuildViolationFeedback(violations)));
                    continue;
                }
                _log("①-b 검사 → 스킬 통과 ✅");
            }

            // ② 적용
            var apply = await _target.ApplyAsync(reply.Edits, ct);
            _log($"② 적용  → {apply.Message}");
            if (!apply.Ok)
            {
                history.Add(new Turn(Role.User, $"적용 실패: {apply.Message}. 경로를 고쳐 다시 출력하세요."));
                continue;
            }

            // ③-a 검증: 컴파일
            var verify = await _target.VerifyAsync(new VerifySpec(VerifyKind.Compile), ct);
            if (!verify.Ok)
            {
                _log($"③ 검증  → {_target.LabelFor(VerifyKind.Compile)} 에러 {verify.Errors.Count}건 ❌");
                foreach (var e in verify.Errors.Take(5))
                    _log($"      · {e}");

                // ④ 피드백 → 다음 스텝 ①로
                history.Add(new Turn(Role.User, BuildErrorFeedback(verify.Errors)));
                continue;
            }
            _log($"③ 검증  → {_target.LabelFor(VerifyKind.Compile)} 통과 ✅");

            // ③-b 검증: 런타임 동작
            // 테스트 파일이 왔으면 **테스트 러너**로 검증한다(레포에 남는 자산 + 다중 프레임 가능).
            // 없으면 일회용 ASSERT 스니펫으로 대체한다. 사람이 준 assert 는 언제나 우선.
            var hasTests = reply.Edits.Any(e => IsTestFile(e.RelativePath));
            var useTests = hasTests && _options.Assert is null && _target.Supports(VerifyKind.Tests);
            var assertCode = _options.Assert ?? EditParser.ParseAssert(reply.Text);

            if (!useTests && (assertCode is null || !_target.Supports(VerifyKind.RuntimeAssert)))
            {
                // ⑤ 판정 — 런타임 기준이 없거나 타깃이 런타임 검증을 지원하지 않으면 여기까지가 성공 기준.
                var why = assertCode is null ? "런타임 검증 기준 없음" : "타깃이 런타임 검증 미지원";
                return new LoopResult(true, step, $"{step}스텝 만에 적용·검증 통과 ({why})");
            }

            var runtimeKind = useTests ? VerifyKind.Tests : VerifyKind.RuntimeAssert;
            var runtimeLabel = _target.LabelFor(runtimeKind);
            var source = useTests ? "테스트 파일" : (_options.Assert is not null ? "사람 지정" : "AI 생성");
            _log($"③ 검증  → {runtimeLabel} 실행 ({source})");

            var play = await _target.VerifyAsync(
                new VerifySpec(runtimeKind, useTests ? null : assertCode), ct);

            if (!play.Ok)
            {
                _log($"③ 검증  → {runtimeLabel} 실패 ❌  {play.Log}");
                foreach (var e in play.Errors.Take(3))
                    _log($"      · {e}");

                // ④ 피드백 → 다음 스텝 ①로
                history.Add(new Turn(Role.User,
                    useTests ? BuildTestFeedback(play.Errors) : BuildAssertFeedback(play.Errors)));
                continue;
            }
            _log($"③ 검증  → {runtimeLabel} 통과 ✅  {play.Log}");

            // ③-c 검증: 성능 예산 (프로파일링) — "동작 정상 ≠ 충분히 빠름"
            var perfSpec = _options.NoPerf ? null : EditParser.ParsePerf(reply.Text);
            if (perfSpec is null || !_target.Supports(VerifyKind.Performance))
            {
                // ⑤ 판정
                await CaptureEvidenceAsync(goal, ct);
                return new LoopResult(true, step, $"{step}스텝 만에 적용 + 런타임 동작 검증 통과");
            }

            var perfLabel = _target.LabelFor(VerifyKind.Performance);
            var perf = await _target.VerifyAsync(new VerifySpec(VerifyKind.Performance, perfSpec), ct);

            // ⑤ 판정
            if (perf.Ok)
            {
                _log($"③ 검증  → {perfLabel} 통과 ✅  {perf.Log}");
                await CaptureEvidenceAsync(goal, ct);
                return new LoopResult(true, step, $"{step}스텝 만에 동작 + 성능까지 검증 통과");
            }

            _log($"③ 검증  → {perfLabel} 초과 ❌  {perf.Log}");
            foreach (var e in perf.Errors.Take(3))
                _log($"      · {e}");

            // ④ 피드백 → 다음 스텝 ①로
            history.Add(new Turn(Role.User, BuildPerfFeedback(perf.Errors)));
        }

        return new LoopResult(false, _options.MaxSteps, $"maxSteps({_options.MaxSteps}) 초과 — 미해결");
    }

    // ── 프롬프트/피드백 (출력 계약을 시스템 프롬프트로 강제 — DESIGN.md §4) ──────────

    // 루프가 소유하는 건 **형식** 계약뿐이다. 언어·경로·검증 스니펫 같은 **내용 규격**은
    // 타깃(IExecTarget.GenerationBrief)이 준다 — 손이 바뀌면 만들 것도 바뀌기 때문(D5).
    private const string FormatContract = """
        You are a code generator running inside an automated apply → verify → repair loop.

        OUTPUT CONTRACT (STRICT):
        - Emit every file exactly as:
        FILE: <path relative to the project root>
        ```
        <complete file content>
        ```
        - Always output the FULL file content. No diffs, no "// ...", no ellipses, no omissions.
        - Keep any prose to at most one short line; the FILE blocks are what matter.
        - When you receive errors from the verification step, fix the ROOT CAUSE in the
          implementation and re-emit the COMPLETE corrected file(s).
        """;

    /// <summary>
    /// 시스템 프롬프트 = [루프의 형식 계약] + [타깃의 생성 규격] + [선택된 스킬의 지침].
    /// 세 조각의 출처가 다르다는 게 설계의 핵심이다 — 형식은 루프, 내용은 손, 품질은 스킬.
    /// (`--print-prompt` 로 조립 결과를 그대로 볼 수 있다.)
    /// </summary>
    public static string BuildSystemPrompt(IExecTarget target, IReadOnlyList<Skill> skills)
    {
        var guidance = SkillLibrary.BuildGuidance(skills);
        return FormatContract
             + "\n\n" + target.GenerationBrief
             + (guidance.Length == 0 ? string.Empty : "\n\n" + guidance);
    }

    // 목표만 전달한다. 어떤 언어로 어디에 만들지는 타깃의 GenerationBrief 가 이미 시스템 프롬프트에 넣었다.
    private static string BuildInitialPrompt(string goal) =>
        $"목표: {goal}\n\n위 목표를 만족하는 파일을 출력 계약(FILE: + 펜스 코드블록) 형식으로 생성하세요.";

    private const string NoEditsFeedback =
        "응답에서 FILE 블록을 하나도 찾지 못했습니다. 반드시 'FILE: <경로>' 다음 줄에 펜스 코드블록으로 전체 파일을 출력하세요.";

    private static string BuildErrorFeedback(IReadOnlyList<string> errors)
    {
        var list = string.Join("\n", errors.Select(e => "  - " + e));
        return $"""
            컴파일에 실패했습니다. 아래 컴파일러 에러를 고쳐서 전체 파일을 다시 출력하세요
            (부분 수정/diff 금지 — 전체 파일 재출력):

            {list}
            """;
    }

    // 도메인 스킬 위반 피드백. 파일이 프로젝트에 적용되기 전 단계라 "다시 내라"가 명확하다.
    private static string BuildViolationFeedback(IReadOnlyList<SkillViolation> violations)
    {
        var list = string.Join("\n", violations.Select(v => $"  - {v.FilePath}: {v.Message}"));
        return $"""
            도메인 규칙(DOMAIN RULES) 위반이 발견되어 적용하지 않았습니다:

            {list}

            규칙을 지키도록 구현을 고쳐 전체 파일을 다시 출력하세요
            (예: Update 계열에서 쓰던 탐색/캐싱 대상은 Awake 또는 Start 에서 한 번만 확보해 필드에 보관).
            """;
    }

    // 성공 시 결과 화면을 남긴다(옵션). 판정에는 영향을 주지 않는 **증거 수집**이다.
    private async Task CaptureEvidenceAsync(string goal, CancellationToken ct)
    {
        if (_options.CaptureDir is null)
            return;

        var name = new string(goal.Take(40).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var dest = Path.Combine(_options.CaptureDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{name}.png");

        try
        {
            var saved = await _target.CaptureEvidenceAsync(dest, ct);
            if (saved is not null)
                _log($"📸 결과 화면: {saved}");
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 증거 수집 실패가 판정을 뒤집지는 않는다 */ }
    }

    /// <summary>테스트 파일인가(경로 규약). 테스트가 오면 일회용 assert 대신 테스트 러너로 검증한다.</summary>
    private static bool IsTestFile(string relativePath) =>
        relativePath.Replace('\\', '/').Contains("/Tests/", StringComparison.OrdinalIgnoreCase) &&
        relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    // 테스트 실패 피드백. 실패한 테스트 이름·메시지를 그대로 준다.
    private static string BuildTestFeedback(IReadOnlyList<string> failures)
    {
        var list = string.Join("\n", failures.Select(e => "  - " + e));
        return $"""
            Unity 테스트 러너에서 **테스트가 실패**했습니다:

            {list}

            구현의 근본 원인을 고쳐 전체 파일을 다시 출력하세요.
            테스트를 느슨하게 고쳐 통과시키지 마세요 — 검증 기준은 그대로 두고 동작을 고쳐야 합니다.
            """;
    }

    // 동작은 맞지만 성능 예산을 넘긴 경우의 피드백.
    // "예산을 늘려라"가 아니라 "구현을 빠르게 고쳐라"를 명시한다.
    private static string BuildPerfFeedback(IReadOnlyList<string> failures)
    {
        var list = string.Join("\n", failures.Select(e => "  - " + e));
        return $"""
            동작은 맞지만 **성능 예산을 초과**했습니다(실측):

            {list}

            핫패스에서 매 호출 발생하는 비용을 제거해 구현을 고치고 전체 파일을 다시 출력하세요.
            흔한 원인: 매 호출 컬렉션/배열 새로 생성, 문자열 결합·보간, LINQ, 박싱, 매번 컴포넌트 탐색.
            → 재사용 가능한 버퍼는 필드로 올리고, 탐색 결과는 Awake/Start 에서 캐싱하세요.
            PERF 블록의 예산(maxTotalMs)을 늘려서 통과시키지 마세요 — 기준은 그대로 두고 구현을 고쳐야 합니다.
            """;
    }

    // 컴파일은 통과했지만 런타임 동작이 틀린 경우의 피드백.
    // "구현을 고쳐라"를 명시한다 — assert 를 느슨하게 고쳐 통과시키는 쪽으로 새는 걸 막기 위해서.
    private static string BuildAssertFeedback(IReadOnlyList<string> failures)
    {
        var list = string.Join("\n", failures.Select(e => "  - " + e));
        return $"""
            컴파일은 통과했지만 **플레이모드 런타임 검증에 실패**했습니다:

            {list}

            구현(FILE)의 근본 원인을 고쳐서 전체 파일을 다시 출력하세요.
            assert 를 느슨하게 바꿔 통과시키지 마세요 — 검증 기준은 그대로 두고 동작을 고쳐야 합니다.
            """;
    }
}
