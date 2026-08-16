using System.Text;
using System.Text.Json;

namespace Orchestrator.Trace;

/// <summary>
/// `trace.jsonl` 을 다시 트리로 세워 사람이 읽을 형태로 그린다(ARCHITECTURE §6.2).
///
/// 이게 되어야 트레이스가 **평평한 로그가 아니라 트리**임이 증명된다 —
/// "이 실패가 어느 작업의 어느 단계 몇 번째 시도에서 왔나"를 복원할 수 있어야
/// 학습 신호로도, 재분해 피드백으로도 쓸 수 있다.
/// </summary>
public static class TraceTree
{
    public static string Render(string runDir)
    {
        var path = Path.Combine(runDir, "trace.jsonl");
        if (!File.Exists(path))
            return $"no trace at {path}";

        var spans = new List<Span>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var s = JsonSerializer.Deserialize<Span>(line, Options);
                if (s is not null)
                    spans.Add(s);
            }
            catch { /* 깨진 줄은 건너뛴다 — 추가 전용이라 중간에 죽으면 마지막 줄이 잘릴 수 있다 */ }
        }

        if (spans.Count == 0)
            return "(empty trace)";

        var byParent = spans.GroupBy(s => s.ParentSpanId ?? "")
                            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SpanId, StringComparer.Ordinal).ToList());

        var sb = new StringBuilder();
        var header = ReadHeader(runDir) ?? Path.GetFileName(runDir);
        sb.AppendLine(header);

        var roots = byParent.GetValueOrDefault("", new List<Span>());
        for (var i = 0; i < roots.Count; i++)
            Walk(sb, byParent, roots[i], prefix: "", isRoot: true, isLast: i == roots.Count - 1);

        return sb.ToString().TrimEnd();
    }

    private static void Walk(
        StringBuilder sb, Dictionary<string, List<Span>> byParent,
        Span span, string prefix, bool isRoot, bool isLast)
    {
        // 루트는 연결선 없이, 나머지는 ├─ / └─ 로 잇는다.
        var head = isRoot ? "" : prefix + (isLast ? "└─ " : "├─ ");
        sb.AppendLine(Format(span, head));

        var children = byParent.GetValueOrDefault(span.SpanId, new List<Span>());
        // 마지막 자식 아래로는 세로선을 잇지 않는다.
        var childPrefix = isRoot ? "" : prefix + (isLast ? "   " : "│  ");

        for (var i = 0; i < children.Count; i++)
            Walk(sb, byParent, children[i], childPrefix, isRoot: false, isLast: i == children.Count - 1);
    }

    private const int LabelColumn = 52;

    private static string Format(Span s, string head)
    {
        var mark = s.Outcome switch
        {
            SpanOutcome.Pass    => "✅",
            SpanOutcome.Fail    => "❌",
            SpanOutcome.Skip    => "–",
            SpanOutcome.Blocked => "⏳",
            SpanOutcome.Fatal   => "💥",
            _                   => "?",
        };

        // 트리 접두사까지 포함해 열을 맞춘다 — 안 그러면 깊이마다 어긋난다.
        var kind = s.Kind is SpanKind.Node or SpanKind.Run ? "" : s.Kind.ToString().ToLowerInvariant() + " ";
        var label = head + kind + s.Name;
        if (label.Length > LabelColumn)
            label = label[..(LabelColumn - 1)] + "…";
        label = label.PadRight(LabelColumn);

        var time = s.Ms >= 1000 ? $"{s.Ms / 1000:F1}s" : $"{s.Ms:F0}ms";

        var tail = new StringBuilder($"{label} {mark} {time,7}");
        if (s.BlamedOn is not null)
            tail.Append($"  ← blocked by {s.BlamedOn}");
        if (!string.IsNullOrWhiteSpace(s.Log))
            tail.Append($"  {Clip(s.Log!, 64)}");
        if (s.Errors is { Count: > 0 })
            tail.Append($"  [{s.Errors.Count} error(s)]");
        if (s.Artifacts is { Count: > 0 })
            tail.Append($"  → {string.Join(", ", s.Artifacts)}");
        return tail.ToString().TrimEnd();
    }

    private static string? ReadHeader(string runDir)
    {
        var manifest = Path.Combine(runDir, "run.json");
        if (!File.Exists(manifest))
            return null;
        try
        {
            var m = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(manifest), Options);
            if (m is null)
                return null;
            var verdict = m.Success ? "✅" : "❌";
            return $"run {m.RunId}  \"{Clip(m.Goal, 60)}\"  {verdict} {m.Steps} step(s), {m.WallClockMs / 1000:F1}s" +
                   $"\n  backend {m.Backend} · target {m.Target} · skills {(m.Skills.Count == 0 ? "none" : string.Join(",", m.Skills))}";
        }
        catch { return null; }
    }

    private static string Clip(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
