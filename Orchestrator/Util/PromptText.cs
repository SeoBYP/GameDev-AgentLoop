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
        sb.AppendLine("Below is the conversation so far. Produce the next assistant response following the output contract above. Do not use tools — text only.");
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
