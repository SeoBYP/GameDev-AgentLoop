using Orchestrator.Trace;

namespace Orchestrator.Loop.Nodes;

/// <summary>
/// 노드가 낼 수 있는 결과. ARCHITECTURE §5.3.
///
/// **갈래는 "무엇이 일어났는가"가 아니라 "누구 잘못인가"로 나뉜다** — 귀책이 라우팅을 결정하기 때문이다.
/// 모델 잘못만 모델에게 되돌린다.
///
///   Pass    — 기준을 만족했다
///   Fail    — **모델 잘못.** 피드백을 주면 고칠 수 있다
///   Skip    — 기준이 없거나 타깃이 미지원. 통과로 친다
///   Blocked — **다른 세션 잘못.** 내 코드는 멀쩡하다 [계획: 멀티세션]
///   Fatal   — **인프라 잘못.** 모델이 고칠 수 없으므로 되먹이지 않고 중단한다
/// </summary>
public abstract record NodeOutcome
{
    public sealed record Pass(string? Log = null) : NodeOutcome;

    /// <param name="Feedback">모델에게 되돌릴 문장. 이걸 쓰는 건 **실패를 발견한 노드**다.</param>
    public sealed record Fail(string Feedback, IReadOnlyList<string>? Errors = null, string? Log = null)
        : NodeOutcome;

    public sealed record Skip(string Why) : NodeOutcome;

    public sealed record Blocked(string By, string? Log = null) : NodeOutcome;

    public sealed record Fatal(string Message) : NodeOutcome;

    public SpanOutcome ToSpanOutcome() => this switch
    {
        Pass    => SpanOutcome.Pass,
        Fail    => SpanOutcome.Fail,
        Skip    => SpanOutcome.Skip,
        Blocked => SpanOutcome.Blocked,
        _       => SpanOutcome.Fatal,
    };
}
