using Orchestrator.Contracts;
using Orchestrator.Util;

namespace Orchestrator.Backends;

/// <summary>
/// OpenAI Codex CLI 를 두뇌로 쓰는 백엔드 (Phase 2 — agent-agnostic 증명용).
///
/// ClaudeCodeBackend 과 완전히 같은 방식으로 <see cref="IAgentBackend"/> 를 구현한다:
/// 서로 다른 두 CLI 에이전트(Claude Code · Codex)가 **같은 루프에 동등하게 꽂힌다** →
/// "백엔드는 진짜 교체 가능, 루프가 결과물"(D1/D3)이 두 에이전트로 입증된다.
///
/// `codex exec` (non-interactive):
///   --sandbox read-only  : 모델이 파일을 쓰지 못하게(순수 텍스트 생성기 — 적용은 오케스트레이터가 소유)
///   --skip-git-repo-check: 격리된 임시 폴더에서 실행하므로 git 레포 검사 생략
///   -o <file>            : 최종 어시스턴트 메시지만 파일로 → 로그 잡음 없이 깔끔히 파싱
/// 전제: `codex` CLI 가 PATH 에 있고 로그인돼 있어야 한다(`codex login`).
/// </summary>
public sealed class CodexBackend : IAgentBackend
{
    private readonly string? _model; // null 이면 codex 설정 기본 모델
    private readonly string _workDir;

    public string Name => _model is null ? "codex" : $"codex:{_model}";

    public CodexBackend(string? model, string? workDir = null)
    {
        _model = model;
        _workDir = workDir ?? Path.GetTempPath();
    }

    public async Task<AgentReply> CompleteAsync(AgentContext context, CancellationToken ct)
    {
        var prompt = PromptText.Flatten(context);
        var outFile = Path.Combine(_workDir, $"codex-out-{Guid.NewGuid():N}.txt");

        var backendArgs = new List<string>
        {
            "exec",
            "--sandbox", "read-only",   // 파일 쓰기 차단 → 순수 텍스트 생성기
            "--skip-git-repo-check",
            "--color", "never",
            "-C", _workDir,
            "-o", outFile,              // 최종 메시지만 이 파일로
        };
        if (_model is not null)
        {
            backendArgs.Add("-m");
            backendArgs.Add(_model);
        }
        // 프롬프트는 stdin 으로(codex exec: 인자 없으면 stdin 을 지시문으로 읽음).

        var (file, prefix) = CodexInvocation();
        var args = new List<string>(prefix);
        args.AddRange(backendArgs);

        var res = await ProcessRunner.RunAsync(file, args, _workDir, ct, stdin: prompt);

        string text;
        if (File.Exists(outFile))
        {
            text = await File.ReadAllTextAsync(outFile, ct);
            try { File.Delete(outFile); } catch { /* 임시파일 정리 실패는 무시 */ }
        }
        else
        {
            text = res.StdOut;
        }

        if (!res.Ok && string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                $"`codex exec` 실패(exit {res.ExitCode}). 로그인 필요면 `codex login`.\n{res.StdErr}{res.StdOut}");

        return new AgentReply(text, EditParser.Parse(text));
    }

    // Windows 는 npm 전역 shim(codex.cmd)이라 cmd.exe 로 감싸 PATH 해석을 맡긴다.
    private static (string File, string[] Prefix) CodexInvocation() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "codex" })
            : ("codex", Array.Empty<string>());
}
