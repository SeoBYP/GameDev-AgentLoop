using Orchestrator.Backends;
using Orchestrator.Contracts;
using Orchestrator.Loop;
using Orchestrator.Skills;
using Orchestrator.Targets;
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

// 1) Unity 프로젝트 루트 해석 (인자 → 환경변수 → cwd에서 위로 탐색)
var projectPath = opts.ProjectPath
    ?? Environment.GetEnvironmentVariable("UNITY_PROJECT_PATH")
    ?? FindUnityProjectRoot(Directory.GetCurrentDirectory());

if (projectPath is null || !Directory.Exists(Path.Combine(projectPath, "Assets")))
{
    Console.Error.WriteLine("Unity 프로젝트 루트를 찾지 못했습니다(Assets/ 없음). --project <경로> 로 지정하세요.");
    return 2;
}

// 1-a) .env 로드 — 자격 증명을 OS 전역이 아닌 레포 루트 파일로 관리한다(.gitignore 로 보호됨).
//      여기서 올린 값은 자식 프로세스(`ugs`/`claude`/`codex`)에도 그대로 상속된다.
var envFile = opts.EnvFile ?? Path.Combine(projectPath, ".env");
var loadedKeys = DotEnv.Load(envFile);
if (loadedKeys.Count > 0)
    Console.WriteLine($".env 적용: {string.Join(", ", loadedKeys)}");   // 키 이름만 — 값은 절대 출력하지 않는다

// 1-b) 도메인 스킬 로드 (Phase 3) — 포터블 마크다운이라 모든 백엔드에 동일하게 적용된다.
var skillsDir = opts.SkillsDir ?? Path.Combine(projectPath, "Skills");
var library = opts.SkillsOff ? SkillLibrary.Empty : SkillLibrary.Load(skillsDir);
var selectedSkills = library.Select(opts.Goal, opts.Target);

if (opts.ListSkills)
{
    Console.WriteLine($"스킬 디렉터리: {skillsDir}");
    if (library.All.Count == 0)
    {
        Console.WriteLine("  (스킬 없음)");
    }
    foreach (var s in library.All)
    {
        var mark = selectedSkills.Contains(s) ? "✓" : " ";
        Console.WriteLine($"  [{mark}] {s.Name} — {s.Title}  (검사 {s.Checks.Count}개)");
        foreach (var c in s.Checks)
            Console.WriteLine($"        · {c.Id}  [{string.Join(", ", c.Scopes)}]");
    }
    return 0;
}

// 1-c) 손(타깃) 선택 — Unity 에디터(클라) vs UGS Cloud Code(백엔드)
IExecTarget target;
if (opts.Target.Equals("ugs", StringComparison.OrdinalIgnoreCase))
{
    var deployDir = opts.CloudCodeDir ?? Path.Combine(projectPath, "CloudCode");
    // 프로젝트/환경은 인자 → 환경변수(.env 포함) 순으로 해석한다.
    var ugsProjectId = opts.UgsProjectId ?? Environment.GetEnvironmentVariable("UGS_CLI_PROJECT_ID");
    var ugsEnv = opts.UgsEnvironment ?? Environment.GetEnvironmentVariable("UGS_CLI_ENVIRONMENT_NAME");
    target = new UgsTarget(projectPath, deployDir, ugsProjectId, ugsEnv);
}
else
{
    var unityExe = UnityEditorTarget.ResolveUnityExe();
    target = new UnityEditorTarget(unityExe, projectPath, label: $"unity:{ReadEditorVersion(projectPath)}");
}

