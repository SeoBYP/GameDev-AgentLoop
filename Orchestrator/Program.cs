using Orchestrator.Backends;
using Orchestrator.Bench;
using Orchestrator.Contracts;
using Orchestrator.Loop;
using Orchestrator.Skills;
using Orchestrator.Targets;
using Orchestrator.Trace;
using Orchestrator.Util;

// ─────────────────────────────────────────────────────────────────────────────
// 오케스트레이터 진입점 — 루프 5단계를 조립·실행한다.
//
//   orchestrator "목표 문장"            # ApiBackend(실제 두뇌, ANTHROPIC_API_KEY 필요)
//   orchestrator --demo                 # ScriptedBackend(키 없이 자가수리 루프 증명)
//   orchestrator --demo --max-steps 4
//   orchestrator "..." --model claude-sonnet-5 --project C:\path\to\UnityProject
// ─────────────────────────────────────────────────────────────────────────────

var opts = ParseArgs(args);

if (opts.Help)
{
    Console.WriteLine(HelpText.Usage);
    return 0;
}

// 1) Unity 프로젝트 루트 해석 (인자 → 환경변수 → cwd에서 위로 탐색)
var projectPath = opts.ProjectPath
    ?? Environment.GetEnvironmentVariable("UNITY_PROJECT_PATH")
    ?? FindUnityProjectRoot(Directory.GetCurrentDirectory());

if (projectPath is null || !Directory.Exists(Path.Combine(projectPath, "Assets")))
{
    Console.Error.WriteLine("No Unity project root found (no Assets/ folder). Pass --project <path>.");
    return 2;
}

// 1-a) .env 로드 — 자격 증명을 OS 전역이 아닌 레포 루트 파일로 관리한다(.gitignore 로 보호됨).
//      여기서 올린 값은 자식 프로세스(`ugs`/`claude`/`codex`)에도 그대로 상속된다.
var envFile = opts.EnvFile ?? Path.Combine(projectPath, ".env");
var loadedKeys = DotEnv.Load(envFile);
if (loadedKeys.Count > 0)
    Console.WriteLine($".env loaded: {string.Join(", ", loadedKeys)}");   // 키 이름만 — 값은 절대 출력하지 않는다

// 1-a2) 대상 프로젝트의 배치 해석 — 경로·어셈블리는 프로젝트마다 다르다.
var layout = ProjectLayout.Resolve(projectPath);

if (opts.Init)
    return ProjectInit.Run(projectPath, layout);

// 지난 실행의 트레이스를 트리로 다시 세워 본다(기본: 가장 최근 실행).
if (opts.ShowTraceOnly)
{
    var dir = opts.TraceRunDir ?? RunStore.FindLatest(projectPath, opts.RunsDir);
    if (dir is null)
    {
        Console.Error.WriteLine("No runs found. Run agentloop once first.");
        return 2;
    }
    Console.WriteLine(TraceTree.Render(dir));
    return 0;
}

// 1-b) 도메인 스킬 로드 (Phase 3) — 포터블 마크다운이라 모든 백엔드에 동일하게 적용된다.
//      스킬은 **오케스트레이터와 함께 배포**된다. 대상 프로젝트의 Skills/ 는 있으면 우선한다
//      (남의 Unity 프로젝트를 가리켰을 때 조용히 0개가 되는 걸 막는다).
var skillsDir = opts.SkillsDir ?? ResolveSkillsDir(projectPath);
var library = opts.SkillsOff ? SkillLibrary.Empty : SkillLibrary.Load(skillsDir);
var selectedSkills = library.Select(opts.Goal, opts.Target);

if (opts.ListSkills)
{
    Console.WriteLine($"Skills directory: {skillsDir}");
    if (library.All.Count == 0)
    {
        Console.WriteLine("  (no skills found)");
    }
    foreach (var s in library.All)
    {
        var mark = selectedSkills.Contains(s) ? "✓" : " ";
        Console.WriteLine($"  [{mark}] {s.Name} — {s.Title}  ({s.Checks.Count} checks)");
        foreach (var c in s.Checks)
            Console.WriteLine($"        · {c.Id}  [{string.Join(", ", c.Scopes)}]");
    }
    return 0;
}

