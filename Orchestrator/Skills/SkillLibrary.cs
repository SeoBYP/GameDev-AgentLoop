using System.Text;
using System.Text.RegularExpressions;
using Orchestrator.Contracts;

namespace Orchestrator.Skills;

/// <summary>
/// `Skills/*.md` 를 읽어 (지침 + 검사)로 만드는 라이브러리.
///
/// 마크다운 형식(의존성 없이 파싱하려고 단순하게 유지):
///   ---
///   name: unity-performance
///   title: ...
///   always: true
///   when: 성능, 최적화        (always 가 아닐 때 목표 문자열과 매칭)
///   ---
///   ## GUIDANCE
///   - 규칙들...
///   ## CHECKS
///   - id: no-getcomponent-in-update
///     scope: Update, FixedUpdate
///     forbid: \bGetComponents?\s*&lt;
///     message: ...
/// </summary>
public sealed class SkillLibrary
{
    private readonly IReadOnlyList<Skill> _skills;

    public IReadOnlyList<Skill> All => _skills;

    private SkillLibrary(IReadOnlyList<Skill> skills) => _skills = skills;

    public static SkillLibrary Empty { get; } = new(Array.Empty<Skill>());

    /// <summary>디렉터리의 *.md 를 모두 로드한다. 없으면 빈 라이브러리.</summary>
    public static SkillLibrary Load(string directory)
    {
        if (!Directory.Exists(directory))
            return Empty;

        var skills = new List<Skill>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.md").OrderBy(f => f))
        {
            var skill = ParseFile(File.ReadAllText(file));
            if (skill is not null)
                skills.Add(skill);
        }
        return new SkillLibrary(skills);
    }

    /// <summary>
    /// 목표·타깃에 적용할 스킬을 고른다.
    /// (1) 타깃이 맞아야 하고(targets 미지정이면 모든 타깃), (2) always 이거나 when 키워드가 목표에 포함되어야 한다.
    /// </summary>
    public IReadOnlyList<Skill> Select(string goal, string target)
    {
        var lowered = goal.ToLowerInvariant();
        return _skills
            .Where(s => s.Targets.Count == 0 ||
                        s.Targets.Any(t => t.Equals(target, StringComparison.OrdinalIgnoreCase)))
            .Where(s => s.Always || s.When.Any(w => lowered.Contains(w.ToLowerInvariant())))
            .ToList();
    }

    /// <summary>선택된 스킬들의 지침을 시스템 프롬프트에 넣을 한 덩어리로 만든다.</summary>
    public static string BuildGuidance(IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("DOMAIN RULES (반드시 지킬 것 — 위반 시 정적 검사에서 자동 반려됩니다):");
        foreach (var s in skills)
        {
            sb.AppendLine();
            sb.AppendLine($"### {s.Title}");
            sb.AppendLine(s.Guidance.Trim());
        }
        return sb.ToString();
    }

    /// <summary>생성된 파일들에 선택된 스킬의 검사를 돌려 위반 목록을 만든다.</summary>
    public static IReadOnlyList<SkillViolation> Inspect(
        IReadOnlyList<Skill> skills,
        IReadOnlyList<FileEdit> edits)
    {
        var violations = new List<SkillViolation>();
        foreach (var skill in skills)
        foreach (var check in skill.Checks)
        foreach (var edit in edits)
        {
            // 검사는 런타임 스크립트에만 적용한다.
            // 에디터 스크립트·테스트 코드는 규칙이 다르다(테스트는 public 메서드·임시 할당이 정상).
            var path = edit.RelativePath.Replace('\\', '/');
            if (path.Contains("/Editor/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/Tests/", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var message in RunCheck(check, edit.Content))
                violations.Add(new SkillViolation(skill.Name, check.Id, edit.RelativePath, message));
        }
        return violations;
    }

    private static IEnumerable<string> RunCheck(SkillCheck check, string content)
    {
        // 파일 전체 스코프
        if (check.Scopes.Count == 1 && check.Scopes[0] == "*")
        {
            if (check.ForbidPattern is not null && Regex.IsMatch(content, check.ForbidPattern))
                yield return check.Message;
            yield break;
        }

        foreach (var method in check.Scopes)
        {
            var body = CSharpSource.ExtractMethodBody(content, method);
            if (body is null)
                continue;

            if (check.ForbidEmptyBody && CSharpSource.IsEffectivelyEmpty(body))
            {
                yield return $"{check.Message} (문제 메서드: {method})";
                continue;
            }

            if (check.ForbidPattern is not null && Regex.IsMatch(body, check.ForbidPattern))
                yield return $"{check.Message} (문제 메서드: {method})";
        }
    }

    // ── 파싱 ──────────────────────────────────────────────────────────────────
    private static Skill? ParseFile(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        // front-matter
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (i < lines.Length && lines[i].Trim() == "---")
        {
            i++;
            while (i < lines.Length && lines[i].Trim() != "---")
            {
                var (k, v) = SplitKeyValue(lines[i]);
                if (k is not null)
                    meta[k] = v;
                i++;
            }
            i++; // 닫는 ---
        }

        if (!meta.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return null;

        // 섹션 분리
        var guidance = new StringBuilder();
        var checkLines = new List<string>();
        var section = "";
        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = line[3..].Trim().ToUpperInvariant();
                continue;
            }
            if (section == "GUIDANCE")
                guidance.AppendLine(line);
            else if (section == "CHECKS")
                checkLines.Add(line);
        }

        return new Skill(
            Name: name.Trim(),
            Title: meta.TryGetValue("title", out var t) ? t.Trim() : name.Trim(),
            Always: meta.TryGetValue("always", out var a) && a.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
            When: meta.TryGetValue("when", out var w) ? SplitList(w) : Array.Empty<string>(),
            Targets: meta.TryGetValue("targets", out var tg) ? SplitList(tg) : Array.Empty<string>(),
            Guidance: guidance.ToString(),
            Checks: ParseChecks(checkLines));
    }

    private static IReadOnlyList<SkillCheck> ParseChecks(IReadOnlyList<string> lines)
    {
        var checks = new List<SkillCheck>();
        Dictionary<string, string>? current = null;

        void Flush()
        {
            if (current is null || !current.TryGetValue("id", out var id))
                return;
            checks.Add(new SkillCheck(
                Id: id,
                Scopes: current.TryGetValue("scope", out var sc) ? SplitList(sc) : new[] { "*" },
                ForbidPattern: current.TryGetValue("forbid", out var f) ? f : null,
                ForbidEmptyBody: current.TryGetValue("forbid-empty-body", out var e) &&
                                 e.Equals("true", StringComparison.OrdinalIgnoreCase),
                Message: current.TryGetValue("message", out var m) ? m : id));
            current = null;
        }

        foreach (var raw in lines)
        {
            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var (k, v) = SplitKeyValue(trimmed[2..]);
                if (k is not null)
                    current[k] = v;
            }
            else if (current is not null && trimmed.Length > 0)
            {
                var (k, v) = SplitKeyValue(trimmed);
                if (k is not null)
                    current[k] = v;
            }
        }
        Flush();
        return checks;
    }

    // "key: value" → (key, value). 값에 콜론이 있을 수 있으므로 첫 콜론만 자른다(정규식 대비).
    private static (string? Key, string Value) SplitKeyValue(string line)
    {
        var idx = line.IndexOf(':');
        if (idx <= 0)
            return (null, "");
        return (line[..idx].Trim(), line[(idx + 1)..].Trim());
    }

    private static string[] SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
