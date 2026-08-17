using Orchestrator.Targets;

namespace Orchestrator.Loop.Nodes;

/// <summary>
/// ②-b 삭제 게이트 — 실행 전에 있던 공개 표면이 사라졌으면 반려한다. ARCHITECTURE §8.3.
///
/// 소유권(§3.2)이 막는 건 **남의 파일에 쓰는 것**이다. 이 노드가 막는 건 그게 못 덮는 나머지 —
/// **자기 소유 파일 안에서 남이 쓰는 멤버를 지우는 것**. 경로 검사로는 안 잡히고,
/// 지운 멤버를 쓰던 테스트까지 같이 지우면 스위트가 초록이 되므로 검증으로도 안 잡힌다.
///
/// Apply **뒤**에 도는 게 맞다 — 비교 대상이 "디스크에 실제로 반영된 표면"이라야 하고,
/// 표면 추출은 소스 기반이라 컴파일 전에도 읽을 수 있다(그래서 컴파일보다 앞이다: 싼 검사가 앞).
///
/// 실패는 `Fail` 이다 — 되돌려 주면 모델이 고칠 수 있다("지운 멤버를 되살려라").
/// </summary>
public sealed class DeletionGateNode : INode
{
    public string Name => "DeletionGate";

    private const int MaxListed = 10;

    public Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var baseline = ctx.Options.SurfaceBaseline;
        var read = ctx.Options.ReadSurface;

        if (baseline is null || read is null)
            return Task.FromResult<NodeOutcome>(new NodeOutcome.Skip("deletion gate off"));
        if (baseline.IsEmpty)
            return Task.FromResult<NodeOutcome>(new NodeOutcome.Skip("nothing existed before this run"));

        var missing = SurfaceDiff.Missing(baseline, read());
        if (missing.Count == 0)
        {
            // 통과도 로그를 남긴다 — 조용한 성공은 "게이트가 돌았는지" 알 수 없게 만든다.
            ctx.Log($"②-b gate     → surface intact ✅  ({baseline.Types.Count} type(s) checked)");
            return Task.FromResult<NodeOutcome>(new NodeOutcome.Pass($"surface intact ({baseline.Types.Count} type(s))"));
        }

        var listed = missing.Take(MaxListed).ToList();
        var more = missing.Count > MaxListed ? $" (+{missing.Count - MaxListed} more)" : "";

        ctx.Log($"②-b deletion → {missing.Count} public member(s) disappeared ❌");
        foreach (var m in listed)
            ctx.Log($"      · {m}");

        return Task.FromResult<NodeOutcome>(new NodeOutcome.Fail(
            Feedback:
                $"Your edit REMOVED public API that existed before this run: {string.Join(", ", listed)}{more}. " +
                "Other code and other tests may depend on it. Do not reshape existing types to fit your " +
                "implementation — adapt to the members they already expose, and re-emit the COMPLETE files " +
                "with everything that was there before still present. Adding is fine; removing is not.",
            Errors: missing,
            Log: $"{missing.Count} public member(s) disappeared"));
    }
}
