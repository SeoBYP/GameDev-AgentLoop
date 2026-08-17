using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Loop.Nodes;

/// <summary>③-a 컴파일 검증 — "그럴듯한데 안 도는 코드"를 여기서 거른다.</summary>
public sealed class VerifyCompileNode : INode
{
    public string Name => "VerifyCompile";

    public async Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var label = ctx.Target.LabelFor(VerifyKind.Compile);
        var verify = await ctx.Target.VerifyAsync(new VerifySpec(VerifyKind.Compile), ct);

        if (verify.Ok)
        {
            ctx.Log($"③ verify   → {label} passed ✅");
            return new NodeOutcome.Pass();
        }

        ctx.Log($"③ verify   → {label}: {verify.Errors.Count} error(s) ❌");
        foreach (var e in verify.Errors.Take(5))
            ctx.Log($"      · {Feedback.Clip(e, 200)}");

        // 모델에는 상위 N건만 가지만, 사람이 볼 전문은 span 에 매달아 둔다.
        ctx.Artifact?.Invoke("compile.log",
            string.Join("\n", verify.Errors) + "\n\n---\n" + verify.Log);

        return new NodeOutcome.Fail(
            Feedback: BuildFeedback(verify.Errors),
            Errors: verify.Errors,
            Log: verify.Errors.Count > 0 ? Feedback.Clip(verify.Errors[0], 90) : null);
    }

    private static string BuildFeedback(IReadOnlyList<string> errors)
    {
        var list = Feedback.Bullets(errors);
        return $"""
            Compilation FAILED. Fix these compiler errors and emit the COMPLETE file(s) again
            (no diffs, no partial edits — full file contents):

            {list}
            """;
    }
}
