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

    /// <summary>생성된 파일 편집을 프로젝트에 적용한다(파일 쓰기 + 리컴파일 트리거).</summary>
    Task<ApplyResult> ApplyAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct);

    /// <summary>검증한다(Phase 1: 컴파일 에러 수집). 성공/실패 + 에러 목록을 돌려준다.</summary>
    Task<VerifyResult> VerifyAsync(VerifySpec spec, CancellationToken ct);
}
