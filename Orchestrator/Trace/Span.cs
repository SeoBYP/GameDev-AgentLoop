namespace Orchestrator.Trace;

/// <summary>
/// span 이 어느 층에 속하는가. ARCHITECTURE §6.1.
///
/// 네 종류인 게 우연이 아니다 — 실행 층 셋(작업/단계/노드) + **리스**.
/// 대기도 1급 기록 대상이다(멀티세션 병목이 진짜인지 재려면 대기 시간이 남아야 한다).
/// </summary>
public enum SpanKind
{
    Run,     // 실행 전체(루트)
    Work,    // 작업 노드 — 목표를 쪼갠 단위 [계획: 분해가 들어오면 채워진다]
    Phase,   // 단계 — 지금은 루프 스텝, 나중엔 RED/GREEN/REFACTOR
    Node,    // 실행 노드 — Generate / Apply / VerifyCompile / ...
    Lease,   // 배타 자원 대기·보유 [계획: 멀티세션에서 채워진다]
}

/// <summary>
/// **누구 잘못인가**로 나뉜다(ARCHITECTURE §5.3). 무엇이 일어났는가가 아니다 —
/// 귀책이 라우팅을 결정하기 때문이다. 모델 잘못만 모델에게 되돌린다.
/// </summary>
public enum SpanOutcome
{
    Pass,      // —
    Fail,      // 모델 잘못. 피드백으로 고칠 수 있다
    Skip,      // 기준 없음 / 타깃 미지원 → 통과로 친다
    Blocked,   // 다른 세션 잘못 [계획]
    Fatal,     // 인프라 잘못. 모델이 고칠 수 없다
}

/// <summary>
/// 트레이스의 한 마디. <see cref="ParentSpanId"/> 한 줄이 로그를 **트리**로 만들고,
/// 트리가 아니면 학습 신호가 되지 못한다(ARCHITECTURE §6.3).
/// </summary>
public sealed record Span(
    string      RunId,
    string      SpanId,
    string?     ParentSpanId,
    SpanKind    Kind,
    string      Name,
    SpanOutcome Outcome,
    double      Ms,
    string?     Log = null,
    string?     BlamedOn = null,        // Blocked 일 때 누구 때문인지 [계획]
    string?     SessionId = null,       // 멀티세션에서 누가 [계획]
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Artifacts = null);
