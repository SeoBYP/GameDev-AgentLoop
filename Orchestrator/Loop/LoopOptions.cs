namespace Orchestrator.Loop;

/// <summary>
/// 검증에 **임시 코드 실행(eval)** 을 허용할지.
///
///   Auto      — 테스트가 있으면 테스트로, 없으면 ASSERT/PERF 스니펫을 eval 로 실행(기본).
///   TestsOnly — eval 을 아예 쓰지 않는다. 검증은 **컴파일된 테스트 파일**로만 한다.
///
/// TestsOnly 의 값: AI 가 만든 코드가 임시 스니펫이 아니라 **레포에 남는 리뷰 가능한 파일**로만
/// 실행된다. git diff 에 걸리고, 사람이 읽을 수 있고, 되돌릴 수 있다.
/// (이건 격리가 아니라 **감사 가능성**이다 — 진짜 격리는 OS 수준의 몫. DESIGN §6.12 참고)
/// </summary>
public enum VerifyMode
{
    Auto,
    TestsOnly,
}

/// <summary>루프 가드/설정.</summary>
public sealed record LoopOptions
{
    /// <summary>최대 반복 횟수(무한루프 가드). DESIGN.md §5 기본값 6.</summary>
    public int MaxSteps { get; init; } = 6;

    /// <summary>
    /// 사람이 지정한 플레이모드 런타임 assert(선택). 주면 백엔드가 낸 ASSERT 블록보다 **우선**한다.
    /// AI 가 자기 코드를 자기 기준으로 채점하는 것을 막고 싶을 때 쓰는 권위 있는 검증 기준.
    /// </summary>
    public string? Assert { get; init; }

    /// <summary>성공 시 결과 화면을 캡처해 남길 디렉터리(지정 시에만 캡처).</summary>
    public string? CaptureDir { get; init; }

    /// <summary>
    /// 백엔드에 보낼 최근 대화 턴 수(목표 턴은 항상 유지). 0 이면 무제한.
    ///
    /// 왜 필요한가: 출력 계약이 "매번 전체 파일"이라 스텝마다 파일 전문이 히스토리에 쌓인다.
    /// 6스텝이면 같은 파일이 6벌 — 컨텍스트·비용이 선형으로 터진다.
    /// 오래된 시도는 최신 전체 파일로 대체되므로 버려도 안전하다.
    /// </summary>
    public int HistoryWindow { get; init; } = 4;

    /// <summary>
    /// 실행 기록을 남길 위치(기본 &lt;project&gt;/.agentloop/runs).
    /// 전체 에러 원문은 span 산출물로 남는다 — 모델에는 요약만 가고, 사람은 전문을 본다.
    /// </summary>
    public string? RunsDir { get; init; }

    /// <summary>검증에 임시 코드 실행(eval)을 허용할지. TestsOnly 면 테스트 파일로만 검증한다.</summary>
    public VerifyMode VerifyMode { get; init; } = VerifyMode.Auto;

    /// <summary>
    /// 프로젝트에 이미 있는 공개 표면 다이제스트(ARCHITECTURE §2.1 <c>reads[]</c>). 비면 주입하지 않는다.
    ///
    /// 히스토리 윈도우가 *과거 시도*에서 아낀 컨텍스트를 *프로젝트 상태*에 쓰는 자리다(§3.1 두 번째 행:
    /// "스텝당 컨텍스트 — 전부 매번 vs 이 노드와 계약만"). 실측으로 필요성이 확인됐다:
    /// 이게 없으면 앞선 노드의 타입을 참조해야 하는 노드가 네임스페이스를 추측해 4스텝 전부 실패했다.
    /// </summary>
    public string? Surface { get; init; }

    /// <summary>
    /// 이 노드가 **쓸 수 있는** 경로 glob (ARCHITECTURE §3.2 `owns[]`). 비면 강제하지 않는다.
    ///
    /// 비었을 때 강제하지 않는 건 타협이 아니라 측정 결과다 — 데모 5종이 쓰는 파일들이 이미
    /// 레포에 있어서 "기존 파일 전면 보호"를 기본값으로 하면 회귀 기준이 깨진다.
    /// 소유권은 추론 대상이 아니라 **선언 대상**이다(§2.1).
    /// </summary>
    public IReadOnlyList<string>? Owns { get; init; }

    /// <summary>
    /// 실행 시작 시점에 이미 있던 파일들(정규화된 상대경로). 소유권 선언이 없을 때
    /// "기존 파일을 고쳤다"는 사실을 **기록**하는 데만 쓴다 — 판정은 바꾸지 않는다.
    /// </summary>
    public IReadOnlySet<string> PreExisting { get; init; } = new HashSet<string>();

    /// <summary>
    /// 삭제 게이트(§8.3)의 기준선 — 실행 시작 시점의 공개 표면. null 이면 게이트는 Skip 한다.
    /// </summary>
    public Targets.ProjectSurface? SurfaceBaseline { get; init; }

    /// <summary>
    /// 현재 표면을 다시 읽는 함수. 노드가 경로를 알 필요가 없게 주입한다
    /// (record 의 값 동등성이 델리게이트 때문에 깨지지만, 여기에 의존하는 코드는 없다).
    /// </summary>
    public Func<Targets.ProjectSurface>? ReadSurface { get; init; }
}

/// <summary>루프 종료 판정 결과.</summary>
public sealed record LoopResult(bool Success, int Steps, string Summary);
