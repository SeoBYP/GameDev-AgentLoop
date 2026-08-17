using Orchestrator.Contracts;
using Orchestrator.Skills;
using Orchestrator.Trace;
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
    private readonly RunTrace? _trace;

    public AgentLoop(
        IAgentBackend backend,
        IExecTarget target,
        LoopOptions options,
        IReadOnlyList<Skill>? skills = null,
        Action<string>? log = null,
        RunTrace? trace = null)
    {
        _backend = backend;
        _target = target;
        _options = options;
        _skills = skills ?? Array.Empty<Skill>();
        _log = log ?? Console.WriteLine;
        _trace = trace;
    }

    // 트레이스가 없으면(레거시 호출) 아무것도 기록하지 않는 더미 스코프를 쓴다 —
    // 호출부가 매번 null 검사를 하지 않도록.
    private SpanScope? Begin(SpanKind kind, string name) => _trace?.Begin(kind, name);

    public async Task<LoopResult> RunAsync(string goal, CancellationToken ct)
    {
        _log($"Goal: {goal}");
        _log($"Backend: {_backend.Name}   Target: {_target.Name}   max steps: {_options.MaxSteps}");
        _log(_skills.Count > 0
            ? $"Skills: {string.Join(", ", _skills.Select(s => s.Name))} ({_skills.Sum(s => s.Checks.Count)} checks)"
            : "Skills: none (--skills off)");

        var system = BuildSystemPrompt(_target, _skills);
        var history = new List<Turn> { new(Role.User, BuildInitialPrompt(goal)) };

        // 이 실행에서 테스트 파일이 한 번이라도 적용됐는지.
        // 이후 스텝이 구현만 고쳐 보내도 테스트는 이미 프로젝트에 있으므로 테스트 러너로 검증해야 한다.
        var testsInProject = false;

        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            _log($"\n──────── step {step}/{_options.MaxSteps} ────────");
            using var stepSpan = Begin(SpanKind.Phase, $"step {step}");

            // ① 생성 — 오래된 시도는 잘라 보낸다(계약이 "매번 전체 파일"이라 최신 것만 있으면 충분).
            var sent = Trim(history);
            _log($"  (context {Feedback.ApproxChars(system, sent.Select(t => t.Content)):N0} chars / {sent.Count} turns)");

            var genSpan = Begin(SpanKind.Node, "Generate");
            var reply = await _backend.CompleteAsync(new AgentContext(system, sent), ct);
            history.Add(new Turn(Role.Assistant, reply.Text));
            _log($"① generate → {reply.Edits.Count} file edit(s)");
            genSpan?.Artifact("reply.txt", reply.Text);

            if (reply.Edits.Count == 0)
            {
                _log("   (no FILE block parsed — asking again for the format)");
                genSpan?.Fail(log: "no FILE block parsed").Dispose();
                history.Add(new Turn(Role.User, NoEditsFeedback));
                continue;
            }
            genSpan?.Pass($"{reply.Edits.Count} file edit(s)  [{_backend.Name}]").Dispose();

            // ①-b 스킬 정적 검사 — 프로젝트에 적용하기 **전에** 품질 위반을 거른다.
            // 지침을 프롬프트로 주는 데서 그치지 않고 검사로 강제하는 게 Phase 3 의 핵심이다.
            if (_skills.Count > 0)
            {
                using var skillSpan = Begin(SpanKind.Node, "SkillCheck");
                var violations = SkillLibrary.Inspect(_skills, reply.Edits);
                if (violations.Count > 0)
                {
                    _log($"①-b check  → {violations.Count} skill violation(s) ❌ (not applied)");
                    foreach (var v in violations.Take(5))
                        _log($"      · {v}");

                    skillSpan?.Fail(violations.Select(v => $"{v.FilePath}: {v.Message}").ToList(),
                                    $"{violations.Count} violation(s)");

                    // ④ 피드백 → 다음 스텝 ①로
                    history.Add(new Turn(Role.User, BuildViolationFeedback(violations)));
                    continue;
                }
                _log("①-b check  → skills passed ✅");
                skillSpan?.Pass($"{_skills.Sum(s => s.Checks.Count)} checks");
            }

            // ② 적용
            var applySpan = Begin(SpanKind.Node, "Apply");
            var apply = await _target.ApplyAsync(reply.Edits, ct);
            _log($"② apply    → {apply.Message}");
            if (!apply.Ok)
            {
                applySpan?.Fail(log: apply.Message).Dispose();
                history.Add(new Turn(Role.User,
                    $"Apply failed: {apply.Message}. Fix the path and emit the files again."));
                continue;
            }
            applySpan?.Pass(apply.Message).Dispose();

            // ③-a 검증: 컴파일
            var compileSpan = Begin(SpanKind.Node, "VerifyCompile");
            var verify = await _target.VerifyAsync(new VerifySpec(VerifyKind.Compile), ct);
            if (!verify.Ok)
            {
                _log($"③ verify   → {_target.LabelFor(VerifyKind.Compile)}: {verify.Errors.Count} error(s) ❌");
                foreach (var e in verify.Errors.Take(5))
                    _log($"      · {Feedback.Clip(e, 200)}");

                // 모델에는 상위 N건만 가지만, 사람이 볼 전문은 span 에 매달아 둔다.
                compileSpan?.Artifact("compile.log", string.Join("\n", verify.Errors) + "\n\n---\n" + verify.Log)
                            .Fail(verify.Errors, Feedback.Clip(verify.Errors[0], 90)).Dispose();

                // ④ 피드백 → 다음 스텝 ①로
                history.Add(new Turn(Role.User, BuildErrorFeedback(verify.Errors)));
                continue;
            }
            _log($"③ verify   → {_target.LabelFor(VerifyKind.Compile)} passed ✅");
            compileSpan?.Pass().Dispose();

            // ③-b 검증: 런타임 동작
            // 테스트 파일이 왔으면 **테스트 러너**로 검증한다(레포에 남는 자산 + 다중 프레임 가능).
            // 없으면 일회용 ASSERT 스니펫으로 대체한다. 사람이 준 assert 는 언제나 우선.
            // 이번 응답에 테스트가 왔거나, 앞선 스텝에서 이미 적용해 뒀거나.
            testsInProject |= reply.Edits.Any(e => IsTestFile(e.RelativePath));
            var useTests = testsInProject && _options.Assert is null && _target.Supports(VerifyKind.Tests);
            var testsOnly = _options.VerifyMode == VerifyMode.TestsOnly;

            // TestsOnly: eval 을 아예 쓰지 않는다. 테스트가 없으면 통과시키지 않고 되돌려 요구한다.
            if (testsOnly && !useTests)
            {
                _log("③ verify   → no test file ❌ (tests-only mode never runs temporary snippets)");
                Begin(SpanKind.Node, "VerifyTests")?.Fail(log: "no test file (tests-only)").Dispose();
                history.Add(new Turn(Role.User, TestsRequiredFeedback));
                continue;
            }

            var assertCode = testsOnly ? null : (_options.Assert ?? EditParser.ParseAssert(reply.Text));

            if (!useTests && (assertCode is null || !_target.Supports(VerifyKind.RuntimeAssert)))
            {
                // ⑤ 판정 — 런타임 기준이 없거나 타깃이 런타임 검증을 지원하지 않으면 여기까지가 성공 기준.
                var why = assertCode is null ? "no runtime criterion" : "target has no runtime verification";
                Begin(SpanKind.Node, "VerifyRuntime")?.Skip(why).Dispose();
                return new LoopResult(true, step, $"applied and verified in {step} step(s) ({why})");
            }

            var runtimeKind = useTests ? VerifyKind.Tests : VerifyKind.RuntimeAssert;
            var runtimeLabel = _target.LabelFor(runtimeKind);
            var source = useTests ? "test files" : (_options.Assert is not null ? "user-supplied" : "AI-written");
            _log($"③ verify   → running {runtimeLabel} ({source})");

            var runtimeSpan = Begin(SpanKind.Node, useTests ? "VerifyTests" : "VerifyAssert");
            var play = await _target.VerifyAsync(
                new VerifySpec(runtimeKind, useTests ? null : assertCode), ct);

            if (!play.Ok)
            {
                _log($"③ verify   → {runtimeLabel} failed ❌  {play.Log}");
                foreach (var e in play.Errors.Take(3))
                    _log($"      · {Feedback.Clip(e, 200)}");

                runtimeSpan?.Artifact("runtime.log", string.Join("\n", play.Errors) + "\n\n---\n" + play.Log)
                            .Fail(play.Errors, play.Log).Dispose();

                // ④ 피드백 → 다음 스텝 ①로
                history.Add(new Turn(Role.User,
                    useTests ? BuildTestFeedback(play.Errors) : BuildAssertFeedback(play.Errors)));
                continue;
            }
            _log($"③ verify   → {runtimeLabel} passed ✅  {play.Log}");
            runtimeSpan?.Pass(string.IsNullOrWhiteSpace(play.Log) ? source : $"{source} · {play.Log}").Dispose();

            // ③-c 검증: 성능 예산 — **기본적으로 루프 밖이다**(타깃이 Supports 로 선언한다).
            // 에디터 측정치는 출시 성능이 아니라 상대 신호이고, 정확성과는 주기가 다른 질문이다.
            // PERF 블록은 eval 로 측정하므로 tests-only 모드에서도 건너뛴다.
            var perfSpec = testsOnly ? null : EditParser.ParsePerf(reply.Text);
            if (perfSpec is null || !_target.Supports(VerifyKind.Performance))
            {
                // ⑤ 판정
                Begin(SpanKind.Node, "VerifyPerf")?.Skip(perfSpec is null ? "no PERF block" : "unsupported").Dispose();
                await CaptureEvidenceAsync(goal, ct);
                return new LoopResult(true, step, $"applied and runtime-verified in {step} step(s)");
            }

            var perfLabel = _target.LabelFor(VerifyKind.Performance);
            var perfSpan = Begin(SpanKind.Node, "VerifyPerf");
            var perf = await _target.VerifyAsync(new VerifySpec(VerifyKind.Performance, perfSpec), ct);

            // ⑤ 판정
            if (perf.Ok)
            {
                _log($"③ verify   → {perfLabel} passed ✅  {perf.Log}");
                perfSpan?.Pass(perf.Log).Dispose();
                await CaptureEvidenceAsync(goal, ct);
                return new LoopResult(true, step, $"behavior AND performance verified in {step} step(s)");
            }

            _log($"③ verify   → {perfLabel} exceeded ❌  {perf.Log}");
            foreach (var e in perf.Errors.Take(3))
                _log($"      · {e}");
            perfSpan?.Fail(perf.Errors, perf.Log).Dispose();

            // ④ 피드백 → 다음 스텝 ①로
            history.Add(new Turn(Role.User, BuildPerfFeedback(perf.Errors)));
        }

        return new LoopResult(false, _options.MaxSteps,
            $"gave up after {_options.MaxSteps} step(s) — still failing");
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
    // 피드백은 모두 영어다 — 시스템 프롬프트가 영어라 섞으면 모델이 형식을 흔든다.
    private static string BuildInitialPrompt(string goal) =>
        $"GOAL: {goal}\n\nProduce the files that satisfy this goal, using the output contract " +
        "(FILE: followed by a fenced code block).";

    private const string TestsRequiredFeedback = """
        This run verifies ONLY through compiled test files — temporary snippets are never executed.
        Emit a PlayMode test file as a FILE block alongside the implementation.
        If performance matters for this behavior, measure it inside the test with a Stopwatch and assert on it.
        """;

    private const string NoEditsFeedback =
        "No FILE block was found in your response. Emit 'FILE: <path>' followed on the next line by a " +
        "fenced code block containing the COMPLETE file.";

    private static string BuildErrorFeedback(IReadOnlyList<string> errors)
    {
        var list = Feedback.Bullets(errors);
        return $"""
            Compilation FAILED. Fix these compiler errors and emit the COMPLETE file(s) again
            (no diffs, no partial edits — full file contents):

            {list}
            """;
    }

    // 도메인 스킬 위반 피드백. 파일이 프로젝트에 적용되기 전 단계라 "다시 내라"가 명확하다.
    private static string BuildViolationFeedback(IReadOnlyList<SkillViolation> violations)
    {
        var list = Feedback.Bullets(violations.Select(v => $"{v.FilePath}: {v.Message}").ToList());
        return $"""
            DOMAIN RULE violations were found, so nothing was applied:

            {list}

            Fix the implementation so it follows the rules, then emit the COMPLETE file(s) again.
            (For example: whatever you were looking up per-frame in Update should be resolved once in
            Awake or Start and cached in a field.)
            """;
    }

    /// <summary>
    /// 히스토리를 최근 N턴으로 자른다(목표 턴은 항상 유지).
    /// 출력 계약이 "매번 전체 파일"이므로 과거 시도는 최신 응답으로 대체된다 — 버려도 안전하다.
    /// </summary>
    private IReadOnlyList<Turn> Trim(List<Turn> history)
    {
        var window = _options.HistoryWindow;
        if (window <= 0 || history.Count <= window + 1)
            return history;

        var kept = new List<Turn> { history[0] };            // 목표
        kept.AddRange(history.Skip(history.Count - window));  // 최근 N턴
        return kept;
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
                _log($"📸 evidence: {saved}");
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
        var list = Feedback.Bullets(failures);
        return $"""
            The Unity Test Runner reports FAILING TESTS:

            {list}

            Fix the ROOT CAUSE in the implementation and emit the COMPLETE file(s) again.
            Do NOT weaken the tests to make them pass — the criteria stay, the behavior changes.
            """;
    }

    // 동작은 맞지만 성능 예산을 넘긴 경우의 피드백.
    // "예산을 늘려라"가 아니라 "구현을 빠르게 고쳐라"를 명시한다.
    private static string BuildPerfFeedback(IReadOnlyList<string> failures)
    {
        var list = Feedback.Bullets(failures);
        return $"""
            The behavior is correct, but it EXCEEDED THE PERFORMANCE BUDGET (measured):

            {list}

            Remove the per-call cost from the hot path and emit the COMPLETE file(s) again.
            Common causes: allocating a new collection/array per call, string concatenation or
            interpolation, LINQ, boxing, looking up components every call.
            → Hoist reusable buffers into fields; resolve lookups once in Awake/Start and cache them.
            Do NOT raise maxTotalMs in the PERF block to pass — the budget stays, the implementation changes.
            """;
    }

    // 컴파일은 통과했지만 런타임 동작이 틀린 경우의 피드백.
    // "구현을 고쳐라"를 명시한다 — assert 를 느슨하게 고쳐 통과시키는 쪽으로 새는 걸 막기 위해서.
    private static string BuildAssertFeedback(IReadOnlyList<string> failures)
    {
        var list = Feedback.Bullets(failures);
        return $"""
            It compiles, but the PLAY MODE RUNTIME CHECK FAILED:

            {list}

            Fix the ROOT CAUSE in the implementation (the FILE) and emit the COMPLETE file(s) again.
            Do NOT weaken the assert to make it pass — the criteria stay, the behavior changes.
            """;
    }
}
