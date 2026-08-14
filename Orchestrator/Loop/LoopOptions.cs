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

    /// <summary>성능 검증(③-c)을 끄고 동작 검증까지만 본다(대조·디버깅용).</summary>
    public bool NoPerf { get; init; }

    /// <summary>성공 시 결과 화면을 캡처해 남길 디렉터리(지정 시에만 캡처).</summary>
    public string? CaptureDir { get; init; }

    /// <summary>
    /// 백엔드에 보낼 최근 대화 턴 수(목표 턴은 항상 유지). 0 이면 무제한.
    ///
    /// 왜 필요한가: 출력 계약이 "매번 전체 파일"이라 스텝마다 파일 전문이 히스토리에 쌓인다.
    /// 6스텝이면 같은 파일이 6벌 — 컨텍스트·비용이 선형으로 터진다.
    /// 오래된 시도는 최신 전체 파일로 대체되므로 버려도 안전하다.
    /// </summary>
    public int HistoryWindow { get; init; } = 4;

    /// <summary>실행 로그(전체 에러 원문 등)를 남길 디렉터리. 모델에는 요약만 가고, 사람은 전문을 본다.</summary>
    public string? RunLogDir { get; init; }
}

/// <summary>루프 종료 판정 결과.</summary>
public sealed record LoopResult(bool Success, int Steps, string Summary);
