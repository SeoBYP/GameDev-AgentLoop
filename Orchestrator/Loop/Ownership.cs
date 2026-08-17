using System.Text.RegularExpressions;

namespace Orchestrator.Loop;

/// <summary>
/// 노드가 **쓸 수 있는 경로**. ARCHITECTURE §3.2 `owns[]` 의 구현.
///
/// 왜 필요한가 — 실측이다(Sample/Roguelike 슬라이스 3, `--no-surface`):
/// 타입을 못 찾은 모델이 이웃 파일(`Actor.cs`·`DungeonGrid.cs`)과 **이웃의 테스트까지 다시 써서**
/// 계약을 축소했고, 그러자 전부 컴파일되고 남은 테스트가 전부 통과해 루프가 **성공을 선언**했다.
/// 목표 문장에 "Do NOT modify DungeonGrid or Actor" 가 그대로 있었는데도 그랬다 —
/// **프롬프트 금지는 강제가 아니다.**
///
/// 그래서 검사는 적용 **전에** 한다. 쓴 다음에 잡으면 이미 늦다.
///
/// 기본값을 "기존 파일 전면 보호"로 하지 않은 이유도 측정이다: 데모 5종이 쓰는
/// `Assets/Scripts/Health.cs` 등이 **이미 레포에 존재**하므로, 전면 보호는 §11 1단계의 회귀 기준
/// ("데모 5종이 같은 판정을 낸다")을 그대로 깨뜨린다. 소유권은 추론할 수 없다 —
/// §2.1 은 그걸 **계획 레이어가 선언한다**고 정의한다. 계획 레이어가 없는 지금은 CLI(`--owns`)가 대신한다.
/// 선언이 없으면 강제하지 않고, 대신 기존 파일을 고쳤다는 사실을 **기록**한다(보이기라도 해야 한다 —
/// 위 사고는 git 비교로만 발견됐다).
/// </summary>
public static class Ownership
{
    /// <summary>선언된 소유 범위를 벗어난 경로들. 비면 위반 없음.</summary>
    public static IReadOnlyList<string> Violations(
        IEnumerable<string> paths, IReadOnlyList<string>? owns)
    {
        if (owns is null || owns.Count == 0)
            return Array.Empty<string>();

        return paths.Where(p => !owns.Any(pattern => Matches(p, pattern))).ToList();
    }

    /// <summary>
    /// glob 매칭. `**` 는 구분자를 넘고, `*`/`?` 는 한 구간 안에서만 매칭한다.
    /// 와일드카드도 확장자도 없는 패턴은 **디렉터리 접두사**로 본다
    /// (`Assets/Scripts/Core` == `Assets/Scripts/Core/**`) — 안 그러면 그게 제일 흔한 오해가 된다.
    /// </summary>
    public static bool Matches(string path, string pattern)
    {
        var p = Normalize(path);
        var pat = Normalize(pattern);

        if (!pat.Contains('*') && !pat.Contains('?') && !Path.HasExtension(pat))
            pat = pat.TrimEnd('/') + "/**";

        var rx = "^" + Regex.Escape(pat)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]") + "$";

        return Regex.IsMatch(p, rx, RegexOptions.IgnoreCase);
    }

    /// <summary>경로 비교 키 — 구분자와 대소문자를 정규화한다(Windows 경로와 선언이 섞여 들어온다).</summary>
    public static string Key(string relativePath) => Normalize(relativePath).ToLowerInvariant();

    private static string Normalize(string s) => s.Replace('\\', '/').TrimStart('.', '/');
}