// 1-b2) 벤치마크 목표는 **일찍** 읽는다 — 깨진 목표 파일을 에디터 연결 확인 뒤에 알려 줄 이유가 없고,
//       90분짜리 실행이 시작 직후 죽는 것도 막는다.
IReadOnlyList<BenchGoal>? benchGoals = null;
string? benchGoalsPath = null;
if (opts.Bench && !opts.BenchFaults)
{
    var goalsPath = opts.BenchGoals ?? ResolveBenchGoals(projectPath);
    benchGoalsPath = goalsPath;
    if (goalsPath is null || !File.Exists(goalsPath))
    {
        Console.Error.WriteLine("No benchmark goals found. Use --bench-goals <path>.");
        return 2;
    }
    Console.WriteLine($"Benchmark goals: {goalsPath}");

    benchGoals = BenchGoal.Load(goalsPath)
        .Where(g => opts.BenchTier is null or "all" || g.Tier.Equals(opts.BenchTier, StringComparison.OrdinalIgnoreCase))
        .Where(g => opts.BenchSet is null or "all" || g.Set.Equals(opts.BenchSet, StringComparison.OrdinalIgnoreCase))
        .Where(g => opts.BenchFilter is null || g.Id.Contains(opts.BenchFilter, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (benchGoals.Count == 0)
    {
        Console.Error.WriteLine("No goals matched the filter.");
        return 2;
    }
}

// 1-b3) 결함 라이브러리 — 저장된 "실패한 첫 응답"으로 **수리 능력**을 잰다(§10).
IReadOnlyList<BenchFault>? benchFaults = null;
if (opts.BenchFaults)
{
    var faultsDir = opts.FaultsDir
        ?? Path.Combine(Path.GetDirectoryName(benchGoalsPath ?? ResolveBenchGoals(projectPath) ?? ".")!, "faults");
    benchFaults = BenchFault.Load(faultsDir)
        .Where(f => opts.BenchSet is null or "all" || f.Set.Equals(opts.BenchSet, StringComparison.OrdinalIgnoreCase))
        .Where(f => opts.BenchFilter is null || f.Id.Contains(opts.BenchFilter, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (benchFaults.Count == 0)
    {
        Console.Error.WriteLine($"No faults found in {faultsDir}.");
        return 2;
    }
    Console.WriteLine($"Fault library: {faultsDir} ({benchFaults.Count} fault(s))");
    benchGoals = benchFaults.Select(f => f.ToGoal()).ToList();
}

// 1-c) 손(타깃) 선택 — Unity 에디터(클라) vs UGS Cloud Code(백엔드)
IExecTarget target;
if (opts.Target.Equals("ugs", StringComparison.OrdinalIgnoreCase))
{
    var deployDir = opts.CloudCodeDir ?? Path.Combine(projectPath, "CloudCode");
    // 프로젝트/환경은 인자 → 환경변수(.env 포함) 순으로 해석한다.
    var ugsProjectId = opts.UgsProjectId ?? Environment.GetEnvironmentVariable("UGS_CLI_PROJECT_ID");
    var ugsEnv = opts.UgsEnvironment ?? Environment.GetEnvironmentVariable("UGS_CLI_ENVIRONMENT_NAME");
    var ugsEnvId = opts.UgsEnvironmentId ?? Environment.GetEnvironmentVariable("UGS_CLI_ENVIRONMENT_ID");
    target = new UgsTarget(projectPath, deployDir, ugsProjectId, ugsEnv, ugsEnvId);
}
else
{
    var unityExe = UnityEditorTarget.ResolveUnityExe();
    Console.WriteLine($"Project layout: {layout.Describe()}");
    if (!layout.TestsReady)
        Console.WriteLine("  ⚠ No test assembly — test verification is skipped. Run `agentloop --init` to enable it.");

    target = new UnityEditorTarget(
        unityExe, projectPath,
        label: $"unity:{ReadEditorVersion(projectPath)}",
        layout: layout,
        allowUnsafeEval: opts.AllowUnsafeEval,
        inlinePerf: opts.Perf);
}

// 1-d) 조립된 시스템 프롬프트만 보고 끝내기(디버깅/설명용) — 인증 없이도 타깃별 규격을 확인할 수 있다.
if (opts.PrintPrompt)
{
    Console.WriteLine($"# target: {target.Name}   runtime verification: {target.Supports(VerifyKind.RuntimeAssert)}");
    Console.WriteLine($"# skills: {(selectedSkills.Count == 0 ? "none" : string.Join(", ", selectedSkills.Select(s => s.Name)))}");
    Console.WriteLine(new string('─', 70));
    Console.WriteLine(AgentLoop.BuildSystemPrompt(target, selectedSkills));
    return 0;
}

// 2) 백엔드 선택 (두뇌 = pluggable)
IAgentBackend backend;
if (opts.Demo)
{
    backend = new ScriptedBackend(DemoScript.BrokenThenFixedHealth);
}
else if (opts.DemoPlay)
{
    backend = new ScriptedBackend(DemoScript.CompilesButWrongStamina);
}
else if (opts.DemoSkills)
{
    backend = new ScriptedBackend(DemoScript.SkillViolationThenFixed);
}
else if (opts.DemoPerf)
{
    backend = new ScriptedBackend(DemoScript.CorrectButSlowThenFast);
}
else if (opts.DemoDraw)
{
    backend = new ScriptedBackend(DemoScript.FastButDrawHeavy);
}
else if (opts.Claude)
{
    // "이 AI 채팅"(Claude Code CLI)을 두뇌로 — 별도 API 키 불필요(CLI 로그인만).
    backend = new ClaudeCodeBackend(opts.Model ?? "sonnet", BackendWorkDir("claude"));
}
else if (opts.Codex)
{
    // Codex CLI 를 두뇌로 — 또 다른 독립 에이전트(agent-agnostic 증명).
    backend = new CodexBackend(opts.Model, BackendWorkDir("codex"));
}
else
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine(
            """
            Pick a backend (the brain):
              • Claude Code CLI:   agentloop --claude "<goal>"     (needs `claude` login, no API key)
              • Codex CLI:         agentloop --codex  "<goal>"     (needs `codex` login, no API key)
              • Anthropic API:     set ANTHROPIC_API_KEY, then  agentloop "<goal>"
              • No key at all:     agentloop --demo        (compile self-repair)
                                   agentloop --demo-play   (compiles, but behaves wrong)
            Never commit API keys to the repository.
            """);
        return 2;
    }
    backend = new ApiBackend(apiKey, opts.Model ?? "claude-opus-5");
}

// 3) 루프 조립·실행
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// 실행 기록 — 학습 계층(Calibrator·Distiller)도 재개도 전부 이 기록 위에 선다.
// %TEMP% 가 아니라 프로젝트 안에 남긴다: 매일 버리던 재료를 줍는 것이다(ARCHITECTURE §6.4).
//
// **연결 확인보다 먼저** 만든다. 인프라 실패(Fatal)도 결과이고, 기록되지 않으면
// "왜 아무 일도 안 일어났는지"가 사라진다.
var startedAt = DateTime.Now;
var store = RunStore.Create(projectPath, opts.RunsDir, startedAt);
var trace = new RunTrace(store);

// 전제 확인: 손이 준비됐는가(에디터 연결 / UGS 인증). 안 되어 있으면 AI 호출 전에 빠르게 실패.
if (!await target.IsConnectedAsync(cts.Token))
{
    Console.Error.WriteLine(target.ConnectionHint);

    using (var s = trace.Begin(SpanKind.Run, opts.Goal))
        s.Fatal($"{target.Name} unreachable — {target.ConnectionHint.Split('\n')[0]}");

    store.WriteManifest(NotStarted(store, startedAt, opts, backend, target, selectedSkills, layout,
                                   "target unreachable"));
    Console.Error.WriteLine($"📁 run: {store.Root}");
    return 2;
}

var loopOptions = new LoopOptions
{
    MaxSteps = opts.MaxSteps,
    Assert = opts.Assert,   // 사람이 준 런타임 검증 기준(있으면 AI 의 ASSERT 블록보다 우선)
    CaptureDir = opts.Capture ? (opts.CaptureDir ?? Path.Combine(store.Root, "evidence")) : null,
    HistoryWindow = opts.HistoryWindow,
    VerifyMode = opts.TestsOnly ? VerifyMode.TestsOnly : VerifyMode.Auto,
    RunsDir = opts.RunsDir,
};

// 벤치마크 — 목표 세트를 통째로 돌려 비교 가능한 숫자를 만든다(ARCHITECTURE §10).
if (opts.Bench || opts.BenchFaults)
{
    var benchId = startedAt.ToString("yyyyMMdd-HHmmss");
    var benchRuns = Path.Combine(projectPath, ".agentloop", "bench", benchId);
    // 요약(숫자)은 **목표 파일 옆**에 쌓는다 — 대상은 빈 샌드박스라 거기 두면 비교 이력이 흩어진다.
    var resultsRoot = Path.GetDirectoryName(benchGoalsPath ?? ResolveBenchGoals(projectPath) ?? ".")!;
    var benchOut = opts.BenchOut
        ?? Path.Combine(resultsRoot, benchFaults is not null ? "fault-results" : "results", benchId);

    var runner = new BenchRunner(backend, target, layout, projectPath, selectedSkills, loopOptions);

    // 결함 모드: 목표마다 저장된 "실패한 첫 응답"을 재생하고, 이후는 실제 모델이 수리한다.
    Func<BenchGoal, IAgentBackend>? backendFor = null;
    if (benchFaults is not null)
    {
        var byId = benchFaults.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        backendFor = g => new SeededFaultBackend(g.Id, byId[g.Id].Reply, backend);
    }

    var summary = await runner.RunAsync(
        benchGoals!, benchId, benchRuns, opts.Model,
        benchFaults is not null ? "fault" : (opts.BenchTier ?? "all"), cts.Token, backendFor);

    Console.WriteLine(BenchRunner.Report(summary));
    Console.WriteLine($"📁 summary: {BenchRunner.WriteSummary(benchOut, summary)}");
    Console.WriteLine($"📁 runs:    {benchRuns}");
    return summary.Results.All(r => r.Success) ? 0 : 1;
}

var loop = new AgentLoop(backend, target, loopOptions, selectedSkills, trace: trace);

var wall = System.Diagnostics.Stopwatch.StartNew();
LoopResult? outcome = null;
try
{
    var runSpan = trace.Begin(SpanKind.Run, opts.Goal);
    var result = await loop.RunAsync(opts.Goal, cts.Token);
    outcome = result;
    if (result.Success) runSpan.Pass(result.Summary); else runSpan.Fail(log: result.Summary);
    runSpan.Dispose();

    Console.WriteLine();
    Console.WriteLine(result.Success ? $"✅ SUCCESS — {result.Summary}" : $"❌ FAILED — {result.Summary}");
    Console.WriteLine($"📁 run: {store.Root}");
    if (opts.ShowTrace)
    {
        Console.WriteLine();
        Console.WriteLine(TraceTree.Render(store.Root));
    }
    return result.Success ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled by user.");
    return 130;
}
catch (Exception ex)
{
    // 백엔드/타깃 실패(예: claude 로그인 만료, unity 서버 미연결)를 깔끔히 보고.
    Console.Error.WriteLine($"\n❌ ERROR — {ex.Message}");
    return 1;
}
finally
{
    // 실패·중단이어도 남긴다 — 실패한 실행이야말로 학습 재료다.
    store.WriteManifest(new RunManifest(
        RunId: store.RunId,
        StartedAt: startedAt.ToString("o"),
        Goal: opts.Goal,
        Backend: backend.Name,
        Target: target.Name,
        Model: opts.Model,
        MaxSteps: opts.MaxSteps,
        VerifyMode: loopOptions.VerifyMode.ToString(),
        HistoryWindow: opts.HistoryWindow,
        Skills: selectedSkills.Select(s => s.Name).ToList(),
        ProjectLayout: layout.Describe(),
        Success: outcome?.Success ?? false,
        Steps: outcome?.Steps ?? 0,
        Summary: outcome?.Summary ?? "interrupted",
        WallClockMs: Math.Round(wall.Elapsed.TotalMilliseconds, 1)));

    (backend as IDisposable)?.Dispose();
}

// ── 로컬 헬퍼 ────────────────────────────────────────────────────────────────

static Options ParseArgs(string[] args)
{
    var o = new Options();
    var positional = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--demo": o.Demo = true; break;
            case "--demo-play": o.DemoPlay = true; break;
            case "--demo-skills": o.DemoSkills = true; break;
            // 성능 데모는 성능 검증이 켜져 있어야 의미가 있다 — 자기 전제를 스스로 켠다.
            case "--demo-perf": o.DemoPerf = true; o.Perf = true; break;
            case "--demo-draw": o.DemoDraw = true; o.Perf = true; break;
            case "--perf": o.Perf = true; break;
            case "--capture": o.Capture = true; break;
            case "--allow-unsafe-eval": o.AllowUnsafeEval = true; break;
            case "--tests-only": o.TestsOnly = true; break;
            case "--history-window" when i + 1 < args.Length: o.HistoryWindow = int.Parse(args[++i]); break;
            case "--capture-dir" when i + 1 < args.Length: o.CaptureDir = args[++i]; o.Capture = true; break;
            case "--claude": o.Claude = true; break;
            case "--codex": o.Codex = true; break;
            case "--assert" when i + 1 < args.Length: o.Assert = args[++i]; break;
            case "--skills" when i + 1 < args.Length: o.SkillsOff = args[++i].Equals("off", StringComparison.OrdinalIgnoreCase); break;
            case "--skills-dir" when i + 1 < args.Length: o.SkillsDir = args[++i]; break;
            case "--list-skills": o.ListSkills = true; break;
            case "--init": o.Init = true; break;
            case "--help" or "-h" or "-?": o.Help = true; break;
            case "--trace": o.ShowTrace = true; break;
            case "--bench": o.Bench = true; break;
            case "--bench-set" when i + 1 < args.Length: o.BenchSet = args[++i]; break;
            case "--bench-tier" when i + 1 < args.Length: o.BenchTier = args[++i]; break;
            case "--bench-faults": o.BenchFaults = true; break;
            case "--faults-dir" when i + 1 < args.Length: o.FaultsDir = args[++i]; break;
            case "--bench-filter" when i + 1 < args.Length: o.BenchFilter = args[++i]; break;
            case "--bench-goals" when i + 1 < args.Length: o.BenchGoals = args[++i]; break;
            case "--bench-out" when i + 1 < args.Length: o.BenchOut = args[++i]; break;
            case "--runs-dir" when i + 1 < args.Length: o.RunsDir = args[++i]; break;
            case "--show-trace": o.ShowTraceOnly = true; break;
            case "--show-trace-run" when i + 1 < args.Length: o.ShowTraceOnly = true; o.TraceRunDir = args[++i]; break;
            case "--print-prompt": o.PrintPrompt = true; break;
            case "--env-file" when i + 1 < args.Length: o.EnvFile = args[++i]; break;
            case "--target" when i + 1 < args.Length: o.Target = args[++i]; break;
            case "--ugs-project-id" when i + 1 < args.Length: o.UgsProjectId = args[++i]; break;
            case "--ugs-env" when i + 1 < args.Length: o.UgsEnvironment = args[++i]; break;
            case "--ugs-env-id" when i + 1 < args.Length: o.UgsEnvironmentId = args[++i]; break;
            case "--cloud-code-dir" when i + 1 < args.Length: o.CloudCodeDir = args[++i]; break;
            case "--max-steps" when i + 1 < args.Length: o.MaxSteps = int.Parse(args[++i]); break;
            case "--project" when i + 1 < args.Length: o.ProjectPath = args[++i]; break;
            case "--model" when i + 1 < args.Length: o.Model = args[++i]; break;
            default: positional.Add(args[i]); break;
        }
    }
    if (positional.Count > 0)
    {
        o.Goal = string.Join(' ', positional);
    }
    else
    {
        // 데모는 목표를 무시하고 스크립트를 재생하므로, 로그가 산출물과 맞도록 기본 목표를 맞춰 준다.
        if (o.DemoPlay)
            o.Goal = "A stamina component with Use(int)/Recover(int) that never leaves the 0..Max range.";
        else if (o.DemoSkills)
            o.Goal = "A Follower component that chases a target; speed and target are set in the inspector.";
        else if (o.DemoPerf)
            o.Goal = "A ScoreTracker component; Record(int) may be called every frame.";
        else if (o.DemoDraw)
            o.Goal = "A TileField component that spawns a grid of floor tiles via Build().";
    }
    return o;
}

