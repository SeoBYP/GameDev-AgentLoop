namespace Orchestrator.Loop.Nodes;

/// <summary>② 적용 — 생성물을 타깃에 쓰고, 타깃이 필요한 후속 처리(리컴파일 등)를 트리거한다.</summary>
public sealed class ApplyNode : INode
{
    public string Name => "Apply";

    public async Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var apply = await ctx.Target.ApplyAsync(ctx.Reply.Edits, ct);
        ctx.Log($"② apply    → {apply.Message}");

        if (apply.Ok)
        {
            // 테스트 파일이 한 번이라도 들어오면 이후 시도에서도 테스트 러너로 검증한다.
            ctx.TestsInProject |= ctx.Reply.Edits.Any(e => IsTestFile(e.RelativePath));
            return new NodeOutcome.Pass(apply.Message);
        }

        return new NodeOutcome.Fail(
            Feedback: $"Apply failed: {apply.Message}. Fix the path and emit the files again.",
            Log: apply.Message);
    }

    /// <summary>테스트 파일인가(경로 규약). 테스트가 오면 일회용 assert 대신 테스트 러너로 검증한다.</summary>
    private static bool IsTestFile(string relativePath) =>
        relativePath.Replace('\\', '/').Contains("/Tests/", StringComparison.OrdinalIgnoreCase) &&
        relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
}
