using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Backends;

/// <summary>
/// 미리 정해둔 응답을 순서대로 돌려주는 결정적 백엔드.
///
/// 용도: API 키 없이 루프 메커니즘(적용→검증→자가수리)을 증명한다.
/// `--demo` 는 이 백엔드에 "일부러 깨진 Health.cs → (컴파일 에러 피드백 받은 뒤) 고친 Health.cs"
/// 를 스크립트로 넣어, 자가수리 루프가 실제로 도는지 결정적으로 보여준다.
/// IAgentBackend 계약만 만족하므로 루프 입장에선 ApiBackend 와 완전히 동등하다(D3).
/// </summary>
public sealed class ScriptedBackend : IAgentBackend
{
    private readonly IReadOnlyList<string> _replies;
    private int _index;

    public string Name => "scripted:demo";

    public ScriptedBackend(IReadOnlyList<string> replies) => _replies = replies;

    public Task<AgentReply> CompleteAsync(AgentContext context, CancellationToken ct)
    {
        // 스크립트가 소진되면 마지막 응답을 반복(루프가 수렴하도록).
        var text = _replies[Math.Min(_index, _replies.Count - 1)];
        _index++;
        return Task.FromResult(new AgentReply(text, EditParser.Parse(text)));
    }
}