// 시작도 못 한 실행의 요약. 실패한 실행이야말로 학습 재료라 기록은 남긴다.
static RunManifest NotStarted(
    RunStore store, DateTime startedAt, Options o,
    IAgentBackend backend, IExecTarget target,
    IReadOnlyList<Skill> skills, ProjectLayout layout, string reason)
    => new(
        RunId: store.RunId,
        StartedAt: startedAt.ToString("o"),
        Goal: o.Goal,
        Backend: backend.Name,
        Target: target.Name,
        Model: o.Model,
        MaxSteps: o.MaxSteps,
        VerifyMode: (o.TestsOnly ? VerifyMode.TestsOnly : VerifyMode.Auto).ToString(),
        HistoryWindow: o.HistoryWindow,
        Skills: skills.Select(s => s.Name).ToList(),
        ProjectLayout: layout.Describe(),
        Success: false,
        Steps: 0,
        Summary: reason,
        WallClockMs: 0);

// 벤치 목표 해석 — 스킬과 같은 규칙이다. 목표 세트는 **대상 프로젝트가 아니라 도구에** 딸려 있다.
// (벤치는 빈 샌드박스 프로젝트를 대상으로 도는 게 정상이라, 대상 기준으로 찾으면 절대 못 찾는다.)
static string? ResolveBenchGoals(string projectPath)
{
    var projectLocal = Path.Combine(projectPath, "Benchmark", "goals.jsonl");
    if (File.Exists(projectLocal))
        return projectLocal;

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "Benchmark", "goals.jsonl");
        // 빌드 출력본(bin/obj)은 건너뛴다 — 개발 중에 그걸 잡으면 결과 요약이
        // 추적되지 않는 bin/ 안에 쌓여 비교 이력이 사라진다. 설치본 경로엔 bin/obj 가 없다.
        if (File.Exists(candidate) && !IsBuildOutput(candidate))
            return candidate;
        dir = dir.Parent;
    }
    return null;

    static bool IsBuildOutput(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}

