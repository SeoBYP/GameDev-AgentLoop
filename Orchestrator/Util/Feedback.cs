using System.Text;

namespace Orchestrator.Util;

/// <summary>
/// 모델에게 되돌릴 피드백의 크기를 제한한다.
///
/// 왜 필요한가: 컴파일 에러가 수십 건이거나 테스트 실패에 스택트레이스가 붙으면 피드백 하나가
/// 수천 자가 된다. 그게 매 스텝 히스토리에 쌓이면 컨텍스트·비용이 터진다.
///
/// **왜 "파일 경로만 주기"는 안 되나:** 레퍼런스(unity-cli-loop)는 AI 가 파일 읽기 도구를 갖고 있어
/// 큰 결과를 파일로 빼고 경로만 넘기지만, 우리 백엔드는 **도구 없는 순수 텍스트 생성기**(D1)라
/// 경로를 줘도 읽지 못한다. 그래서 우리는 **모델에겐 상위 N건 요약, 사람에겐 전체 로그 파일**로 나눈다.
/// </summary>
public static class Feedback
{
    public const int MaxItems = 8;
    public const int MaxItemChars = 400;

    /// <summary>항목 목록을 상한에 맞춰 잘라 "  - 항목" 형태로 만든다.</summary>
    public static string Bullets(IReadOnlyList<string> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items.Take(MaxItems))
            sb.AppendLine("  - " + Clip(item, MaxItemChars));

        if (items.Count > MaxItems)
            sb.AppendLine($"  … and {items.Count - MaxItems} more (fix these categories first)");

        return sb.ToString().TrimEnd();
    }

    public static string Clip(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + " …(truncated)";
    }

    /// <summary>대략적인 맥락 크기(문자 수) — 절약 효과를 로그로 보이기 위한 지표.</summary>
    public static int ApproxChars(string system, IEnumerable<string> turns) =>
        system.Length + turns.Sum(t => t.Length);
}
