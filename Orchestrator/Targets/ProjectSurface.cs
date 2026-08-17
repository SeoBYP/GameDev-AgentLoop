using System.Text;
using System.Text.RegularExpressions;

namespace Orchestrator.Targets;

/// <summary>
/// 대상 프로젝트에 **이미 존재하는** 공개 표면. ARCHITECTURE §2.1 노드 튜플의 <c>reads[]</c> 구현체다.
///
/// 왜 필요한가 — 실측으로 확인됐다(Sample/Roguelike 슬라이스 3):
/// 루프가 모델에 주는 건 목표 + 자기 과거 응답 + 검증 에러뿐이고 **프로젝트 상태는 안 준다.**
/// 그래서 앞선 노드의 산출물을 참조해야 하는 첫 노드가 네임스페이스와 멤버 이름을 *추측*했고,
/// 4스텝 전부 `CS0246`("타입 없음")으로 실패했다. 게다가 그 에러는 "어디 있는지"를 말해주지
/// 않아 피드백으로도 수리되지 않았다 — 스텝 3·4가 스텝 1과 같은 에러 좌표를 냈다.
/// 표면을 손으로 붙여 주자 같은 노드가 2스텝·25/25 로 통과했다.
///
/// 파일을 읽어 오게 시키는 방식은 배제된다 — 우리 백엔드는 **도구 없는 텍스트 생성기**다
/// (이미 실측된 제약: 피드백에 "경로만 주기"가 불가능했던 것과 같은 이유).
/// 그래서 표면을 **프롬프트에 넣는다.**
///
/// Roslyn 을 붙이지 않은 이유는 <see cref="Skills.CSharpSource"/> 와 같다 — 필요한 건
/// 선언부(네임스페이스·타입·시그니처)뿐이고 몸통 해석이 아니다. 대신 **못 읽은 건 추측하지 않고
/// 건너뛴 것으로 기록한다**(<see cref="SkippedFiles"/>). 틀린 시그니처는 없는 것보다 나쁘다 —
/// 위 실측이 정확히 그걸 보여줬다(내가 생성자를 빼먹었더니 실패가 그 멤버 하나에만 떨어졌다).
/// </summary>
public sealed record ProjectSurface(
    IReadOnlyList<SurfaceType> Types,
    IReadOnlyList<string> SkippedFiles)
{
    public static readonly ProjectSurface Empty = new(Array.Empty<SurfaceType>(), Array.Empty<string>());

    public bool IsEmpty => Types.Count == 0;

    /// <summary>
    /// 런타임 스크립트 디렉터리를 훑어 공개 표면을 만든다.
    /// 테스트 디렉터리는 제외한다 — 런타임 코드가 읽는 대상이 아니다.
    /// </summary>
    /// <param name="reads">
    /// 좁히기 필터(타입 이름 또는 경로 조각). 비우면 런타임 표면 전체.
    /// §2.1 은 노드가 이걸 선언한다고 정의한다. 계획 레이어(§2)가 없는 지금은 CLI 가 대신 준다.
    /// </param>
    /// <param name="includeTests">
    /// 테스트 디렉터리도 포함할지. 프롬프트 다이제스트는 **런타임만**(테스트는 노드가 읽는 대상이 아니다),
    /// 삭제 게이트(§8.3)는 **포함**한다 — 실측된 부정행위가 정확히 거기서 일어났다:
    /// 모델이 멤버를 지운 뒤 그 멤버를 쓰던 테스트까지 지우면 스위트가 초록이 된다.
    /// </param>
    public static ProjectSurface Read(
        string projectRoot, ProjectLayout layout, IReadOnlyList<string>? reads = null, bool includeTests = false)
    {
        var scriptDir = Path.Combine(projectRoot, layout.ScriptDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(scriptDir))
            return Empty;

        var testDir = Path.GetFullPath(Path.Combine(projectRoot, layout.TestDir.Replace('/', Path.DirectorySeparatorChar)));

        var types = new List<SurfaceType>();
        var skipped = new List<string>();

        var roots = new List<string> { scriptDir };
        if (includeTests && Directory.Exists(testDir) &&
            !Path.GetFullPath(testDir).StartsWith(Path.GetFullPath(scriptDir), StringComparison.OrdinalIgnoreCase))
            roots.Add(testDir);

        foreach (var file in roots
                     .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
                     .Distinct()
                     .OrderBy(f => f))
        {
            if (!includeTests && Path.GetFullPath(file).StartsWith(testDir, StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
            try
            {
                var found = Extract(File.ReadAllText(file), rel);
                if (found.Count == 0)
                    skipped.Add(rel);          // 공개 타입이 없거나 못 읽었다 — 둘 다 정직하게 기록
                else
                    types.AddRange(found);
            }
            catch (IOException) { skipped.Add(rel); }
            catch (UnauthorizedAccessException) { skipped.Add(rel); }
        }

        if (reads is { Count: > 0 })
            types = types.Where(t => reads.Any(r => Matches(t, r))).ToList();

        return new ProjectSurface(types, skipped);
    }

    private static bool Matches(SurfaceType t, string read) =>
        t.Name.Equals(read, StringComparison.OrdinalIgnoreCase) ||
        t.File.Contains(read, StringComparison.OrdinalIgnoreCase);

    /// <summary>지정한 <c>reads</c> 항목 중 아무 타입도 맞추지 못한 것들. 시작 거부 판단에 쓴다.</summary>
    public static IReadOnlyList<string> Unresolved(ProjectSurface all, IReadOnlyList<string> reads) =>
        reads.Where(r => !all.Types.Any(t => Matches(t, r))).ToList();

    /// <summary>프롬프트에 넣을 다이제스트. 없으면 빈 문자열.</summary>
    public string ToDigest(string? runtimeAssembly)
    {
        if (IsEmpty)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("ALREADY IN THIS PROJECT (do not redeclare, do not modify — call these as declared):");
        sb.AppendLine($"  assembly `{runtimeAssembly ?? "-"}`. Signatures only; bodies omitted.");

        foreach (var ns in Types.GroupBy(t => t.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine(ns.Key.Length == 0 ? "// (global namespace)" : $"namespace {ns.Key}");
            foreach (var t in ns)
            {
                sb.AppendLine($"  {t.Declaration}        // {t.File}");
                foreach (var m in t.Members)
                    sb.AppendLine($"      {m}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ── 추출 ──────────────────────────────────────────────────────────────────

    private static readonly Regex NamespaceRx = new(@"\bnamespace\s+([\w\.]+)", RegexOptions.Compiled);

    // 타입 선언: 접근자 + 수식어들 + (class|struct|interface|enum|record [struct|class]) + 이름
    private static readonly Regex TypeRx = new(
        @"\bpublic\s+((?:(?:sealed|abstract|static|partial|readonly|unsafe|ref)\s+)*)(class|struct|interface|enum|record\s+struct|record\s+class|record)\s+(\w+)",
        RegexOptions.Compiled);

    private static List<SurfaceType> Extract(string raw, string relPath)
    {
        var src = Blank(raw);
        var result = new List<SurfaceType>();

        foreach (Match m in TypeRx.Matches(src))
        {
            var kind = Regex.Replace(m.Groups[2].Value, @"\s+", " ");
            var name = m.Groups[3].Value;

            // 이 선언을 감싸는 가장 가까운 namespace (앞쪽에서 마지막으로 나온 것)
            var ns = NamespaceRx.Matches(src)
                .Where(n => n.Index < m.Index)
                .Select(n => n.Groups[1].Value)
                .LastOrDefault() ?? string.Empty;

            var open = src.IndexOf('{', m.Index + m.Length);
            if (open < 0)
                continue;
            var close = MatchBrace(src, open);
            if (close < 0)
                continue;

            var body = src[(open + 1)..close];
            var members = kind == "enum" ? EnumMembers(body) : PublicMembers(body);

            var mods = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
            var decl = $"{(mods.Length == 0 ? "" : mods + " ")}{kind} {name}";
            result.Add(new SurfaceType(ns, name, decl, members, relPath));
        }

        return result;
    }

    private static List<string> EnumMembers(string body)
    {
        var names = body.Split(',')
            .Select(part => Regex.Match(part, @"\b(\w+)\b").Groups[1].Value)
            .Where(v => v.Length > 0)
            .ToList();
        return names.Count == 0 ? new List<string>() : new List<string> { string.Join(", ", names) };
    }

    /// <summary>
    /// 타입 몸통의 **최상위 깊이**에서 `public` 로 시작하는 선언들의 시그니처.
    /// 몸통(블록)과 식 본문은 버린다 — 다이제스트는 계약이고 구현이 아니다.
    /// </summary>
    private static List<string> PublicMembers(string body)
    {
        var members = new List<string>();
        var depth = 0;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];

            if (c == '{') { depth++; continue; }
            if (c == '}') { depth--; continue; }
            if (depth != 0) continue;

            if (!IsWordAt(body, i, "public"))
                continue;

            var (signature, next) = ReadSignature(body, i);
            if (signature is not null)
                members.Add(signature);
            i = next - 1;                          // for 문이 ++ 하므로 -1
        }

        return members;
    }

    /// <summary>`public` 위치에서 시그니처 하나를 읽고, 그 선언이 끝난 다음 인덱스를 돌려준다.</summary>
    private static (string? Signature, int Next) ReadSignature(string body, int start)
    {
        var paren = 0;
        var angle = 0;

        for (var i = start; i < body.Length; i++)
        {
            var c = body[i];

            if (c == '(') paren++;
            else if (c == ')') paren--;
            else if (c == '<') angle++;
            else if (c == '>' && angle > 0) angle--;

            if (paren > 0 || angle > 0)
                continue;

            // 필드 / 본문 없는 선언
            if (c == ';')
                return (Normalize(body[start..i]), i + 1);

            // 식 본문 멤버:  public int X => _x;
            if (c == '=' && i + 1 < body.Length && body[i + 1] == '>')
            {
                var semi = body.IndexOf(';', i);
                return (Normalize(body[start..i]), semi < 0 ? body.Length : semi + 1);
            }

            // 블록: 메서드 몸통이거나 프로퍼티 접근자
            if (c == '{')
            {
                var end = MatchBrace(body, i);
                if (end < 0)
                    return (Normalize(body[start..i]), body.Length);

                var inner = body[(i + 1)..end].Trim();
                var sig = Normalize(body[start..i]);

                // 자동 프로퍼티는 접근자가 계약의 일부다 — { get; } 과 { get; set; } 은 다른 약속이다.
                if (Regex.IsMatch(inner, @"^(get|set|init)\s*;(\s*(get|set|init)\s*;)*$"))
                    sig += " { " + Regex.Replace(inner, @"\s+", " ") + " }";

                return (sig, end + 1);
            }
        }

        return (null, body.Length);
    }

    // `public` 접두사와 여분 공백을 정리한다. 속성([SerializeField] 등)은 Blank 단계에서 안 지워지므로 여기서 제거.
    private static string Normalize(string decl)
    {
        var s = Regex.Replace(decl, @"\[[^\]]*\]", " ");          // 속성
        s = Regex.Replace(s, @"^\s*public\s+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static bool IsWordAt(string s, int i, string word)
    {
        if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            return false;
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_'))
            return false;
        var after = i + word.Length;
        return after >= s.Length || !(char.IsLetterOrDigit(s[after]) || s[after] == '_');
    }

    /// <summary>주석과 문자열 리터럴을 같은 길이의 공백으로 바꾼다 — 인덱스가 원문과 일치해야 한다.</summary>
    private static string Blank(string src)
    {
        var sb = new StringBuilder(src);
        var i = 0;

        while (i < sb.Length)
        {
            // 줄 주석
            if (i + 1 < sb.Length && sb[i] == '/' && sb[i + 1] == '/')
            {
                while (i < sb.Length && sb[i] != '\n') { sb[i] = ' '; i++; }
                continue;
            }
            // 블록 주석
            if (i + 1 < sb.Length && sb[i] == '/' && sb[i + 1] == '*')
            {
                while (i < sb.Length && !(i + 1 < sb.Length && sb[i] == '*' && sb[i + 1] == '/'))
                {
                    if (sb[i] != '\n') sb[i] = ' ';
                    i++;
                }
                for (var k = 0; k < 2 && i < sb.Length; k++, i++) sb[i] = ' ';
                continue;
            }
            // 문자열 / 문자 리터럴 — 안의 중괄호·public 이 스캔을 흔들지 않게 비운다
            if (sb[i] == '"' || sb[i] == '\'')
            {
                var quote = sb[i];
                var verbatim = i > 0 && sb[i - 1] == '@';
                i++;
                while (i < sb.Length && sb[i] != quote)
                {
                    if (!verbatim && sb[i] == '\\' && i + 1 < sb.Length) { sb[i] = ' '; i++; }
                    if (sb[i] != '\n') sb[i] = ' ';
                    i++;
                }
                if (i < sb.Length) i++;
                continue;
            }
            i++;
        }

        return sb.ToString();
    }

    private static int MatchBrace(string s, int open)
    {
        if (open < 0 || open >= s.Length || s[open] != '{')
            return -1;

        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }
}

/// <summary>다이제스트에 실리는 공개 타입 하나.</summary>
public sealed record SurfaceType(
    string Namespace,
    string Name,
    string Declaration,
    IReadOnlyList<string> Members,
    string File);