// 1-d) 조립된 시스템 프롬프트만 보고 끝내기(디버깅/설명용) — 인증 없이도 타깃별 규격을 확인할 수 있다.
if (opts.PrintPrompt)
{
    Console.WriteLine($"# 타깃: {target.Name}   런타임 검증 지원: {target.Supports(VerifyKind.PlayModeAssert)}");
    Console.WriteLine($"# 스킬: {(selectedSkills.Count == 0 ? "없음" : string.Join(", ", selectedSkills.Select(s => s.Name)))}");
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
              • 키 없이 루프 배관 증명:  orchestrator --demo        (컴파일 자가수리)
              • 키 없이 런타임 검증 증명: orchestrator --demo-play   (컴파일은 통과하나 동작이 틀린 코드)
            (키는 절대 레포에 커밋하지 마세요 — CLAUDE.md)
            """);
        return 2;
    }
    backend = new ApiBackend(apiKey, opts.Model ?? "claude-opus-5");
}

// 3) 루프 조립·실행
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// 전제 확인: 손이 준비됐는가(에디터 연결 / UGS 인증). 안 되어 있으면 AI 호출 전에 빠르게 실패.
if (!await target.IsConnectedAsync(cts.Token))
{
    Console.Error.WriteLine(target.ConnectionHint);
    return 2;
}

var loop = new AgentLoop(
    backend,
    target,
    new LoopOptions
    {
        MaxSteps = opts.MaxSteps,
        Assert = opts.Assert,   // 사람이 준 런타임 검증 기준(있으면 AI 의 ASSERT 블록보다 우선)
    },
    selectedSkills);

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
            case "--demo-play": o.DemoPlay = true; break;
            case "--demo-skills": o.DemoSkills = true; break;
            case "--claude": o.Claude = true; break;
            case "--codex": o.Codex = true; break;
            case "--assert" when i + 1 < args.Length: o.Assert = args[++i]; break;
            case "--skills" when i + 1 < args.Length: o.SkillsOff = args[++i].Equals("off", StringComparison.OrdinalIgnoreCase); break;
            case "--skills-dir" when i + 1 < args.Length: o.SkillsDir = args[++i]; break;
            case "--list-skills": o.ListSkills = true; break;
            case "--print-prompt": o.PrintPrompt = true; break;
            case "--env-file" when i + 1 < args.Length: o.EnvFile = args[++i]; break;
            case "--target" when i + 1 < args.Length: o.Target = args[++i]; break;
            case "--ugs-project-id" when i + 1 < args.Length: o.UgsProjectId = args[++i]; break;
            case "--ugs-env" when i + 1 < args.Length: o.UgsEnvironment = args[++i]; break;
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
            o.Goal = "스태미나 컴포넌트를 만들어줘. Use(int)/Recover(int) 를 갖고, 값은 0~Max 를 벗어나지 않는다.";
        else if (o.DemoSkills)
            o.Goal = "대상을 따라가는 Follower 컴포넌트를 만들어줘. 속도와 대상은 인스펙터에서 설정한다.";
    }
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
    public bool DemoPlay { get; set; }
    public bool DemoSkills { get; set; }
    public bool Claude { get; set; }
    public bool Codex { get; set; }
    public string? Assert { get; set; }
    public bool SkillsOff { get; set; }
    public bool ListSkills { get; set; }
    public bool PrintPrompt { get; set; }
    public string? EnvFile { get; set; }
    public string? SkillsDir { get; set; }
    public string Target { get; set; } = "unity";   // unity | ugs
    public string? UgsProjectId { get; set; }
    public string? UgsEnvironment { get; set; }
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
        if (afterUse != 0) return "Use(500) 후 Current 는 0 이어야 하는데 " + afterUse + " 였습니다.";
        if (afterRecover != 100) return "Recover(9999) 후 Current 는 Max(100) 이어야 하는데 " + afterRecover + " 였습니다.";
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
        if (afterUse != 0) return "Use(500) 후 Current 는 0 이어야 하는데 " + afterUse + " 였습니다.";
        if (afterRecover != 100) return "Recover(9999) 후 Current 는 Max(100) 이어야 하는데 " + afterRecover + " 였습니다.";
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
        return has ? "target 이 없는데 HasTarget 이 true 입니다." : "OK";
        ```
        """,
    };
}
