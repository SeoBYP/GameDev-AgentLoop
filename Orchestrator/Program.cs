using Orchestrator.Backends;
using Orchestrator.Contracts;
using Orchestrator.Loop;
using Orchestrator.Targets;

// ─────────────────────────────────────────────────────────────────────────────
// 오케스트레이터 진입점 — 루프 5단계를 조립·실행한다.
//
//   orchestrator "목표 문장"            # ApiBackend(실제 두뇌, ANTHROPIC_API_KEY 필요)
//   orchestrator --demo                 # ScriptedBackend(키 없이 자가수리 루프 증명)
//   orchestrator --demo --max-steps 4
//   orchestrator "..." --model claude-sonnet-5 --project C:\path\to\UnityProject
// ─────────────────────────────────────────────────────────────────────────────

var opts = ParseArgs(args);

// 1) Unity 프로젝트 루트 해석 (인자 → 환경변수 → cwd에서 위로 탐색)
var projectPath = opts.ProjectPath
    ?? Environment.GetEnvironmentVariable("UNITY_PROJECT_PATH")
    ?? FindUnityProjectRoot(Directory.GetCurrentDirectory());

if (projectPath is null || !Directory.Exists(Path.Combine(projectPath, "Assets")))
{
    Console.Error.WriteLine("Unity 프로젝트 루트를 찾지 못했습니다(Assets/ 없음). --project <경로> 로 지정하세요.");
    return 2;
}

var unityExe = UnityEditorTarget.ResolveUnityExe();
var target = new UnityEditorTarget(unityExe, projectPath, label: $"unity:{ReadEditorVersion(projectPath)}");

// 2) 백엔드 선택 (두뇌 = pluggable)
IAgentBackend backend;
if (opts.Demo)
{
    backend = new ScriptedBackend(DemoScript.BrokenThenFixedHealth);
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
            백엔드(두뇌)를 고르세요:
              • 이 AI(Claude Code)로:    orchestrator --claude "목표"   ← claude CLI 로그인 필요
              • Codex 로:                orchestrator --codex  "목표"   ← codex  CLI 로그인 필요
              • Anthropic API 키로:      환경변수 ANTHROPIC_API_KEY 설정 후  orchestrator "목표"
              • 키 없이 루프 배관 증명:  orchestrator --demo
            (키는 절대 레포에 커밋하지 마세요 — CLAUDE.md)
            """);
        return 2;
    }
    backend = new ApiBackend(apiKey, opts.Model ?? "claude-opus-5");
}

// 3) 루프 조립·실행
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// 전제 확인: pipeline 서버 연결(에디터가 열려 있어야 함). 없으면 AI 호출 전에 빠르게 실패.
if (!await target.IsConnectedAsync(cts.Token))
{
    Console.Error.WriteLine(
        "Unity pipeline 서버에 연결할 수 없습니다.\n" +
        "  → GameDev-AgentLoop 를 에디터에서 열고, `unity pipeline list` 의 '서버 연결 가능' 이 true 인지 확인하세요.");
    return 2;
}

var loop = new AgentLoop(backend, target, new LoopOptions { MaxSteps = opts.MaxSteps });

try
{
    var result = await loop.RunAsync(opts.Goal, cts.Token);
    Console.WriteLine();
    Console.WriteLine(result.Success ? $"✅ 성공 — {result.Summary}" : $"❌ 실패 — {result.Summary}");
    return result.Success ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("사용자에 의해 취소됨.");
    return 130;
}
catch (Exception ex)
{
    // 백엔드/타깃 실패(예: claude 로그인 만료, unity 서버 미연결)를 깔끔히 보고.
    Console.Error.WriteLine($"\n❌ 오류 — {ex.Message}");
    return 1;
}
finally
{
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
            case "--claude": o.Claude = true; break;
            case "--codex": o.Codex = true; break;
            case "--max-steps" when i + 1 < args.Length: o.MaxSteps = int.Parse(args[++i]); break;
            case "--project" when i + 1 < args.Length: o.ProjectPath = args[++i]; break;
            case "--model" when i + 1 < args.Length: o.Model = args[++i]; break;
            default: positional.Add(args[i]); break;
        }
    }
    if (positional.Count > 0)
        o.Goal = string.Join(' ', positional);
    return o;
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

// ── 옵션/데모 스크립트 ────────────────────────────────────────────────────────

sealed class Options
{
    public string Goal { get; set; } =
        "간단한 Health(HP) 컴포넌트를 만들어줘. 현재/최대 체력, TakeDamage(int), Heal(int)을 갖고, 체력은 0 미만·최대치 초과가 되지 않아야 한다.";
    public int MaxSteps { get; set; } = 6;
    public bool Demo { get; set; }
    public bool Claude { get; set; }
    public bool Codex { get; set; }
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
        // step 1 — 일부러 깨뜨림: `Current = 100` 뒤 세미콜론 누락 → CS1002
        """
        FILE: Assets/Scripts/Health.cs
        ```csharp
        using UnityEngine;

        public class Health : MonoBehaviour
        {
            public int Max = 100;
            public int Current = 100

            public void TakeDamage(int amount) => Current = Mathf.Max(0, Current - amount);
            public void Heal(int amount)       => Current = Mathf.Min(Max, Current + amount);
        }
        ```
        """,

        // step 2 — 세미콜론 추가로 수정
        """
        FILE: Assets/Scripts/Health.cs
        ```csharp
        using UnityEngine;

        public class Health : MonoBehaviour
        {
            public int Max = 100;
            public int Current = 100;

            public void TakeDamage(int amount) => Current = Mathf.Max(0, Current - amount);
            public void Heal(int amount)       => Current = Mathf.Min(Max, Current + amount);
        }
        ```
        """,
    };
}
