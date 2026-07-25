using System.Diagnostics;
using System.Text;

namespace Orchestrator.Util;

/// <summary>외부 프로세스(주로 `unity` CLI) 실행 결과.</summary>
public record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// 외부 프로세스를 실행하고 stdout/stderr 를 캡처하는 얇은 헬퍼.
/// 인자는 ArgumentList 로 넘겨 OS별 따옴표/이스케이프 문제를 피한다
/// (멀티라인 C# 스니펫을 `unity command eval` 에 통째로 넘길 때 중요).
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDir,
        CancellationToken ct,
        string? stdin = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;

        using var proc = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            proc.StandardInput.Close();
        }

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        return new ProcessResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // 정리 중 실패는 무시 — 원래 취소 예외를 살린다.
        }
    }
}