// 스킬 디렉터리 해석 — 대상 프로젝트의 Skills/ 가 있으면 우선, 없으면 오케스트레이터와 함께 배포된 것.
// (dev: bin/Debug/netX/ 에서 위로 올라가며 찾는다. 설치본: 실행 파일 옆.)
static string ResolveSkillsDir(string projectPath)
{
    var projectLocal = Path.Combine(projectPath, "Skills");
    if (HasSkillFiles(projectLocal))
        return projectLocal;

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "Skills");
        if (HasSkillFiles(candidate))
            return candidate;
        dir = dir.Parent;
    }
    return projectLocal;   // 없으면 원래 자리를 가리켜 둔다(빈 라이브러리로 로드됨)

    // 이름만으로는 부족하다 — `Orchestrator/Skills/` 는 같은 이름의 **C# 소스** 폴더라 먼저 걸린다.
    // 실제 스킬은 마크다운이므로 .md 가 있는지까지 봐야 한다.
    static bool HasSkillFiles(string path)
    {
        try { return Directory.Exists(path) && Directory.EnumerateFiles(path, "*.md").Any(); }
        catch { return false; }
    }
}

// cwd 에서 위로 올라가며 Assets/ + ProjectSettings/ 가 있는 폴더(Unity 루트)를 찾는다.
static string? FindUnityProjectRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Assets")) &&
            Directory.Exists(Path.Combine(dir.FullName, "ProjectSettings")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

// CLI 백엔드 격리 작업 폴더(샌드박스가 꺼져 있어도 프로젝트를 못 건드리게).
static string BackendWorkDir(string name)
{
    var dir = Path.Combine(Path.GetTempPath(), $"orchestrator-{name}-backend");
    Directory.CreateDirectory(dir);
    return dir;
}

static string ReadEditorVersion(string projectPath)
{
    try
    {
        var file = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        foreach (var line in File.ReadLines(file))
            if (line.StartsWith("m_EditorVersion:", StringComparison.Ordinal))
                return line["m_EditorVersion:".Length..].Trim();
    }
    catch { /* 무시 */ }
    return "unknown";
}

// ── 사용법 ───────────────────────────────────────────────────────────────────
// CLI 표면은 영어로 둔다 — 오픈소스 도구의 첫 진입점이라 가장 넓게 읽혀야 한다.
static class HelpText
{
    public const string Usage = """
        agentloop — a closed loop that makes AI-written Unity code actually run.

        USAGE
          agentloop "<goal in natural language>" [options]
          agentloop --init                       set up the target project for test-based verification
          agentloop --demo                       prove the loop works, no API key needed

        The Unity project is auto-detected by walking up from the current directory.
        The target project must be OPEN in the Unity Editor (the pipeline server runs inside it).
        Check with: unity pipeline list

        BACKEND (the brain — pick one; defaults to the Anthropic API)
          --claude                 use the Claude Code CLI (`claude -p`) — no API key needed
          --codex                  use the Codex CLI (`codex exec`)      — no API key needed
          --model <id>             model id for the chosen backend
                                   (API backend needs ANTHROPIC_API_KEY)

        TARGET (the hands)
          --target unity|ugs       Unity Editor (default) or UGS Cloud Code
          --project <path>         Unity project root (default: auto-detect, or UNITY_PROJECT_PATH)

        VERIFICATION
          --assert <c#>            supply your own runtime check instead of letting the model write one
          --tests-only             verify only through compiled test files; never eval temp snippets
          --perf                   also enforce a performance budget inside the loop (OFF by default:
                                   editor timings are a relative signal, not shipping performance —
                                   real numbers come from a build, measured separately)
          --capture                save a Game View screenshot as evidence on success
          --max-steps <n>          give up after n repair attempts (default 6)

        DOMAIN SKILLS
          --skills off             disable domain rule checks
          --skills-dir <path>      load skills from a different folder
          --list-skills            show which skills and checks would apply

        DEMOS (deterministic, no API key — each reproduces one class of failure)
          --demo                   compile error       -> self-repair
          --demo-play              compiles but behaves wrong
          --demo-skills            violates a domain rule -> rejected before apply
          --demo-perf              correct but allocates on the hot path
          --demo-draw              fast but floods draw calls

        BENCHMARK (measure the loop itself, so later improvements can be proven)
          --bench                  run every goal in Benchmark/goals.jsonl and report
          --bench-faults           replay recorded faults instead of generating from scratch:
                                   each run starts from a response that really failed verification,
                                   so the step count measures REPAIR, which is what the loop provides
          --faults-dir <path>      use a different fault library
          --bench-tier smoke|hard  which tier to run (default: all)
                                   smoke = fast regression sweep · hard = the measurement set
          --bench-set train|holdout|all
                                   which split to run (default: all)
          --bench-filter <text>    only goals whose id contains this
          --bench-goals <path>     use a different goal file
          --bench-out <dir>        where to write summary.json (default Benchmark/results/<id>/)

        RUN RECORDS
          Every run is recorded under <project>/.agentloop/runs/<runId>/ — a span trace,
          the model's raw replies, compiler output, and a manifest of the settings in effect.
          --trace                  print the span tree after the run finishes
          --show-trace             print the span tree of the most recent run and exit
          --show-trace-run <dir>   print the span tree of a specific run directory
          --runs-dir <path>        store run records somewhere else

        DIAGNOSTICS
          --print-prompt           show the assembled system prompt and exit
          --history-window <n>     turns of history to send (default 4; 0 = unlimited)
          --allow-unsafe-eval      bypass the static guard on eval snippets (not a sandbox either way)
          -h, --help               show this help

        Docs: https://github.com/SeoBYP/GameDev-AgentLoop
        """;
}

// ── 옵션/데모 스크립트 ────────────────────────────────────────────────────────

sealed class Options
{
    public string Goal { get; set; } =
        "A simple Health component with current/max HP, TakeDamage(int) and Heal(int); HP must never go below 0 or above max.";
    public int MaxSteps { get; set; } = 6;
    public bool Demo { get; set; }
    public bool DemoPlay { get; set; }
    public bool DemoSkills { get; set; }
    public bool DemoPerf { get; set; }
    public bool DemoDraw { get; set; }
    public bool Perf { get; set; }
    public bool Capture { get; set; }
    public bool AllowUnsafeEval { get; set; }
    public bool TestsOnly { get; set; }
    public int HistoryWindow { get; set; } = 4;
    public string? CaptureDir { get; set; }
    public bool Claude { get; set; }
    public bool Codex { get; set; }
    public string? Assert { get; set; }
    public bool SkillsOff { get; set; }
    public bool ListSkills { get; set; }
    public bool Init { get; set; }
    public bool Help { get; set; }
    public bool ShowTrace { get; set; }
    public bool ShowTraceOnly { get; set; }
    public string? TraceRunDir { get; set; }
    public string? RunsDir { get; set; }
    public bool Bench { get; set; }
    public string? BenchSet { get; set; }
    public string? BenchTier { get; set; }
    public bool BenchFaults { get; set; }
    public string? FaultsDir { get; set; }
    public string? BenchFilter { get; set; }
    public string? BenchGoals { get; set; }
    public string? BenchOut { get; set; }
    public bool PrintPrompt { get; set; }
    public string? EnvFile { get; set; }
    public string? SkillsDir { get; set; }
    public string Target { get; set; } = "unity";   // unity | ugs
    public string? UgsProjectId { get; set; }
    public string? UgsEnvironment { get; set; }
    public string? UgsEnvironmentId { get; set; }
    public string? CloudCodeDir { get; set; }
    public string? ProjectPath { get; set; }
    // 백엔드별 기본값이 달라 nullable(ApiBackend→claude-opus-5, ClaudeCodeBackend→sonnet).
    public string? Model { get; set; } = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL");
}

// --demo 스크립트: 1) 세미콜론 빠진 깨진 Health.cs → 2) (컴파일 에러 피드백 후) 고친 Health.cs.
// 자가수리 루프가 실제로 도는지 키 없이 결정적으로 증명한다.
static class DemoScript
{
    public static readonly IReadOnlyList<string> BrokenThenFixedHealth = new[]
    {
        // step 1 — 일부러 깨뜨림: `_max = 100` 뒤 세미콜론 누락 → CS1002
        """
        FILE: Assets/Scripts/DemoHealth.cs
        ```csharp
        using UnityEngine;

        public class DemoHealth : MonoBehaviour
        {
            [SerializeField] private int _max = 100

            public int Max => _max;
            public int Current { get; private set; }

            private void Awake() => Current = _max;

            public void TakeDamage(int amount) => Current = Mathf.Max(0, Current - amount);
            public void Heal(int amount)       => Current = Mathf.Min(_max, Current + amount);
        }
        ```
        """,

        // step 2 — 세미콜론 추가로 수정
        """
        FILE: Assets/Scripts/DemoHealth.cs
        ```csharp
        using UnityEngine;

        public class DemoHealth : MonoBehaviour
        {
            [SerializeField] private int _max = 100;

            public int Max => _max;
            public int Current { get; private set; }

            private void Awake() => Current = _max;

            public void TakeDamage(int amount) => Current = Mathf.Max(0, Current - amount);
            public void Heal(int amount)       => Current = Mathf.Min(_max, Current + amount);
        }
        ```
        """,
    };

    // --demo-play 스크립트: **컴파일은 통과하지만 동작이 틀린** 코드를 런타임 assert 가 잡아낸다.
    // step 1) 클램프 없는 Stamina → 컴파일 OK, 하지만 Use(500) 시 Current 가 음수가 된다.
    // step 2) (assert 실패 피드백 후) Mathf.Max/Min 클램프 추가 → 런타임 통과.
    // "컴파일 통과 ≠ 동작 정상" 을 보여주는 시나리오 — 이 프로젝트의 차별점(D4)의 핵심 증거.
    public static readonly IReadOnlyList<string> CompilesButWrongStamina = new[]
    {
        // step 1 — 컴파일은 되지만 클램프가 빠졌다.
        """
        FILE: Assets/Scripts/Stamina.cs
        ```csharp
        using UnityEngine;

        public class Stamina : MonoBehaviour
        {
            [SerializeField] private int _max = 100;

            public int Max => _max;
            public int Current { get; private set; }

            private void Awake() => Current = _max;

            public void Use(int amount)     => Current -= amount;
            public void Recover(int amount) => Current += amount;
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var s = go.AddComponent<Stamina>();
        s.Use(500);
        int afterUse = s.Current;
        s.Recover(9999);
        int afterRecover = s.Current;
        UnityEngine.Object.DestroyImmediate(go);
        if (afterUse != 0) return "After Use(500), Current should be 0 but was " + afterUse + ".";
        if (afterRecover != 100) return "After Recover(9999), Current should be Max(100) but was " + afterRecover + ".";
        return "OK";
        ```
        """,

        // step 2 — 클램프 추가로 수정(검증 기준은 그대로).
        """
        FILE: Assets/Scripts/Stamina.cs
        ```csharp
        using UnityEngine;

        public class Stamina : MonoBehaviour
        {
            [SerializeField] private int _max = 100;

            public int Max => _max;
            public int Current { get; private set; }

            private void Awake() => Current = _max;

            public void Use(int amount)     => Current = Mathf.Max(0, Current - amount);
            public void Recover(int amount) => Current = Mathf.Min(Max, Current + amount);
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var s = go.AddComponent<Stamina>();
        s.Use(500);
        int afterUse = s.Current;
        s.Recover(9999);
        int afterRecover = s.Current;
        UnityEngine.Object.DestroyImmediate(go);
        if (afterUse != 0) return "After Use(500), Current should be 0 but was " + afterUse + ".";
        if (afterRecover != 100) return "After Recover(9999), Current should be Max(100) but was " + afterRecover + ".";
        return "OK";
        ```
        """,
    };

    // --demo-skills 스크립트: 도메인 스킬의 **정적 검사가 실제로 반려하는** 경로를 결정적으로 보여준다.
    // step 1) public 필드 + Update 안 GetComponent/Debug.Log → 프로젝트에 **적용되기 전에** 반려.
    // step 2) 캡슐화 + Awake 캐싱으로 수정 → 통과.
    // 지침(프롬프트)으로 권고하는 데 그치지 않고 검사로 강제한다는 게 Phase 3 의 핵심.
    public static readonly IReadOnlyList<string> SkillViolationThenFixed = new[]
    {
        // step 1 — 컴파일은 되지만 도메인 규칙을 여럿 어긴다.
        """
        FILE: Assets/Scripts/Follower.cs
        ```csharp
        using UnityEngine;

        public class Follower : MonoBehaviour
        {
            public float speed = 3f;
            public Transform target;

            private void Update()
            {
                var body = GetComponent<Rigidbody>();
                Debug.Log("following");
                if (target != null)
                {
                    transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
                }
            }
        }
        ```
        """,

        // step 2 — 캡슐화 + Awake 캐싱 + 프레임 의존 분리로 수정.
        """
        FILE: Assets/Scripts/Follower.cs
        ```csharp
        using UnityEngine;

        public class Follower : MonoBehaviour
        {
            [SerializeField] private float _speed = 3f;
            [SerializeField] private Transform _target;

            private Rigidbody _body;

            public bool HasTarget => _target != null;

            private void Awake() => _body = GetComponent<Rigidbody>();

            private void Update() => Tick(Time.deltaTime);

            public void Tick(float deltaTime)
            {
                if (_target == null)
                {
                    return;
                }

                transform.position = Vector3.MoveTowards(transform.position, _target.position, _speed * deltaTime);
            }
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var f = go.AddComponent<Follower>();
        bool has = f.HasTarget;
        UnityEngine.Object.DestroyImmediate(go);
        return has ? "HasTarget is true even though no target is set." : "OK";
        ```
        """,
    };

    // --demo-draw 스크립트: **빠르지만 드로우콜을 폭증시키는** 코드를 렌더 예산이 잡아낸다.
    // 시간 예산은 통과한다(스폰 자체는 싸다) — 시간만 재면 놓치는 결함이라 렌더 예산이 따로 필요하다.
    // step 1) 타일을 하나씩 개별 GameObject 로 스폰(드로우콜 폭증) → 예산 초과.
    // step 2) 스폰 수를 줄여 예산 안으로.
    public static readonly IReadOnlyList<string> FastButDrawHeavy = new[]
    {
        // step 1 — 64개 개별 오브젝트. 호출은 빠르지만 드로우콜이 치솟는다.
        """
        FILE: Assets/Scripts/TileField.cs
        ```csharp
        using System.Collections.Generic;
        using UnityEngine;

        public class TileField : MonoBehaviour
        {
            [SerializeField] private int _tilesPerSide = 8;

            private readonly List<GameObject> _spawned = new List<GameObject>(64);

            public int SpawnedCount => _spawned.Count;

            public void Build()
            {
                for (int x = 0; x < _tilesPerSide; x++)
                {
                    for (int z = 0; z < _tilesPerSide; z++)
                    {
                        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tile.transform.SetParent(transform, false);
                        tile.transform.localPosition = new Vector3(x, 0f, z);
                        _spawned.Add(tile);
                    }
                }
            }
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var f = go.AddComponent<TileField>();
        f.Build();
        int n = f.SpawnedCount;
        UnityEngine.Object.DestroyImmediate(go);
        return n == 64 ? "OK" : ("Expected 64 tiles but found " + n + ".");
        ```
        PERF:
        ```json
        { "component": "TileField", "call": "target.SpawnedCount", "iterations": 1000, "maxTotalMs": 25,
          "scene": { "setup": "target.Build()", "maxDrawCallIncrease": 120 } }
        ```
        """,

        // step 2 — 4x4 로 줄여 렌더 예산 안으로(같은 동작, 적은 비용).
        """
        FILE: Assets/Scripts/TileField.cs
        ```csharp
        using System.Collections.Generic;
        using UnityEngine;

        public class TileField : MonoBehaviour
        {
            [SerializeField] private int _tilesPerSide = 4;

            private readonly List<GameObject> _spawned = new List<GameObject>(16);

            public int SpawnedCount => _spawned.Count;

            public void Build()
            {
                for (int x = 0; x < _tilesPerSide; x++)
                {
                    for (int z = 0; z < _tilesPerSide; z++)
                    {
                        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tile.transform.SetParent(transform, false);
                        tile.transform.localPosition = new Vector3(x, 0f, z);
                        _spawned.Add(tile);
                    }
                }
            }
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var f = go.AddComponent<TileField>();
        f.Build();
        int n = f.SpawnedCount;
        UnityEngine.Object.DestroyImmediate(go);
        return n == 16 ? "OK" : ("Expected 16 tiles but found " + n + ".");
        ```
        PERF:
        ```json
        { "component": "TileField", "call": "target.SpawnedCount", "iterations": 1000, "maxTotalMs": 25,
          "scene": { "setup": "target.Build()", "maxDrawCallIncrease": 120 } }
        ```
        """,
    };

    // --demo-perf 스크립트: **동작은 맞지만 느린** 코드를 성능 실측이 잡아낸다.
    // step 1) 결과는 정확하지만 매 호출 List 를 새로 만들고 문자열을 조립한다 → 컴파일·런타임 assert 통과, 성능 초과.
    // step 2) 버퍼 재사용 + 문자열 제거 → 예산 통과.
    // "동작 정상 ≠ 충분히 빠름" — Phase 3 의 정적 추측과 달리 **측정으로** 잡는다는 게 핵심.
    public static readonly IReadOnlyList<string> CorrectButSlowThenFast = new[]
    {
        // step 1 — 정답은 맞지만 핫패스에서 매 호출 할당.
        """
        FILE: Assets/Scripts/ScoreTracker.cs
        ```csharp
        using System.Collections.Generic;
        using UnityEngine;

        public class ScoreTracker : MonoBehaviour
        {
            [SerializeField] private int _window = 16;

            public int Total { get; private set; }

            public void Record(int score)
            {
                var buffer = new List<int>();
                for (int i = 0; i < _window; i++)
                {
                    buffer.Add(score + i);
                }

                int sum = 0;
                foreach (var v in buffer)
                {
                    sum += v;
                }

                var label = "score:" + sum;
                Total = sum + label.Length - label.Length;
            }
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var t = go.AddComponent<ScoreTracker>();
        t.Record(10);
        int total = t.Total;
        UnityEngine.Object.DestroyImmediate(go);
        return total == 280 ? "OK" : ("After Record(10), Total should be 280 but was " + total + ".");
        ```
        PERF:
        ```json
        { "component": "ScoreTracker", "call": "target.Record(10)", "iterations": 50000, "maxTotalMs": 25 }
        ```
        """,

        // step 2 — 버퍼 재사용 + 문자열 제거로 동일 결과를 무할당으로.
        """
        FILE: Assets/Scripts/ScoreTracker.cs
        ```csharp
        using System.Collections.Generic;
        using UnityEngine;

        public class ScoreTracker : MonoBehaviour
        {
            [SerializeField] private int _window = 16;

            private readonly List<int> _buffer = new List<int>(64);

            public int Total { get; private set; }

            public void Record(int score)
            {
                _buffer.Clear();
                for (int i = 0; i < _window; i++)
                {
                    _buffer.Add(score + i);
                }

                int sum = 0;
                for (int i = 0; i < _buffer.Count; i++)
                {
                    sum += _buffer[i];
                }

                Total = sum;
            }
        }
        ```
        ASSERT:
        ```csharp
        var go = new UnityEngine.GameObject();
        var t = go.AddComponent<ScoreTracker>();
        t.Record(10);
        int total = t.Total;
        UnityEngine.Object.DestroyImmediate(go);
        return total == 280 ? "OK" : ("After Record(10), Total should be 280 but was " + total + ".");
        ```
        PERF:
        ```json
        { "component": "ScoreTracker", "call": "target.Record(10)", "iterations": 50000, "maxTotalMs": 25 }
        ```
        """,
    };
}
