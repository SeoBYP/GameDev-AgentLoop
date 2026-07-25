namespace Orchestrator.Contracts;

/// <summary>
/// 두뇌 축. "맥락 주면 다음 응답을 돌려준다"는 최소공통분모 계약만 갖는다.
/// DESIGN.md D1: 백엔드는 텍스트 생성기일 뿐, 적용·검증·재시도는 오케스트레이터(루프)가 소유한다.
/// → 이 인터페이스가 얇을수록 백엔드(ApiBackend / ClaudeCodeBackend / CodexBackend) 교체가 쉬워진다.
/// </summary>
public interface IAgentBackend
{
    /// <summary>로그·판정용 표시 이름 (예: "api:claude-opus-5").</summary>
    string Name { get; }

    /// <summary>맥락(목표 + 이전 생성물 + 검증 에러 누적)을 받아 다음 응답을 돌려준다.</summary>
    Task<AgentReply> CompleteAsync(AgentContext context, CancellationToken ct);
}
