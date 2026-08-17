using Orchestrator.Contracts;
using Orchestrator.Loop.Nodes;
using Orchestrator.Skills;
using Orchestrator.Trace;
using Orchestrator.Util;

namespace Orchestrator.Loop;

/// <summary>
/// 루프 소유자 — 이제 **실행기**다.
///
///   목표(자연어)
///    → ① 생성   backend.CompleteAsync
///    → 노드 파이프라인   SkillCheck → Apply → VerifyCompile → VerifyRuntime → VerifyPerf
///    → ④ 피드백  실패한 노드가 쓴 문장을 History 에 넣어 백엔드로 되돌림
///    → ⑤ 판정   전부 Pass/Skip → 종료 / 하나라도 Fail → 다음 시도 (maxSteps 가드)
///
/// **흐름 제어는 노드 밖에 있다**(ARCHITECTURE §5.2). 노드는 자기 판단만 하고,
/// 라우팅·타이밍·기록은 여기가 소유한다. 그래서 "검증을 하나 추가한다"가
/// *이 메서드 수술*이 아니라 *파이프라인 등록*이 된다.
///
/// 이 클래스가 "루프는 우리 것"(D1)의 실체다.
/// </summary>
public sealed class AgentLoop
{
    private readonly IAgentBackend _backend;
    private readonly IExecTarget _target;
    private readonly LoopOptions _options;
    private readonly IReadOnlyList<Skill> _skills;
    private readonly Action<string> _log;
    private readonly RunTrace? _trace;
    private readonly IReadOnlyList<INode> _pipeline;

    public AgentLoop(
        IAgentBackend backend,
        IExecTarget target,
        LoopOptions options,
        IReadOnlyList<Skill>? skills = null,
        Action<string>? log = null,
        RunTrace? trace = null,
        IReadOnlyList<INode>? pipeline = null)
    {
        _backend = backend;
        _target = target;
        _options = options;
        _skills = skills ?? Array.Empty<Skill>();
        _log = log ?? Console.WriteLine;
        _trace = trace;
        _pipeline = pipeline ?? DefaultPipeline;
    }

    /// <summary>
    /// 기본 검증 사슬. 순서가 곧 정책이다 — 싼 검사(정적)를 앞에, 비싼 검사(플레이모드)를 뒤에 둔다.
    /// 성능은 마지막이고, 타깃이 지원하지 않으면 스스로 Skip 한다(§9.4).
    /// </summary>
    public static readonly IReadOnlyList<INode> DefaultPipeline = new INode[]
    {
        new SkillCheckNode(),
        new ApplyNode(),
        new VerifyCompileNode(),
        new VerifyRuntimeNode(),
        new VerifyPerfNode(),
    };

    public async Task<LoopResult> RunAsync(string goal, CancellationToken ct)
    {
        _log($"Goal: {goal}");
        _log($"Backend: {_backend.Name}   Target: {_target.Name}   max steps: {_options.MaxSteps}");
        _log(_skills.Count > 0
            ? $"Skills: {string.Join(", ", _skills.Select(s => s.Name))} ({_skills.Sum(s => s.Checks.Count)} checks)"
            : "Skills: none (--skills off)");

        var system = BuildSystemPrompt(_target, _skills);
        var history = new List<Turn> { new(Role.User, BuildInitialPrompt(goal)) };

        var ctx = new RunContext
        {
            Goal = goal,
            Target = _target,
            Skills = _skills,
            Options = _options,
            Log = _log,
        };

        for (int step = 1; step <= _options.MaxSteps; step++)
        {
            _log($"\n──────── step {step}/{_options.MaxSteps} ────────");
            using var stepSpan = _trace?.Begin(SpanKind.Phase, $"step {step}");
            ctx.Attempt = step;

            // ① 생성 — 오래된 시도는 잘라 보낸다(계약이 "매번 전체 파일"이라 최신 것만 있으면 충분).
            var sent = Trim(history);
            _log($"  (context {Feedback.ApproxChars(system, sent.Select(t => t.Content)):N0} chars / {sent.Count} turns)");

            var genSpan = _trace?.Begin(SpanKind.Node, "Generate");
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
            ctx.Reply = reply;

            // ② ~ ③ 노드 파이프라인
            var (verdict, feedback) = await RunPipelineAsync(ctx, ct);

            // ⑤ 판정
            if (verdict is not null)
            {
                await CaptureEvidenceAsync(goal, ct);
                return new LoopResult(true, step, verdict);
            }

            // ④ 피드백 → 다음 시도 ①로
            history.Add(new Turn(Role.User, feedback!));
        }

        return new LoopResult(false, _options.MaxSteps,
            $"gave up after {_options.MaxSteps} step(s) — still failing");
    }

