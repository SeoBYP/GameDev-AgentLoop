using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Backends;

/// <summary>
/// **첫 응답만 고정**하고, 그 뒤는 진짜 모델이 인계받는 백엔드.
///
/// 왜 필요한가(ARCHITECTURE §10): 벤치마크가 *"모델이 한 번에 맞히나"* 를 재고 있었다 —
/// 루프가 거의 개입하지 않는 지표다. 실제로 hard 목표 12개를 만들었더니 **12/12 원샷**이라
/// 개선 여지가 0이 됐다.
///
/// **루프의 제품은 생성이 아니라 수리다.** 그래서 시작점을 고장난 상태로 고정하면:
///   - 루프가 반드시 일을 해야 하므로 스텝 수가 **루프의 능력**을 반영한다
///   - 출발점이 같으니 모델 편차가 줄어 실행 간 비교가 가능해진다
///   - 피드백 문구를 개선하면 수리 스텝이 줄어드는 게 **보인다**
///
/// 결함은 **발명하지 않는다.** `Benchmark/faults/*.json` 은 전부 실제 모델이 내고
/// 실제 검증이 잡은 응답이며, 인프라 실패(도메인 리로드 경합 등)는 제외돼 있다 —
/// 그건 모델 잘못이 아니라서 재현되지도 않는다.
/// </summary>
public sealed class SeededFaultBackend : IAgentBackend
{
    private readonly string _faultReply;
    private readonly IAgentBackend _inner;
    private int _calls;

    public string Name { get; }

    public SeededFaultBackend(string faultId, string faultReply, IAgentBackend inner)
    {
        _faultReply = faultReply;
        _inner = inner;
        Name = $"fault:{faultId}+{inner.Name}";
    }

    public Task<AgentReply> CompleteAsync(AgentContext context, CancellationToken ct)
    {
        if (_calls++ == 0)
            return Task.FromResult(new AgentReply(_faultReply, EditParser.Parse(_faultReply)));

        // 두 번째 호출부터는 실제 모델이 검증 피드백을 받아 수리한다.
        return _inner.CompleteAsync(context, ct);
    }
}
