using System.Text;
using Orchestrator.Contracts;

namespace Orchestrator.Util;

/// <summary>
/// AgentContext(시스템 지시 + 대화 히스토리)를 headless CLI 한 방 프롬프트로 평탄화한다.
/// ClaudeCodeBackend·CodexBackend 가 공유 — API messages 를 못 받는 CLI 백엔드용 정규화(DESIGN.md §4 "맥락 정규화").
/// </summary>
public static class PromptText
{
    public static string Flatten(AgentContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(context.System);
        sb.AppendLine();
        sb.AppendLine("아래는 지금까지의 대화 맥락이다. 이어서 다음 어시스턴트 응답을 위 출력 계약대로 내라. 도구를 쓰지 말고 텍스트로만.");
        sb.AppendLine();
        foreach (var turn in context.History)
        {
            sb.AppendLine(turn.Role == Role.User ? "## USER" : "## ASSISTANT");
            sb.AppendLine(turn.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
