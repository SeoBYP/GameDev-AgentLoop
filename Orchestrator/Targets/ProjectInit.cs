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
        Console.WriteLine($"프로젝트: {projectRoot}");
        Console.WriteLine($"현재 배치: {layout.Describe()}");

        if (layout.TestsReady)
        {
            Console.WriteLine("\n✅ 이미 테스트 어셈블리가 있습니다 — 할 일이 없습니다.");
            return 0;
        }

        var runtimeName = layout.RuntimeAssembly ?? "Game.Runtime";
        var testName = layout.TestAssembly ?? "Game.Tests";
        var created = new List<string>();

        // 런타임 asmdef — 이미 있으면(탐지됨) 건드리지 않는다.
        if (layout.RuntimeAssembly is null)
        {
            var dir = Path.Combine(projectRoot, layout.ScriptDir.Replace('/', Path.DirectorySeparatorChar));
            if (TryWrite(Path.Combine(dir, runtimeName + ".asmdef"), RuntimeAsmdef(runtimeName), out var p))
                created.Add(p);
        }

        // 테스트 asmdef — 런타임을 참조하고, 테스트 러너/NUnit 을 끌어온다.
        {
            var dir = Path.Combine(projectRoot, layout.TestDir.Replace('/', Path.DirectorySeparatorChar));
            if (TryWrite(Path.Combine(dir, testName + ".asmdef"), TestAsmdef(testName, runtimeName), out var p))
                created.Add(p);
        }

        // 다음 실행이 탐지에 기대지 않도록 배치를 명시로 굳혀 둔다.
        var cfg = Path.Combine(projectRoot, ".agentloop", "layout.json");
        if (TryWrite(cfg, LayoutJson(layout.ScriptDir, layout.TestDir, runtimeName, testName), out var cp))
            created.Add(cp);

        if (created.Count == 0)
        {
            Console.WriteLine("\n변경 없음 — 대상 파일이 이미 존재합니다.");
            return 0;
        }

        Console.WriteLine("\n생성:");
        foreach (var f in created)
            Console.WriteLine($"  + {Path.GetRelativePath(projectRoot, f).Replace('\\', '/')}");

        Console.WriteLine("""

            다음:
              1) Unity 에디터에서 프로젝트를 열어(또는 포커스를 줘서) 컴파일이 끝나기를 기다리세요.
                 새 asmdef 은 기존 스크립트를 다른 어셈블리로 옮기므로, 참조가 깨지면 그 부분을 고쳐야 합니다.
              2) `unity pipeline list` 로 '서버 연결 가능: true' 를 확인하세요.
              3) 다시 실행하면 테스트 러너로 검증합니다.
            """);
        return 0;
    }

    private static bool TryWrite(string path, string content, out string written)
    {
        written = path;
        if (File.Exists(path))
        {
            Console.WriteLine($"  · 건너뜀(이미 있음): {path}");
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return true;
    }

    private static string RuntimeAsmdef(string name) => $$"""
        {
            "name": "{{name}}",
            "rootNamespace": "",
            "references": [],
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
    private static string TestAsmdef(string name, string runtime) => $$"""
        {
            "name": "{{name}}",
            "rootNamespace": "",
            "references": [
                "{{runtime}}",
                "UnityEngine.TestRunner",
                "UnityEditor.TestRunner"
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
