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
        var prompt = PromptText.Flatten(context);

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
                $"`claude -p` failed (exit {res.ExitCode}). If your login expired, run `claude` and sign in again.\n{res.StdErr}{res.StdOut}");

        var text = res.StdOut;
        return new AgentReply(text, EditParser.Parse(text));
    }

    // Windows 는 npm 전역 shim(claude.cmd)이라 cmd.exe 로 감싸 PATH 해석을 맡긴다.
    private static (string File, string[] Prefix) ClaudeInvocation() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "claude" })
            : ("claude", Array.Empty<string>());
}
