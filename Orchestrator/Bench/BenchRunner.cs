using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orchestrator.Contracts;
using Orchestrator.Loop;
using Orchestrator.Skills;
using Orchestrator.Targets;
using Orchestrator.Trace;

namespace Orchestrator.Bench;

/// <summary>
/// 목표 세트를 통째로 돌려 **비교 가능한 숫자**를 만든다(ARCHITECTURE §10).
///
/// 왜 필요한가: 자기개선은 자기기만이 특히 쉬운 영역이다 —
/// 학습에 쓴 목표에서만 잘해지는 것을 "똑똑해졌다"로 착각하기 쉽다.
/// 베이스라인을 **루프가 바뀌기 전에** 찍어 둬야 이후 개선을 홀드아웃 대비로 말할 수 있다.
///
/// 목표마다 프로젝트에 파일이 쌓이므로, 실행 전 스냅샷을 떠서 **새로 생긴 파일만** 지운다.
/// (원래 있던 파일은 절대 건드리지 않는다.)
/// </summary>
public sealed class BenchRunner
{
    private readonly IAgentBackend _backend;
    private readonly IExecTarget _target;
    private readonly ProjectLayout _layout;
    private readonly string _projectPath;
    private readonly IReadOnlyList<Skill> _skills;
    private readonly LoopOptions _template;

    public BenchRunner(
        IAgentBackend backend, IExecTarget target, ProjectLayout layout,
        string projectPath, IReadOnlyList<Skill> skills, LoopOptions template)
    {
        _backend = backend;
        _target = target;
        _layout = layout;
        _projectPath = projectPath;
        _skills = skills;
        _template = template;
    }

    public async Task<BenchSummary> RunAsync(
        IReadOnlyList<BenchGoal> goals, string benchId, string runsRoot,
        string? model, CancellationToken ct)
    {
        var results = new List<BenchResult>();
        var startedAt = DateTime.Now;

        Console.WriteLine($"benchmark {benchId} — {goals.Count} goal(s), backend {_backend.Name}, target {_target.Name}");
        Console.WriteLine(new string('═', 78));

        for (var i = 0; i < goals.Count; i++)
        {
            var g = goals[i];
            Console.WriteLine($"\n[{i + 1}/{goals.Count}] {g.Id}  ({g.Set})");
            Console.WriteLine(new string('─', 78));

            var before = Snapshot();
            var store = RunStore.Create(_projectPath, Path.Combine(runsRoot, g.Id), DateTime.Now);
            var wall = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            int steps = 0;
            string summary;

            try
            {
                var trace = new RunTrace(store);
                var loop = new AgentLoop(_backend, _target, _template, _skills, trace: trace);

                using (var runSpan = trace.Begin(SpanKind.Run, g.Goal))
                {
                    var r = await loop.RunAsync(g.Goal, ct);
                    success = r.Success;
                    steps = r.Steps;
                    summary = r.Summary;
                    if (r.Success) runSpan.Pass(r.Summary); else runSpan.Fail(log: r.Summary);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 한 목표가 터져도 나머지는 계속 돈다 — 부분 결과가 없는 것보다 낫다.
                summary = $"error: {ex.Message}";
                Console.Error.WriteLine($"  ⚠ {summary}");
            }

            wall.Stop();
            results.Add(new BenchResult(
                g.Id, g.Set, g.Tags, success, steps,
                Math.Round(wall.Elapsed.TotalMilliseconds, 0), summary, store.RunId));

            Console.WriteLine($"  → {(success ? "✅" : "❌")} {steps} step(s), {wall.Elapsed.TotalSeconds:F1}s — {summary}");

            await CleanupAsync(before, ct);
        }

        return new BenchSummary(
            benchId, startedAt.ToString("o"), _backend.Name, _target.Name, model,
            _template.MaxSteps, _skills.Select(s => s.Name).ToList(), results);
    }

    // ── 프로젝트 원상복구 ─────────────────────────────────────────────────────
    // 생성물이 쌓이면 다음 목표의 컴파일에 영향을 준다(같은 타입 이름 충돌 등).
    // 스냅샷 대비 **새로 생긴 것만** 지운다.

    private HashSet<string> Snapshot()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in TrackedDirs())
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                    files.Add(f);
        return files;
    }

    private IEnumerable<string> TrackedDirs()
    {
        yield return Path.Combine(_projectPath, _layout.ScriptDir.Replace('/', Path.DirectorySeparatorChar));
        yield return Path.Combine(_projectPath, _layout.TestDir.Replace('/', Path.DirectorySeparatorChar));
    }

    private async Task CleanupAsync(HashSet<string> before, CancellationToken ct)
    {
        var removed = 0;
        foreach (var f in Snapshot())
        {
            if (before.Contains(f))
                continue;
            try
            {
                File.Delete(f);
                if (File.Exists(f + ".meta"))
                    File.Delete(f + ".meta");
                removed++;
            }
            catch { /* 지우지 못해도 벤치는 계속한다 */ }
        }

        if (removed == 0)
            return;

        Console.WriteLine($"  🧹 removed {removed} generated file(s), recompiling…");
        try
        {
            // 지운 뒤 컴파일을 확인해 다음 목표가 깨끗한 상태에서 시작하게 한다.
            var verify = await _target.VerifyAsync(new VerifySpec(VerifyKind.Compile), ct);
            if (!verify.Ok)
                Console.Error.WriteLine($"  ⚠ project is not green after cleanup: {verify.Errors.FirstOrDefault()}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Console.Error.WriteLine($"  ⚠ cleanup recompile failed: {ex.Message}"); }
    }

    // ── 보고 ─────────────────────────────────────────────────────────────────

    public static string Report(BenchSummary s)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(new string('═', 78));
        sb.AppendLine($"benchmark {s.BenchId}   backend {s.Backend} · target {s.Target}" +
                      (s.Model is null ? "" : $" · model {s.Model}"));
        sb.AppendLine(new string('═', 78));
        sb.AppendLine($"{"goal",-20} {"set",-8} {"result",-8} {"steps",6} {"time",9}");
        sb.AppendLine(new string('─', 78));

        foreach (var r in s.Results)
            sb.AppendLine($"{Clip(r.Id, 20),-20} {r.Set,-8} {(r.Success ? "pass" : "FAIL"),-8} " +
                          $"{(r.Success ? r.Steps.ToString() : "-"),6} {r.WallClockMs / 1000,8:F1}s");

        sb.AppendLine(new string('─', 78));
        foreach (var st in new[] { s.All, s.Train, s.Holdout })
        {
            if (st.Total == 0)
                continue;
            sb.AppendLine($"{st.Set,-10} {st.Passed}/{st.Total} passed ({st.SuccessRate}%)   " +
                          $"mean steps {st.MeanSteps} (passing only)   mean {st.MeanWallClockMs / 1000:F1}s");
        }

        sb.AppendLine();
        sb.AppendLine("Compare later runs against the holdout row only — the training goals are the ones");
        sb.AppendLine("future skills and budgets get tuned on, so improvement there proves nothing.");
        return sb.ToString();
    }

    public static string WriteSummary(string outDir, BenchSummary s)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "summary.json");
        File.WriteAllText(path, JsonSerializer.Serialize(s, Options));
        return path;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
