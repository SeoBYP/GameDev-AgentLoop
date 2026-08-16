namespace Orchestrator.Loop;

/// <summary>
/// 검증에 **임시 코드 실행(eval)** 을 허용할지.
///
///   Auto      — 테스트가 있으면 테스트로, 없으면 ASSERT/PERF 스니펫을 eval 로 실행(기본).
///   TestsOnly — eval 을 아예 쓰지 않는다. 검증은 **컴파일된 테스트 파일**로만 한다.
///
/// TestsOnly 의 값: AI 가 만든 코드가 임시 스니펫이 아니라 **레포에 남는 리뷰 가능한 파일**로만
/// 실행된다. git diff 에 걸리고, 사람이 읽을 수 있고, 되돌릴 수 있다.
/// (이건 격리가 아니라 **감사 가능성**이다 — 진짜 격리는 OS 수준의 몫. DESIGN §6.12 참고)
/// </summary>
public enum VerifyMode
{
    Auto,
    TestsOnly,
}

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

    /// <summary>검증에 임시 코드 실행(eval)을 허용할지. TestsOnly 면 테스트 파일로만 검증한다.</summary>
    public VerifyMode VerifyMode { get; init; } = VerifyMode.Auto;
}

/// <summary>루프 종료 판정 결과.</summary>
public sealed record LoopResult(bool Success, int Steps, string Summary);
