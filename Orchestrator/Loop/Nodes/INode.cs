using Orchestrator.Contracts;
using Orchestrator.Skills;

namespace Orchestrator.Loop.Nodes;

/// <summary>
/// 루프의 한 단계. ARCHITECTURE §5.2.
///
/// 노드는 **자기 판단만** 한다 — 라우팅·재시도·타이밍·기록은 실행기(<see cref="AgentLoop"/>)가 소유한다.
/// 노드가 `continue` 를 부르지 않는다는 게 핵심이다: 흐름 제어가 노드 밖으로 나오면서
/// "검증을 하나 추가한다"가 *수술*에서 *등록*으로 바뀐다.
/// </summary>
public interface INode
{
    string Name { get; }

    /// <summary>기본 정책. 노드가 스스로 자기 재시도·치명도를 선언한다.</summary>
    NodePolicy Policy => NodePolicy.Default;

    Task<NodeOutcome> RunAsync(RunContext ctx, CancellationToken ct);
}

/// <summary>
/// 노드별 정책. 손으로 짜 넣던 재시도·게이팅을 데이터로 끌어낸 자리다.
/// (지금은 <see cref="FatalOnError"/> 만 실제로 쓰인다 — 나머지는 그래프 선언 단계에서 채운다.)
/// </summary>
public sealed record NodePolicy(
    int       MaxRetries   = 0,
    TimeSpan? Timeout      = null,
    bool      FatalOnError = false)
{
    public static readonly NodePolicy Default = new();
}

/// <summary>
/// 한 시도(attempt) 동안 노드들이 공유하는 상태.
/// 노드는 여기서 읽고, 다음 노드가 쓸 것만 쓴다.
/// </summary>
public sealed class RunContext
{
    public required string Goal { get; init; }
    public required IExecTarget Target { get; init; }
    public required IReadOnlyList<Skill> Skills { get; init; }
    public required LoopOptions Options { get; init; }
    public required Action<string> Log { get; init; }

    /// <summary>현재 시도 번호(1부터).</summary>
    public int Attempt { get; set; }

    /// <summary>①생성이 만든 응답. 이후 노드들이 소비한다.</summary>
    public AgentReply Reply { get; set; } = new(string.Empty, Array.Empty<FileEdit>());

    /// <summary>
    /// 이 실행에서 테스트 파일이 **한 번이라도** 적용됐는지(시도를 넘어 유지된다).
    /// 이후 시도가 구현만 고쳐 보내도 테스트는 이미 프로젝트에 있으므로 테스트 러너로 검증해야 한다.
    /// </summary>
    public bool TestsInProject { get; set; }

    public bool TestsOnly => Options.VerifyMode == VerifyMode.TestsOnly;

    /// <summary>
    /// 현재 노드의 span 에 큰 원문을 매달아 둔다(파일이름, 내용). 실행기가 노드마다 갈아 끼운다.
    /// 모델에는 요약만 가고 사람은 전문을 본다 — 그 "전문"이 여기로 간다.
    /// </summary>
    public Action<string, string>? Artifact { get; set; }
}
