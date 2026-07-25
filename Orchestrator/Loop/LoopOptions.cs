namespace Orchestrator.Loop;

/// <summary>루프 가드/설정.</summary>
public sealed record LoopOptions
{
    /// <summary>최대 반복 횟수(무한루프 가드). DESIGN.md §5 기본값 6.</summary>
    public int MaxSteps { get; init; } = 6;
}

/// <summary>루프 종료 판정 결과.</summary>
public sealed record LoopResult(bool Success, int Steps, string Summary);
