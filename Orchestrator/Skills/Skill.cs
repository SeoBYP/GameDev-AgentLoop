namespace Orchestrator.Skills;

/// <summary>
/// 스킬이 강제하는 정적 검사 한 건.
///   Scopes            — 검사할 메서드 이름들. ["*"] 이면 파일 전체.
///   ForbidPattern     — 스코프 안에서 발견되면 위반인 정규식.
///   ForbidEmptyBody   — 스코프 메서드의 몸통이 비어 있으면 위반.
/// </summary>
public sealed record SkillCheck(
    string Id,
    IReadOnlyList<string> Scopes,
    string? ForbidPattern,
    bool ForbidEmptyBody,
    string Message);

/// <summary>
/// 도메인 스킬 하나 = (생성 시 지킬 지침) + (산출물에 강제할 검사).
///
/// **포터블 마크다운**으로 둔 이유(DESIGN.md §7 결정): `.claude/skills` 같은 특정 CLI 전용 형식으로 두면
/// Codex/API 백엔드에서 안 먹는다. 스킬은 오케스트레이터가 소유하고 모든 백엔드에 동일하게 적용해야
/// "백엔드 교체 가능"(D1)이 유지된다.
/// </summary>
public sealed record Skill(
    string Name,
    string Title,
    bool Always,
    IReadOnlyList<string> When,
    IReadOnlyList<string> Targets,   // 비어 있으면 모든 타깃. 예: ["unity"] → Unity 타깃에서만 적용
    string Guidance,
    IReadOnlyList<SkillCheck> Checks);

/// <summary>정적 검사 위반 1건.</summary>
public sealed record SkillViolation(string SkillName, string CheckId, string FilePath, string Message)
{
    public override string ToString() => $"[{SkillName}/{CheckId}] {FilePath}: {Message}";
}
