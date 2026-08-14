namespace Orchestrator.Contracts;

/// <summary>
/// 손 축. 생성물을 프로젝트에 적용하고, 실제로 도는지 검증한다.
/// DESIGN.md D5: UnityEditorTarget(지금)·UgsTarget(나중)이 이 인터페이스의 구현.
/// 검증이 1급 시민(D4) — Verify 는 이 프로젝트의 차별점이다.
/// </summary>
public interface IExecTarget
{
    /// <summary>로그·판정용 표시 이름 (예: "unity:6000.5.4f1").</summary>
    string Name { get; }

    /// <summary>
    /// 이 타깃에 맞는 생성 지침(언어·파일 경로·검증 스니펫 형식).
    /// 손이 바뀌면 만들어야 할 것도 바뀐다 — Unity 타깃은 C# 스크립트를, UGS 타깃은 Cloud Code JS 를 요구한다.
    /// 루프는 "FILE: 블록으로 전체 파일을 내라"는 **형식**만 소유하고, **내용 규격**은 타깃이 준다.
    /// </summary>
    string GenerationBrief { get; }

    /// <summary>이 타깃이 해당 검증을 지원하는가(예: UGS 는 CLI 에 호출 명령이 없어 런타임 assert 미지원).</summary>
    bool Supports(VerifyKind kind);

    /// <summary>
    /// 검증 종류의 사람이 읽을 이름 — 같은 <see cref="VerifyKind"/> 라도 손마다 하는 일이 다르다.
    /// Compile: Unity "컴파일" / UGS "배포".  RuntimeAssert: Unity "플레이모드 assert" / UGS "스크립트 호출".
    /// </summary>
    string LabelFor(VerifyKind kind);

    /// <summary>
    /// 손이 준비됐는지 사전 점검(에디터가 떠 있나 / UGS 인증·프로젝트가 설정됐나).
    /// 루프 시작 전에 확인해, 준비가 안 됐으면 **AI 를 부르기 전에** 실패시킨다(시간·비용 낭비 방지).
    /// </summary>
    Task<bool> IsConnectedAsync(CancellationToken ct);

    /// <summary>사전 점검 실패 시 사용자에게 보여줄 해결 안내.</summary>
    string ConnectionHint { get; }

    /// <summary>생성된 파일 편집을 프로젝트에 적용한다(파일 쓰기 + 리컴파일 트리거).</summary>
    Task<ApplyResult> ApplyAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct);

    /// <summary>검증한다(Phase 1: 컴파일 에러 수집). 성공/실패 + 에러 목록을 돌려준다.</summary>
    Task<VerifyResult> VerifyAsync(VerifySpec spec, CancellationToken ct);
}
