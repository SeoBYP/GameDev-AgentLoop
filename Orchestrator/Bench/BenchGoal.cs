using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Bench;

/// <summary>
/// 벤치마크 목표 하나. `Benchmark/goals.jsonl` 한 줄에 대응한다.
///
/// `set` 이 핵심이다 — **train / holdout 분리가 없으면 자기개선을 정직하게 주장할 수 없다.**
/// 학습(스킬 증류·예산 캘리브레이션)에 쓴 목표에서만 잘해지는 것과
/// 실제로 나아진 것을 구분할 방법이 사라지기 때문이다(ARCHITECTURE §10).
/// </summary>
public sealed record BenchGoal(
    string Id,
    string Goal,
    string Set,                      // "train" | "holdout"
    IReadOnlyList<string> Tags,
    string? Target = null)           // 비우면 실행 시 지정한 타깃
{
    public static IReadOnlyList<BenchGoal> Load(string path)
    {
        var goals = new List<BenchGoal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (line, n) in File.ReadLines(path).Select((l, i) => (l, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;

            BenchGoal? g;
            try { g = JsonSerializer.Deserialize<BenchGoal>(line, Options); }
            catch (Exception ex) { throw new FormatException($"{path}:{n} — {ex.Message}"); }

            if (g is null || string.IsNullOrWhiteSpace(g.Id) || string.IsNullOrWhiteSpace(g.Goal))
                throw new FormatException($"{path}:{n} — every goal needs an 'id' and a 'goal'.");
            if (!seen.Add(g.Id))
                throw new FormatException($"{path}:{n} — duplicate id '{g.Id}'.");

            goals.Add(g with { Set = string.IsNullOrWhiteSpace(g.Set) ? "train" : g.Set.ToLowerInvariant() });
        }
        return goals;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>한 목표의 실행 결과.</summary>
public sealed record BenchResult(
    string Id,
    string Set,
    IReadOnlyList<string> Tags,
    bool   Success,
    int    Steps,
    double WallClockMs,
    string Summary,
    string? RunId);

/// <summary>
/// 한 번의 벤치마크 실행 요약. **이후 모든 개선은 이 파일 대비로만 말한다.**
/// 지표는 세 개뿐이다 — 성공률 · 평균 스텝 · 벽시계.
/// </summary>
public sealed record BenchSummary(
    string BenchId,
    string StartedAt,
    string Backend,
    string Target,
    string? Model,
    int MaxSteps,
    IReadOnlyList<string> Skills,
    IReadOnlyList<BenchResult> Results)
{
    public BenchStats All      => BenchStats.From("all", Results);
    public BenchStats Train    => BenchStats.From("train", Results.Where(r => r.Set == "train").ToList());
    public BenchStats Holdout  => BenchStats.From("holdout", Results.Where(r => r.Set == "holdout").ToList());
}

public sealed record BenchStats(string Set, int Total, int Passed, double SuccessRate, double MeanSteps, double MeanWallClockMs)
{
    public static BenchStats From(string set, IReadOnlyList<BenchResult> rs)
    {
        if (rs.Count == 0)
            return new BenchStats(set, 0, 0, 0, 0, 0);

        var passed = rs.Where(r => r.Success).ToList();
        return new BenchStats(
            set,
            rs.Count,
            passed.Count,
            Math.Round(100.0 * passed.Count / rs.Count, 1),
            // 평균 스텝은 **통과한 것만** 센다 — 실패는 maxSteps 로 잘려 평균을 왜곡한다.
            passed.Count == 0 ? 0 : Math.Round(passed.Average(r => r.Steps), 2),
            Math.Round(rs.Average(r => r.WallClockMs), 0));
    }
}
