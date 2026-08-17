using System.Text.RegularExpressions;

namespace Orchestrator.Targets;

/// <summary>
/// 두 <see cref="ProjectSurface"/> 를 비교해 **사라진 것**을 찾는다. ARCHITECTURE §8.3 삭제 게이트.
///
/// 왜 필요한가 — §3.2 소유권이 못 덮는 구멍이 하나 남는다:
/// 노드가 **정당하게 소유한** 파일 안에서 남이 쓰는 멤버를 지우는 경우. 경로 검사로는 안 잡힌다.
/// 그리고 지운 뒤 그 멤버를 쓰던 테스트까지 같이 지우면 스위트가 초록이 되므로 검증으로도 안 잡힌다
/// (실측: 굶은 노드가 그렇게 해서 루프가 "성공"을 보고했다 — §8.3).
///
/// **멤버 키를 전체 시그니처가 아니라 이름 + 인자 수로 잡는 게 핵심이다.**
/// 전체 시그니처로 잡으면 파라미터 이름만 바꿔도(`int amount` → `int damage`) "삭제"로 보여
/// 정당한 작업을 막는다. 이름+arity 는 삭제와 arity 변경은 잡고 개명·서식 변화는 흘려보낸다.
/// (인자 수는 `&lt;&gt;`·중첩 괄호 안의 쉼표를 세지 않는다 — `Dictionary&lt;int,string&gt; m` 이 2개로 세이면
/// 그것도 오탐이 된다.)
/// </summary>
public static class SurfaceDiff
{
    /// <summary>사라진 표면 항목들. 비면 회귀 없음.</summary>
    public static IReadOnlyList<string> Missing(ProjectSurface before, ProjectSurface after)
    {
        var now = new HashSet<string>(Keys(after).Select(k => k.Key), StringComparer.Ordinal);
        return Keys(before)
            .Where(k => !now.Contains(k.Key))
            .Select(k => k.Display)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<(string Key, string Display)> Keys(ProjectSurface surface)
    {
        foreach (var t in surface.Types)
        {
            var owner = t.Namespace.Length == 0 ? t.Name : $"{t.Namespace}.{t.Name}";
            yield return ($"T:{owner}", owner);

            foreach (var raw in t.Members)
            {
                // enum 은 추출기가 값들을 한 줄로 합쳐 준다 — 값 단위로 쪼개야 하나가 사라진 걸 잡는다.
                var isEnum = t.Declaration.StartsWith("enum ", StringComparison.Ordinal) ||
                             t.Declaration.Contains(" enum ", StringComparison.Ordinal);
                if (isEnum)
                {
                    foreach (var value in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        yield return ($"M:{owner}.{value}/0", $"{owner}.{value}");
                    continue;
                }

                var (name, arity) = NameAndArity(raw);
                if (name.Length == 0)
                    continue;
                yield return ($"M:{owner}.{name}/{arity}", $"{owner}.{name}({arity} arg)");
            }
        }
    }

    /// <summary>정규화된 선언에서 멤버 이름과 인자 수를 뽑는다.</summary>
    internal static (string Name, int Arity) NameAndArity(string declaration)
    {
        var open = IndexOfTopLevel(declaration, '(');
        if (open < 0)
        {
            // 필드·프로퍼티·이벤트:  "int Width { get; }" · "event Action<Actor> Died"
            var head = declaration.Split('{')[0].TrimEnd().TrimEnd(';');
            var last = Regex.Match(head, @"(\w+)\s*$");
            return (last.Success ? last.Groups[1].Value : string.Empty, 0);
        }

        var before = declaration[..open].TrimEnd();
        var nameMatch = Regex.Match(before, @"(\w+)\s*$");
        var close = MatchPair(declaration, open);
        var inner = close > open ? declaration[(open + 1)..close] : string.Empty;

        return (nameMatch.Success ? nameMatch.Groups[1].Value : string.Empty, CountArgs(inner));
    }

    /// <summary>최상위 쉼표만 센다 — 제네릭 인자와 중첩 괄호 안의 쉼표는 인자가 아니다.</summary>
    private static int CountArgs(string parameters)
    {
        if (parameters.Trim().Length == 0)
            return 0;

        var depth = 0;
        var count = 1;
        foreach (var c in parameters)
        {
            switch (c)
            {
                case '<' or '(' or '[': depth++; break;
                case '>' or ')' or ']': depth--; break;
                case ',' when depth == 0: count++; break;
            }
        }
        return count;
    }

    private static int IndexOfTopLevel(string s, char target)
    {
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is '<' or '[') depth++;
            else if (s[i] is '>' or ']') depth--;
            else if (s[i] == target && depth == 0) return i;
        }
        return -1;
    }

    private static int MatchPair(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }
}
