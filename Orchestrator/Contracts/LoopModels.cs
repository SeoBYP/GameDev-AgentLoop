namespace Orchestrator.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// 루프가 주고받는 값들. 백엔드(두뇌)·타깃(손) 양쪽이 공유하는 "공용 계약".
// DESIGN.md §4 의 스케치를 그대로 구체화했다.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>대화 한 턴의 화자.</summary>
public enum Role
{
    User,
    Assistant,
}

/// <summary>
/// 대화 한 턴. 백엔드는 이 History를 자기 형식으로 변환한다
/// (ApiBackend → messages 배열, CLI 백엔드 → 포맷된 프롬프트).
/// </summary>
public record Turn(Role Role, string Content);

/// <summary>
/// 두뇌에 주는 맥락: 시스템 지시 + 누적 대화(목표·이전 생성물·검증 에러).
/// 백엔드는 상태를 갖지 않는다 — 매 스텝 이 맥락 전체를 받는다.
/// </summary>
public record AgentContext(string System, IReadOnlyList<Turn> History);

/// <summary>
/// 파일 하나의 "전체 덮어쓰기" 제안.
/// Phase 1 결정(DESIGN.md §7): diff/부분패치가 아니라 전체 내용으로 단순화.
/// </summary>
public record FileEdit(string RelativePath, string Content);

/// <summary>두뇌의 응답: 원문 텍스트 + 원문에서 파싱한 파일 편집.</summary>
public record AgentReply(string Text, IReadOnlyList<FileEdit> Edits);

/// <summary>적용(파일 쓰기) 결과.</summary>
public record ApplyResult(bool Ok, string Message);

/// <summary>
/// 검증 종류.
///   Compile        — 컴파일이 통과하는가(Phase 1).
///   RuntimeAssert — 플레이모드에서 실제로 의도대로 동작하는가(Phase 2).
/// 컴파일은 통과하지만 동작이 틀린 코드를 잡아내는 게 RuntimeAssert 의 존재 이유다.
/// </summary>
public enum VerifyKind
{
    Compile,
    RuntimeAssert,

    /// <summary>
    /// 성능 예산을 지키는가(Phase 5). 동작이 맞아도 핫패스에서 할당하거나 느리면 게임에선 실패다.
    /// Phase 3 스킬이 "Update 에서 할당하지 마라"를 **정적으로 추측**한다면, 이건 **실측**한다.
    /// </summary>
    Performance,
}

/// <summary>
/// 검증 요청. <paramref name="AssertCode"/> 는 RuntimeAssert 일 때만 쓰인다 —
/// 플레이모드에서 `unity command eval` 로 실행되는 C# 스니펫으로,
/// 통과 시 "OK", 실패 시 사유 문자열을 return 한다.
/// </summary>
public record VerifySpec(VerifyKind Kind, string? AssertCode = null);

/// <summary>
/// 검증 결과: 성공 여부 + 원본 로그(디버깅용) + 파싱된 에러 목록(피드백용).
/// 실패 시 Errors 를 다음 스텝 맥락에 넣어 백엔드가 스스로 고치게 한다.
/// </summary>
public record VerifyResult(bool Ok, string Log, IReadOnlyList<string> Errors);
