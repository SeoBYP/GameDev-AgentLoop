using System.Text.Json;

namespace Orchestrator.Bench;

/// <summary>
/// 저장된 결함 하나 — 실제 모델이 냈고 실제 검증이 잡은 첫 응답.
///
/// `Benchmark/faults/*.json` 은 트레이스에서 추출한다. 손으로 쓰지 않는다:
/// 오늘까지 "이건 모델이 틀릴 것"이라는 내 예측은 세 번 다 빗나갔다
/// (검사 5개 무득점 · 예산 12ms · hard 목표 12/12 원샷).
/// </summary>
public sealed record BenchFault(
    string Id,
    string Goal,
    string Reply,                    // 첫 응답 원문 (FILE/ASSERT 블록 포함)
    string CaughtBy,                 // 어느 노드가 잡았나 — 재현 확인에 쓴다
    string Failure,
    IReadOnlyList<string> Errors,
    int OriginalSteps,               // 원래 몇 스텝 만에 고쳐졌나(참고선)
    string Set = "train",
    string Source = "")
{
    public static IReadOnlyList<BenchFault> Load(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<BenchFault>();

        var faults = new List<BenchFault>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f))
        {
            BenchFault? f;
            try { f = JsonSerializer.Deserialize<BenchFault>(File.ReadAllText(file), Options); }
            catch (Exception ex) { throw new FormatException($"{file} — {ex.Message}"); }

            if (f is null || string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.Reply))
                throw new FormatException($"{file} — a fault needs an 'id' and a 'reply'.");
            faults.Add(f);
        }
        return faults;
    }

    /// <summary>벤치 러너가 목표처럼 다룰 수 있게 변환한다(tier 는 "fault").</summary>
    public BenchGoal ToGoal() => new(Id, Goal, Set, new[] { "fault", CaughtBy }, "fault");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
