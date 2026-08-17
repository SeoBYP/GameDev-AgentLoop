using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Loop.Nodes;

/// <summary>
/// ③-b 런타임 동작 검증 — "컴파일은 되는데 로직이 틀린 코드"를 잡는다.
///
/// 경로 선택:
///   테스트 파일이 있으면 **테스트 러너**(레포에 남는 자산 + 다중 프레임 가능),
///   없으면 일회용 `ASSERT` 스니펫. 사람이 준 `--assert` 는 언제나 우선.
/// `--tests-only` 면 스니펫 경로를 아예 쓰지 않고 테스트를 요구한다.
/// </summary>
public sealed class VerifyRuntimeNode : INode
{
    public string Name => "VerifyRuntime";

    public async Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var useTests = ctx.TestsInProject
                       && ctx.Options.Assert is null
                       && ctx.Target.Supports(VerifyKind.Tests);

        // tests-only: 임시 스니펫을 실행하지 않는다. 테스트가 없으면 통과시키지 않고 요구한다.
        if (ctx.TestsOnly && !useTests)
        {
            ctx.Log("③ verify   → no test file ❌ (tests-only mode never runs temporary snippets)");
            return new NodeOutcome.Fail(TestsRequiredFeedback, Log: "no test file (tests-only)");
        }

        var assertCode = ctx.TestsOnly ? null : (ctx.Options.Assert ?? EditParser.ParseAssert(ctx.Reply.Text));

        if (!useTests && (assertCode is null || !ctx.Target.Supports(VerifyKind.RuntimeAssert)))
            return new NodeOutcome.Skip(
                assertCode is null ? "no runtime criterion" : "target has no runtime verification");

        var kind = useTests ? VerifyKind.Tests : VerifyKind.RuntimeAssert;
        var label = ctx.Target.LabelFor(kind);
        var source = useTests ? "test files"
                              : (ctx.Options.Assert is not null ? "user-supplied" : "AI-written");

        ctx.Log($"③ verify   → running {label} ({source})");
        var play = await ctx.Target.VerifyAsync(new VerifySpec(kind, useTests ? null : assertCode), ct);

        if (play.Ok)
        {
            ctx.Log($"③ verify   → {label} passed ✅  {play.Log}");
            return new NodeOutcome.Pass(
                string.IsNullOrWhiteSpace(play.Log) ? source : $"{source} · {play.Log}");
        }

        ctx.Log($"③ verify   → {label} failed ❌  {play.Log}");
        foreach (var e in play.Errors.Take(3))
            ctx.Log($"      · {Feedback.Clip(e, 200)}");

        ctx.Artifact?.Invoke("runtime.log", string.Join("\n", play.Errors) + "\n\n---\n" + play.Log);

        return new NodeOutcome.Fail(
            Feedback: useTests ? BuildTestFeedback(play.Errors) : BuildAssertFeedback(play.Errors),
            Errors: play.Errors,
            Log: play.Log);
    }

    private const string TestsRequiredFeedback = """
        This run verifies ONLY through compiled test files — temporary snippets are never executed.
        Emit a PlayMode test file as a FILE block alongside the implementation.
        If performance matters for this behavior, measure it inside the test with a Stopwatch and assert on it.
        """;

    // "구현을 고쳐라"를 명시한다 — 기준을 느슨하게 고쳐 통과시키는 쪽으로 새는 걸 막기 위해서.
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
