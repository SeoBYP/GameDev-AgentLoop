using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Trace;

/// <summary>
/// 한 번의 실행이 남기는 기록. ARCHITECTURE §6.4.
///
/// 왜 옮겼는가: 실행 로그가 `%TEMP%` 로 가고 있었다. 즉 *"모델이 무엇을 틀렸고 무엇으로 고쳐서
/// 통과했는가"* 라는 제일 구하기 힘든 데이터를 만들어 놓고 OS 가 지우게 두고 있었다.
/// 학습 계층(Calibrator·Distiller)도, 재개도, 재분해 피드백도 전부 이 기록 위에 선다.
///
///   .agentloop/runs/&lt;runId&gt;/
///     run.json        목표·백엔드·타깃·옵션 스냅샷·판정·벽시계
///     trace.jsonl     Span 스트림 (추가 전용 — 중간에 죽어도 거기까지 남는다)
///     spans/&lt;id&gt;/    큰 산출물(응답 원문·컴파일 로그 등)은 span 에 매달아 둔다
///     evidence/       스크린샷 등
///
/// **비밀은 저장하지 않는다.** 키 *이름* 만 기록하는 현재 규칙을 그대로 지킨다.
/// </summary>
public sealed class RunStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions Jsonl = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string RunId { get; }
    public string Root { get; }

    private readonly string _tracePath;
    private readonly object _lock = new();

    private RunStore(string runId, string root)
    {
        RunId = runId;
        Root = root;
        _tracePath = Path.Combine(root, "trace.jsonl");
    }

    /// <summary>실행 기록 디렉터리를 만든다. baseDir 기본값은 &lt;project&gt;/.agentloop/runs.</summary>
    public static RunStore Create(string projectPath, string? baseDir, DateTime startedAt)
    {
        var runsDir = baseDir ?? Path.Combine(projectPath, ".agentloop", "runs");
        var runId = startedAt.ToString("yyyyMMdd-HHmmss");

        // 같은 초에 두 번 시작해도 덮어쓰지 않는다.
        var root = Path.Combine(runsDir, runId);
        for (var n = 2; Directory.Exists(root); n++)
            root = Path.Combine(runsDir, $"{runId}-{n}");

        Directory.CreateDirectory(root);
        return new RunStore(Path.GetFileName(root), root);
    }

    /// <summary>span 하나를 trace.jsonl 에 덧붙인다(추가 전용).</summary>
    public void Append(Span span)
    {
        try
        {
            var line = JsonSerializer.Serialize(span, Jsonl);
            lock (_lock)
                File.AppendAllText(_tracePath, line + Environment.NewLine);
        }
        catch { /* 기록 실패가 루프를 막지는 않는다 */ }
    }

    /// <summary>큰 산출물을 해당 span 폴더에 남긴다. 반환값은 run 루트 기준 상대 경로.</summary>
    public string? WriteArtifact(string spanId, string fileName, string content)
    {
        try
        {
            var dir = Path.Combine(Root, "spans", spanId);
            Directory.CreateDirectory(dir);
            var full = Path.Combine(dir, fileName);
            File.WriteAllText(full, content);
            return Path.GetRelativePath(Root, full).Replace('\\', '/');
        }
        catch { return null; }
    }

    /// <summary>
    /// 실행 요약. **그때의 스킬·예산까지 스냅샷**한다 —
    /// 안 그러면 나중에 실행끼리 비교하는 게 무의미해진다.
    /// </summary>
    public void WriteManifest(RunManifest manifest)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Root, "run.json"),
                JsonSerializer.Serialize(manifest, Json));
        }
        catch { /* 무시 */ }
    }

    /// <summary>가장 최근 실행 디렉터리(트레이스 조회용).</summary>
    public static string? FindLatest(string projectPath, string? baseDir)
    {
        var runsDir = baseDir ?? Path.Combine(projectPath, ".agentloop", "runs");
        if (!Directory.Exists(runsDir))
            return null;

        return Directory.EnumerateDirectories(runsDir)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();
    }
}

/// <summary>run.json 의 내용 — 이 실행이 무엇이었는지를 사후에 재구성할 수 있을 만큼.</summary>
public sealed record RunManifest(
    string   RunId,
    string   StartedAt,
    string   Goal,
    string   Backend,
    string   Target,
    string?  Model,
    int      MaxSteps,
    string   VerifyMode,
    int      HistoryWindow,
    IReadOnlyList<string> Skills,     // 그때 적용된 스킬(이름)
    string?  ProjectLayout,           // 그때의 경로·어셈블리
    bool     Success,
    int      Steps,
    string   Summary,
    double   WallClockMs);
