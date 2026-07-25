using System.Text;
using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Backends;

/// <summary>
/// "이 AI 채팅"(Claude Code CLI)을 두뇌로 쓰는 백엔드.
///
/// 핵심: 별도 API 키 없이, 사용자가 이미 로그인한 Claude Code 로 루프를 돌린다.
/// 설계 D1 준수 — Claude Code 의 자체 에이전트 루프에 위임하지 않는다:
///   `claude -p`(headless print) 로 **1회 응답만** 받고(도구 전부 비활성),
///   적용·검증·재시도·판정은 여전히 오케스트레이터가 소유한다.
/// → 그래서 ApiBackend 와 완전히 동등하게 루프에 꽂힌다(교체 가능성의 증거).
///
/// 전제: `claude` CLI 가 PATH 에 있고 로그인돼 있어야 한다(`claude` 실행 후 로그인).
/// 인증이 만료되면 `claude -p` 가 401 을 내므로, 그때는 재로그인이 필요하다.
/// </summary>
public sealed class ClaudeCodeBackend : IAgentBackend
{
    private readonly string _model;   // 'sonnet' | 'opus' | 풀 모델 ID
    private readonly string _workDir; // 격리된 작업 폴더(도구가 꺼져 있어도 프로젝트를 건드리지 못하게)

    public string Name => $"claude-code:{_model}";

    public ClaudeCodeBackend(string model, string? workDir = null)
    {
        _model = model;
        _workDir = workDir ?? Path.GetTempPath();
    }

    public async Task<AgentReply> CompleteAsync(AgentContext context, CancellationToken ct)
    {
        var prompt = BuildPrompt(context);

        // 파일/셸 도구를 전부 비활성 → 순수 텍스트 생성기로만 동작(오케스트레이터가 적용을 소유).
        string[] backendArgs =
        {
            "-p",
            "--output-format", "text",
            "--model", _model,
            "--disallowedTools", "Write", "Edit", "MultiEdit", "NotebookEdit", "Bash",
        };

        var (file, prefix) = ClaudeInvocation();
        var args = new List<string>(prefix);
        args.AddRange(backendArgs);

        var res = await ProcessRunner.RunAsync(file, args, _workDir, ct, stdin: prompt);
        if (!res.Ok)
            throw new InvalidOperationException(
                $"`claude -p` 실패(exit {res.ExitCode}). 로그인 만료면 `claude` 를 실행해 재로그인하세요.\n{res.StdErr}{res.StdOut}");

        var text = res.StdOut;
        return new AgentReply(text, EditParser.Parse(text));
    }

    // AgentContext(시스템 + 대화 히스토리)를 headless 한 방 프롬프트로 평탄화한다.
    private static string BuildPrompt(AgentContext context)
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

    // Windows 는 npm 전역 shim(claude.cmd)이라 cmd.exe 로 감싸 PATH 해석을 맡긴다.
    private static (string File, string[] Prefix) ClaudeInvocation() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "claude" })
            : ("claude", Array.Empty<string>());
}
