namespace Orchestrator.Loop.Nodes;

/// <summary>② 적용 — 생성물을 타깃에 쓰고, 타깃이 필요한 후속 처리(리컴파일 등)를 트리거한다.</summary>
public sealed class ApplyNode : INode
{
    public string Name => "Apply";

    public async Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        // ①-c 소유권 — **쓰기 전에** 본다(§3.2). 쓴 다음에 잡으면 이미 남의 파일이 덮였다.
        //     선언 밖에 쓰는 건 모델 잘못이므로 되먹여서 고칠 수 있다 → Fail.
        var paths = ctx.Reply.Edits.Select(e => e.RelativePath).ToList();
        var violations = Ownership.Violations(paths, ctx.Options.Owns);
        if (violations.Count > 0)
        {
            var allowed = string.Join(", ", ctx.Options.Owns!);
            ctx.Log($"②-a owns   → {violations.Count} path(s) outside ownership ❌");
            foreach (var v in violations)
                ctx.Log($"      · {v}");

            return new NodeOutcome.Fail(
                Feedback:
                    $"These paths are outside this node's ownership, so they were NOT written: " +
                    $"{string.Join(", ", violations)}. This node may only write: {allowed}. " +
                    "Achieve the goal using only the members the other files already expose, and " +
                    "re-emit the COMPLETE files that belong to you. If the goal genuinely cannot be " +
                    "met without changing a file you do not own, say so in one line instead of editing it.",
                Errors: violations,
                Log: $"{violations.Count} path(s) outside ownership");
        }

        var apply = await ctx.Target.ApplyAsync(ctx.Reply.Edits, ct);
        ctx.Log($"② apply    → {apply.Message}");

        if (apply.Ok)
        {
            // 테스트 파일이 한 번이라도 들어오면 이후 시도에서도 테스트 러너로 검증한다.
            ctx.TestsInProject |= ctx.Reply.Edits.Any(e => IsTestFile(e.RelativePath));

            // 소유권 선언이 없을 때의 최소 방어: **기록**. 강제하지 않는 이유는 Ownership 주석 참고.
            // 위 사고가 git 비교로만 발견됐으므로, 최소한 로그와 트레이스에는 남아야 한다.
            var touched = ctx.Options.Owns is { Count: > 0 }
                ? Array.Empty<string>()
                : paths.Where(p => ctx.Options.PreExisting.Contains(Ownership.Key(p))).ToArray();
            if (touched.Length > 0)
                ctx.Log($"   ⚠ modified {touched.Length} file(s) that existed before this run: {string.Join(", ", touched)}");

            return new NodeOutcome.Pass(touched.Length > 0
                ? $"{apply.Message} · modified {touched.Length} pre-existing file(s)"
                : apply.Message);
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
