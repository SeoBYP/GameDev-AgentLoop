using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Loop.Nodes;

/// <summary>
/// ③-c 성능 예산 — **기본적으로 루프 밖이다**(타깃이 `Supports` 로 선언한다, `--perf`).
///
/// 에디터 측정치는 출시 성능이 아니라 상대 신호이고, 정확성과는 주기가 다른 질문이다.
/// 자세한 근거는 ARCHITECTURE §9.4.
/// </summary>
public sealed class VerifyPerfNode : INode
{
    public string Name => "VerifyPerf";

    public async Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        // PERF 블록은 eval 로 측정하므로 tests-only 모드에서는 건너뛴다.
        var spec = ctx.TestsOnly ? null : EditParser.ParsePerf(ctx.Reply.Text);

        if (spec is null)
            return new NodeOutcome.Skip("no PERF block");
        if (!ctx.Target.Supports(VerifyKind.Performance))
            return new NodeOutcome.Skip("unsupported");

        var label = ctx.Target.LabelFor(VerifyKind.Performance);
        var perf = await ctx.Target.VerifyAsync(new VerifySpec(VerifyKind.Performance, spec), ct);

        if (perf.Ok)
        {
            ctx.Log($"③ verify   → {label} passed ✅  {perf.Log}");
            return new NodeOutcome.Pass(perf.Log);
        }

        ctx.Log($"③ verify   → {label} exceeded ❌  {perf.Log}");
        foreach (var e in perf.Errors.Take(3))
            ctx.Log($"      · {e}");

        return new NodeOutcome.Fail(BuildFeedback(perf.Errors), perf.Errors, perf.Log);
    }

    // "예산을 늘려라"가 아니라 "구현을 빠르게 고쳐라"를 명시한다.
    private static string BuildFeedback(IReadOnlyList<string> failures)
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
}
