using Orchestrator.Skills;
using Orchestrator.Util;

namespace Orchestrator.Loop.Nodes;

/// <summary>
/// ①-b 도메인 규칙 정적 검사 — **프로젝트에 쓰기 전에** 거른다.
/// 지침을 프롬프트로 주는 데서 그치지 않고 검사로 강제하는 게 Phase 3 의 핵심이다.
/// </summary>
public sealed class SkillCheckNode : INode
{
    public string Name => "SkillCheck";

    public Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        if (ctx.Skills.Count == 0)
            return Task.FromResult<NodeOutcome>(new NodeOutcome.Skip("no skills"));

        var violations = SkillLibrary.Inspect(ctx.Skills, ctx.Reply.Edits);
        if (violations.Count == 0)
        {
            ctx.Log("①-b check  → skills passed ✅");
            return Task.FromResult<NodeOutcome>(
                new NodeOutcome.Pass($"{ctx.Skills.Sum(s => s.Checks.Count)} checks"));
        }

        ctx.Log($"①-b check  → {violations.Count} skill violation(s) ❌ (not applied)");
        foreach (var v in violations.Take(5))
            ctx.Log($"      · {v}");

        return Task.FromResult<NodeOutcome>(new NodeOutcome.Fail(
            Feedback: BuildFeedback(violations),
            Errors: violations.Select(v => $"{v.FilePath}: {v.Message}").ToList(),
            Log: $"{violations.Count} violation(s)"));
    }

    // 파일이 프로젝트에 적용되기 **전** 단계라 "다시 내라"가 명확하다.
    private static string BuildFeedback(IReadOnlyList<SkillViolation> violations)
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
}
