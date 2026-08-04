namespace Orchestrator.Loop;

/// <summary>루프 가드/설정.</summary>
public sealed record LoopOptions
{
    /// <summary>최대 반복 횟수(무한루프 가드). DESIGN.md §5 기본값 6.</summary>
    public int MaxSteps { get; init; } = 6;

    /// <summary>
    /// 사람이 지정한 플레이모드 런타임 assert(선택). 주면 백엔드가 낸 ASSERT 블록보다 **우선**한다.
    /// AI 가 자기 코드를 자기 기준으로 채점하는 것을 막고 싶을 때 쓰는 권위 있는 검증 기준.
    /// </summary>
    public string? Assert { get; init; }
}

/// <summary>루프 종료 판정 결과.</summary>
public sealed record LoopResult(bool Success, int Steps, string Summary);
