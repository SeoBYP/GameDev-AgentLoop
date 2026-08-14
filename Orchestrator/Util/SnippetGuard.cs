using System.Text.RegularExpressions;

namespace Orchestrator.Util;

/// <summary>
/// AI 가 만든 C# 스니펫을 `unity command eval` 로 실행하기 **전에** 위험한 호출을 걸러낸다.
///
/// 왜 필요한가: 검증용 ASSERT/PERF 스니펫은 에디터 프로세스 안에서 **제한 없이** 실행된다.
/// 파일 삭제·프로세스 실행·네트워크 호출도 문법적으로 가능하다. 의도적 공격이 아니더라도
/// 모델이 "정리한다"며 디스크를 건드리는 코드를 낼 수 있다.
///
/// **한계를 분명히 한다 — 이건 샌드박스가 아니라 완화책이다.**
/// 정적 문자열 검사이므로 리플렉션이나 문자열 조립으로 우회할 수 있다.
/// 진짜 격리가 필요하면 실행 자체를 별도 프로세스/권한으로 분리해야 한다.
/// 그럼에도 두는 이유: 흔한 사고를 값싸게 막고, **무엇이 금지인지 모델에게 알려주기** 위해서다.
/// </summary>
public static partial class SnippetGuard
{
    private sealed record Rule(Regex Pattern, string Why);

    private static readonly Rule[] Rules =
    {
        new(FileMutationRegex(),  "파일 시스템 변경(삭제·쓰기·이동)"),
        new(DirectoryRegex(),     "디렉터리 조작"),
        new(ProcessRegex(),       "외부 프로세스 실행"),
        new(NetworkRegex(),       "네트워크 접근"),
        new(RegistryRegex(),      "레지스트리 접근"),
        new(AssetMutationRegex(), "에셋 삭제·이동(프로젝트 손상 위험)"),
        new(QuitRegex(),          "에디터/애플리케이션 종료"),
    };

    // 파일 변경: 읽기(ReadAllText 등)는 허용, 변경 계열만 막는다.
    [GeneratedRegex(@"\bFile\s*\.\s*(Delete|Move|Copy|Create|WriteAll|AppendAll|Replace|Encrypt|Decrypt)", RegexOptions.IgnoreCase)]
    private static partial Regex FileMutationRegex();

    [GeneratedRegex(@"\bDirectory\s*\.\s*(Delete|Move|CreateDirectory)", RegexOptions.IgnoreCase)]
    private static partial Regex DirectoryRegex();

    [GeneratedRegex(@"\b(System\s*\.\s*Diagnostics\s*\.\s*Process|Process\s*\.\s*Start)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProcessRegex();

    [GeneratedRegex(@"\b(System\s*\.\s*Net|UnityWebRequest|HttpClient|WebRequest|Socket)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkRegex();

    [GeneratedRegex(@"\b(Microsoft\s*\.\s*Win32\s*\.\s*Registry|RegistryKey)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RegistryRegex();

    // AssetDatabase 는 읽기는 흔하지만 삭제/이동은 프로젝트를 망가뜨린다.
    [GeneratedRegex(@"\bAssetDatabase\s*\.\s*(Delete|Move|Rename)", RegexOptions.IgnoreCase)]
    private static partial Regex AssetMutationRegex();

    [GeneratedRegex(@"\b(Application\s*\.\s*Quit|EditorApplication\s*\.\s*Exit|Environment\s*\.\s*Exit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuitRegex();

    /// <summary>위반 사유 목록(비어 있으면 실행해도 되는 스니펫).</summary>
    public static IReadOnlyList<string> Inspect(string snippet)
    {
        var hits = new List<string>();
        foreach (var rule in Rules)
        {
            var m = rule.Pattern.Match(snippet);
            if (m.Success)
                hits.Add($"{rule.Why} — `{m.Value.Trim()}`");
        }
        return hits;
    }

    /// <summary>모델에게 되돌릴 거부 메시지.</summary>
    public static string BuildRejection(IReadOnlyList<string> violations)
    {
        var list = string.Join("\n", violations.Select(v => "  - " + v));
        return $"""
            검증 스니펫이 **허용되지 않는 동작**을 포함해 실행하지 않았습니다:

            {list}

            검증 코드는 컴포넌트의 동작만 확인해야 합니다.
            파일·프로세스·네트워크·레지스트리·에셋 삭제에 접근하지 말고,
            `new UnityEngine.GameObject()` + `AddComponent<T>()` 로 만든 객체만 조작한 뒤
            `UnityEngine.Object.DestroyImmediate(go)` 로 정리하세요.
            """;
    }
}
