using System.Text.RegularExpressions;

namespace Orchestrator.Skills;

/// <summary>
/// 스킬 정적 검사를 위한 최소한의 C# 소스 훑기.
///
/// 파서를 붙이지 않는 이유: 검사 대상이 "Update 안에서 GetComponent 를 부르나" 수준의
/// 국소 규칙이라 중괄호 매칭만으로 충분하고, 의존성 0 원칙을 유지할 수 있다.
/// (완벽한 구문 분석이 필요해지면 그때 Roslyn 을 붙이면 된다.)
/// </summary>
public static class CSharpSource
{
    /// <summary>메서드 몸통을 뽑아온다(블록 또는 식 본문). 못 찾으면 null.</summary>
    public static string? ExtractMethodBody(string src, string method)
    {
        foreach (Match m in Regex.Matches(src, $@"\b{Regex.Escape(method)}\s*\("))
        {
            var open = src.IndexOf('(', m.Index);
            var close = MatchPair(src, open, '(', ')');
            if (close < 0)
                continue;

            var j = close + 1;
            while (j < src.Length && char.IsWhiteSpace(src[j]))
                j++;

            // 블록 본문:  void Update() { ... }
            if (j < src.Length && src[j] == '{')
            {
                var end = MatchPair(src, j, '{', '}');
                if (end > j)
                    return src[(j + 1)..end];
            }
            // 식 본문:    void Update() => Expr;
            // (뒤에 ';' 가 오는 "호출"은 여기 걸리지 않으므로 선언만 잡힌다)
            else if (j + 1 < src.Length && src[j] == '=' && src[j + 1] == '>')
            {
                var semi = src.IndexOf(';', j);
                if (semi > 0)
                    return src[(j + 2)..semi];
            }
        }
        return null;
    }

    /// <summary>주석·공백만 남으면 빈 몸통으로 본다.</summary>
    public static bool IsEffectivelyEmpty(string body)
    {
        var stripped = Regex.Replace(body, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        stripped = Regex.Replace(stripped, @"//[^\n]*", " ");
        return stripped.Trim().Length == 0;
    }

    // start 위치의 여는 문자에 대응하는 닫는 문자 인덱스(단순 깊이 계산).
    private static int MatchPair(string src, int start, char open, char close)
    {
        if (start < 0 || start >= src.Length || src[start] != open)
            return -1;

        var depth = 0;
        for (var i = start; i < src.Length; i++)
        {
            if (src[i] == open)
                depth++;
            else if (src[i] == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }
}
