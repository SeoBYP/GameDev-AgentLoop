namespace Orchestrator.Targets;

/// <summary>
/// 대상 Unity 프로젝트를 루프가 검증할 수 있는 상태로 만든다(`agentloop --init`).
///
/// 왜 필요한가: Unity 테스트 asmdef 은 **`Assembly-CSharp` 를 참조할 수 없다.**
/// 그래서 asmdef 이 하나도 없는 보통의 프로젝트에서는 PlayMode 테스트가 원리적으로 컴파일되지 않는다.
/// 런타임 asmdef + 테스트 asmdef 한 쌍을 만들어 줘야 비로소 "테스트로 검증"이 가능해진다.
/// (이 프로젝트가 Phase 6 에서 직접 부딪힌 함정이다 — WORKLOG 참조.)
///
/// 이미 있는 파일은 절대 덮어쓰지 않는다.
/// </summary>
public static class ProjectInit
{
    public static int Run(string projectRoot, ProjectLayout layout)
    {
        Console.WriteLine($"Project: {projectRoot}");
        Console.WriteLine($"Current layout: {layout.Describe()}");

        if (layout.TestsReady)
        {
            Console.WriteLine("\n✅ A test assembly already exists — nothing to do.");
            return 0;
        }

        var runtimeName = layout.RuntimeAssembly ?? "Game.Runtime";
        var testName = layout.TestAssembly ?? "Game.Tests";
        var created = new List<string>();

        // Input System 이 설치돼 있으면 참조를 넣어 준다. 없으면 가상 입력으로 조작 시나리오를
        // 검증할 수 없고, 그건 "테스트가 안 됨"이 아니라 "인프라가 없음"이라 조용히 실패한다.
        var hasInput = HasPackage(projectRoot, "com.unity.inputsystem");
        if (hasInput)
            Console.WriteLine("  · Input System detected — adding references for virtual input tests");

        // 런타임 asmdef — 이미 있으면(탐지됨) 건드리지 않는다.
        if (layout.RuntimeAssembly is null)
        {
            var dir = Path.Combine(projectRoot, layout.ScriptDir.Replace('/', Path.DirectorySeparatorChar));
            if (TryWrite(Path.Combine(dir, runtimeName + ".asmdef"), RuntimeAsmdef(runtimeName, hasInput), out var p))
                created.Add(p);
        }

        // 테스트 asmdef — 런타임을 참조하고, 테스트 러너/NUnit 을 끌어온다.
        {
            var dir = Path.Combine(projectRoot, layout.TestDir.Replace('/', Path.DirectorySeparatorChar));
            if (TryWrite(Path.Combine(dir, testName + ".asmdef"), TestAsmdef(testName, runtimeName, hasInput), out var p))
                created.Add(p);
        }

        // 다음 실행이 탐지에 기대지 않도록 배치를 명시로 굳혀 둔다.
        var cfg = Path.Combine(projectRoot, ".agentloop", "layout.json");
        if (TryWrite(cfg, LayoutJson(layout.ScriptDir, layout.TestDir, runtimeName, testName), out var cp))
            created.Add(cp);

        if (created.Count == 0)
        {
            Console.WriteLine("\nNo changes — the target files already exist.");
            return 0;
        }

        Console.WriteLine("\nCreated:");
        foreach (var f in created)
            Console.WriteLine($"  + {Path.GetRelativePath(projectRoot, f).Replace('\\', '/')}");

        Console.WriteLine("""

            Next:
              1) Open (or focus) the project in the Unity Editor and let it finish compiling.
                 New .asmdef files move existing scripts into a different assembly, so fix any
                 references that break.
              2) Confirm a reachable server with: unity pipeline list
              3) Run agentloop again — it will now verify through the Test Runner.
            """);
        return 0;
    }

    private static bool TryWrite(string path, string content, out string written)
    {
        written = path;
        if (File.Exists(path))
        {
            Console.WriteLine($"  · skipped (already exists): {path}");
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return true;
    }

    /// <summary>manifest.json 에 해당 패키지가 있는가(간단 문자열 검사로 충분하다).</summary>
    private static bool HasPackage(string projectRoot, string package)
    {
        try
        {
            var manifest = Path.Combine(projectRoot, "Packages", "manifest.json");
            return File.Exists(manifest) &&
                   File.ReadAllText(manifest).Contains(package, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string RuntimeAsmdef(string name, bool hasInput) => $$"""
        {
            "name": "{{name}}",
            "rootNamespace": "",
            "references": [{{(hasInput ? "\n        \"Unity.InputSystem\"\n    " : "")}}],
            "includePlatforms": [],
            "excludePlatforms": [],
            "allowUnsafeCode": false,
            "overrideReferences": false,
            "precompiledReferences": [],
            "autoReferenced": true,
            "defineConstraints": [],
            "versionDefines": [],
            "noEngineReferences": false
        }
        """;

    // 테스트 asmdef 의 필수 요건 3가지 — 하나라도 빠지면 테스트가 안 잡힌다.
    //   references: 런타임 + 테스트 러너(에디터/런타임 양쪽)
    //   precompiledReferences: nunit.framework.dll
    //   defineConstraints: UNITY_INCLUDE_TESTS  (빌드에 딸려 나가지 않게)
    private static string TestAsmdef(string name, string runtime, bool hasInput) => $$"""
        {
            "name": "{{name}}",
            "rootNamespace": "",
            "references": [
                "{{runtime}}",
                "UnityEngine.TestRunner",
                "UnityEditor.TestRunner"{{(hasInput ? ",\n        \"Unity.InputSystem\",\n        \"Unity.InputSystem.TestFramework\"" : "")}}
            ],
            "includePlatforms": [],
            "excludePlatforms": [],
            "allowUnsafeCode": false,
            "overrideReferences": true,
            "precompiledReferences": [
                "nunit.framework.dll"
            ],
            "autoReferenced": false,
            "defineConstraints": [
                "UNITY_INCLUDE_TESTS"
            ],
            "versionDefines": [],
            "noEngineReferences": false
        }
        """;

    private static string LayoutJson(string scriptDir, string testDir, string runtime, string test) => $$"""
        {
            "scriptDir": "{{scriptDir}}",
            "testDir": "{{testDir}}",
            "runtimeAssembly": "{{runtime}}",
            "testAssembly": "{{test}}"
        }
        """;
}