    /// <summary>
    /// 파이프라인 한 바퀴. 전부 통과하면 판정 문장을, 하나라도 실패하면 피드백을 돌려준다.
    /// **첫 Fail 에서 멈춘다** — 뒤 검증은 앞이 깨진 상태에서 의미가 없다.
    /// </summary>
    private async Task<(string? Verdict, string? Feedback)> RunPipelineAsync(RunContext ctx, CancellationToken ct)
    {
        var outcomes = new Dictionary<string, NodeOutcome>();

        foreach (var node in _pipeline)
        {
            using var span = _trace?.Begin(SpanKind.Node, node.Name);
            ctx.Artifact = span is null ? null : (name, content) => span.Artifact(name, content);

            NodeOutcome outcome;
            try
            {
                outcome = await node.RunAsync(ctx, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (node.Policy.FatalOnError)
            {
                outcome = new NodeOutcome.Fatal(ex.Message);
            }
            finally
            {
                ctx.Artifact = null;
            }

            outcomes[node.Name] = outcome;

            switch (outcome)
            {
                case NodeOutcome.Pass p:
                    span?.Pass(p.Log);
                    break;

                case NodeOutcome.Skip s:
                    span?.Skip(s.Why);
                    break;

                case NodeOutcome.Blocked b:
                    // [계획] 멀티세션 — 남의 잘못이므로 모델에 되먹이지 않는다(§7.5).
                    span?.Blocked(b.By, b.Log);
                    throw new InvalidOperationException($"blocked by {b.By}: {b.Log}");

                case NodeOutcome.Fatal f:
                    // 인프라 잘못은 모델이 고칠 수 없다 — 되먹이지 않고 중단한다(§5.3).
                    span?.Fatal(f.Message);
                    throw new InvalidOperationException(f.Message);

                case NodeOutcome.Fail fail:
                    span?.Fail(fail.Errors, fail.Log);
                    return (null, fail.Feedback);
            }
        }

        return (Summarize(ctx.Attempt, outcomes), null);
    }

    /// <summary>
    /// 판정 문장 — **무엇까지 실제로 검증됐는지**를 그대로 말한다.
    /// Skip 을 "통과"로 뭉뚱그리지 않는 게 핵심이다. 안 잰 걸 쟀다고 하면 안 된다.
    /// </summary>
    private static string Summarize(int step, IReadOnlyDictionary<string, NodeOutcome> outcomes)
    {
        var runtime = outcomes.GetValueOrDefault("VerifyRuntime");
        var perf = outcomes.GetValueOrDefault("VerifyPerf");

        if (perf is NodeOutcome.Pass)
            return $"behavior AND performance verified in {step} step(s)";

        if (runtime is NodeOutcome.Skip skipped)
            return $"applied and verified in {step} step(s) ({skipped.Why})";

        return $"applied and runtime-verified in {step} step(s)";
    }

    // ── 프롬프트 (출력 계약을 시스템 프롬프트로 강제 — DESIGN.md §4) ──────────────
    //
    // 루프가 소유하는 건 **형식** 계약뿐이다. 언어·경로·검증 스니펫 같은 **내용 규격**은
    // 타깃(IExecTarget.GenerationBrief)이 준다 — 손이 바뀌면 만들 것도 바뀌기 때문(D5).
    // 실패 피드백은 **그 실패를 발견한 노드**가 쓴다(Loop/Nodes/).

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

    private const string NoEditsFeedback =
        "No FILE block was found in your response. Emit 'FILE: <path>' followed on the next line by a " +
        "fenced code block containing the COMPLETE file.";

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
}
