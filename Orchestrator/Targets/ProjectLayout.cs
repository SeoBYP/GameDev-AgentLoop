using System.Text.Json;

namespace Orchestrator.Targets;

/// <summary>
/// 대상 Unity 프로젝트의 배치 — 스크립트를 어디에 쓰고, 테스트가 어느 어셈블리에 속하는가.
///
/// 왜 필요한가: 생성 지침(<see cref="UnityEditorTarget.GenerationBrief"/>)이 경로와 어셈블리 이름을
/// 모델에게 알려 줘야 하는데, 이건 **프로젝트마다 다르다**. 여기에 하드코딩하면
/// 이 레포에서만 도는 도구가 된다.
///
/// 특히 테스트 어셈블리가 중요하다 — Unity 테스트 asmdef 은 `Assembly-CSharp` 를 참조할 수 없다.
/// 그래서 asmdef 이 없는 프로젝트에서는 **테스트가 원리적으로 컴파일되지 않는다**(`--init` 이 만들어 준다).
///
/// 해석 순서: `.agentloop/layout.json` → asmdef 스캔 → 관례적 기본값.
/// </summary>
public sealed record ProjectLayout(
    string  ScriptDir,          // 예: "Assets/Scripts"
    string  TestDir,            // 예: "Assets/Tests/PlayMode"
    string? RuntimeAssembly,    // asmdef 이름. null = Assembly-CSharp(기본 어셈블리)
    string? TestAssembly,       // 테스트 asmdef 이름. null = 없음 → 테스트 검증 불가
    string  Source)             // 어떻게 정해졌는지(로그·진단용)
{
    /// <summary>테스트 러너로 검증할 수 있는 상태인가.</summary>
    public bool TestsReady => TestAssembly is not null;

    public const string ConfigRelPath = ".agentloop/layout.json";

    private static readonly ProjectLayout Conventional =
        new("Assets/Scripts", "Assets/Tests/PlayMode", null, null, "관례 기본값");

    /// <summary>설정 → 탐지 → 기본값 순으로 해석한다.</summary>
    public static ProjectLayout Resolve(string projectRoot)
        => LoadConfig(projectRoot) ?? Detect(projectRoot) ?? Conventional;

    // ── 1) 명시 설정이 최우선 ────────────────────────────────────────────────
    private static ProjectLayout? LoadConfig(string projectRoot)
    {
        var path = Path.Combine(projectRoot, ".agentloop", "layout.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            return new ProjectLayout(
                Str(r, "scriptDir")  ?? Conventional.ScriptDir,
                Str(r, "testDir")    ?? Conventional.TestDir,
                Str(r, "runtimeAssembly"),
                Str(r, "testAssembly"),
                ConfigRelPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"경고: {ConfigRelPath} 를 읽지 못했습니다 — {ex.Message}. 자동 탐지로 넘어갑니다.");
            return null;
        }

        static string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
               ? v.GetString() : null;
    }

    // ── 2) asmdef 스캔 ──────────────────────────────────────────────────────
    private static ProjectLayout? Detect(string projectRoot)
    {
        var assetsDir = Path.Combine(projectRoot, "Assets");
        if (!Directory.Exists(assetsDir))
            return null;

        List<AsmdefInfo> defs;
        try
        {
            defs = Directory.EnumerateFiles(assetsDir, "*.asmdef", SearchOption.AllDirectories)
                            .Select(p => AsmdefInfo.Read(p, projectRoot))
                            .Where(d => d is not null)
                            .Select(d => d!)
                            .ToList();
        }
        catch { return null; }

        if (defs.Count == 0)
            return null;

        var test = defs.FirstOrDefault(d => d.IsTest);
        // 테스트가 참조하는 로컬 어셈블리 = 런타임 후보. 없으면 Editor/테스트가 아닌 첫 asmdef.
        var runtime =
            (test is not null
                ? defs.FirstOrDefault(d => !d.IsTest && !d.IsEditorOnly && test.References.Contains(d.Name))
                : null)
            ?? defs.FirstOrDefault(d => !d.IsTest && !d.IsEditorOnly);

        if (test is null && runtime is null)
            return null;

        return new ProjectLayout(
            runtime?.Dir ?? Conventional.ScriptDir,
            test?.Dir    ?? Conventional.TestDir,
            runtime?.Name,
            test?.Name,
            "asmdef 탐지");
    }

    private sealed record AsmdefInfo(string Name, string Dir, IReadOnlyList<string> References, bool IsTest, bool IsEditorOnly)
    {
        public static AsmdefInfo? Read(string fullPath, string projectRoot)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
                var r = doc.RootElement;

                var name = r.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                var refs = Array(r, "references");
                var constraints = Array(r, "defineConstraints");
                var platforms = Array(r, "includePlatforms");

                // 테스트 asmdef 판별: 테스트 러너 참조 또는 UNITY_INCLUDE_TESTS 제약.
                var isTest = refs.Any(x => x.Contains("TestRunner", StringComparison.OrdinalIgnoreCase))
                          || constraints.Contains("UNITY_INCLUDE_TESTS");
                var isEditorOnly = !isTest && platforms.Count == 1 && platforms[0] == "Editor";

                var dir = Path.GetRelativePath(projectRoot, Path.GetDirectoryName(fullPath)!)
                              .Replace('\\', '/');

                return new AsmdefInfo(name!, dir, refs, isTest, isEditorOnly);
            }
            catch { return null; }

            static List<string> Array(JsonElement e, string name)
                => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                   ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                      .Select(x => x.GetString()!).ToList()
                   : new List<string>();
        }
    }

    public string Describe()
        => $"스크립트 {ScriptDir}/ · 테스트 {TestDir}/ " +
           $"· 런타임 어셈블리 {RuntimeAssembly ?? "Assembly-CSharp"} " +
           $"· 테스트 어셈블리 {TestAssembly ?? "(없음)"}  [{Source}]";
}
