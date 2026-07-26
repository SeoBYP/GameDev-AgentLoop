# GameDev-AgentLoop — 설계 노트

이 문서는 프로젝트의 아키텍처와 "왜 이렇게 정했는지"를 박제한다. 결정을 바꾸려면 이 문서를 먼저 고친다.

---

## 1. 문제와 목표

- **문제:** CLI AI 에이전트는 코드를 잘 "생성"하지만, 그게 Unity에서 실제로 도는지 스스로 확인·수리하지 못한다. 그럴듯하지만 깨진 코드가 나온다.
- **목표:** 생성 → 적용 → **검증** → 수리 → 반복하는 **닫힌 루프**로, "동작이 검증된 결과"를 만든다.
- **가능해진 이유:** Unity CLI(`unity command eval`)가 실행 중 에디터에서 C#을 즉시 실행·검증하게 해준다(2026-07 발표). 이게 루프의 "검증" 단계를 가능하게 하는 핵심 인에이블러.

---

## 2. 아키텍처 한 장

```
        ┌─────────── Orchestrator (C#) : 루프 소유 ───────────┐
목표 →  │  ①생성 → ②적용 → ③검증 → ④피드백 → ⑤판정(반복)     │
        └──┬─────────────────────────────┬────────────────────┘
     IAgentBackend                    IExecTarget
   ├ ApiBackend        (Phase 1)     ├ UnityEditorTarget (unity CLI)  (Phase 1)
   ├ ClaudeCodeBackend (Phase 2)     └ UgsTarget         (ugs CLI)    (Phase 4)
   └ CodexBackend      (Phase 2)
```

**두 축이 pluggable = 두뇌(어떤 AI) × 손(어디에 적용·검증).** 언어를 둘로 나누지 않고, 이 두 인터페이스로 "둘 다"를 실현한다.

---

## 3. 핵심 결정과 근거 (Decision Log)

### D1. 백엔드 = "텍스트 생성기", 루프 = 우리 것
Claude Code는 자체 에이전트 루프가 있다. 그걸 그대로 쓰면(위임) 우리 루프는 껍데기가 되고 Codex/API와 비교·교체가 안 된다.
→ **백엔드는 "맥락 주면 텍스트 응답" 계약만** 갖게 하고(최소공통분모), 적용·검증·재시도는 오케스트레이터가 소유한다.
→ 이점: (a) 백엔드 진짜 교체 가능, (b) 루프 자체가 결과물(포폴 가치), (c) 검증이 일관됨.
→ 대가(인정): Claude Code의 자체 에이전트 능력은 안 쓴다. CLI 백엔드는 headless/print 모드로 "1회 응답"만 받는다. (`claude -p`, `codex exec` 등)

### D2. 언어 = C# 단일
오케스트레이터가 하는 일(프로세스 호출·출력 파싱·AI 호출)은 C#/Python 둘 다 잘한다 — 기술적 우열 없음. 그럼 전략:
- 사용자 강점·정체성 = Unity C#. Unity쪽은 어차피 무조건 C#. → **전 스택 한 언어**가 깔끔하고 어필됨.
- Python의 유일한 장점(AI 글루 생태계)은 C# 네이티브인 사용자에겐 이득이 적다.
→ **"둘 다(언어)"는 하지 않는다.** 같은 걸 두 번 만드는 것 = 깊이 반토막, 사용자 이득 0.

### D3. 첫 백엔드 = ApiBackend
루프를 세우는 단계라 백엔드는 제일 단순·확정적이어야 한다(버그가 루프에 있음이 명확하도록). API 직접 = 맥락/에러 제어 쉬움, 외부 CLI 의존 0, 사용자가 이미 해봄.
→ CLI 백엔드(ClaudeCode/Codex)는 **루프가 도는 걸 확인한 뒤** "인터페이스 하나로 갈아끼워진다"를 증명하는 용도(Phase 2).

### D4. 검증이 1급 시민
"실행"까지가 아니라 "동작 검증·수리"까지가 성공 기준. 이게 이 프로젝트의 차별점.

### D5. UGS는 또 하나의 "타깃"
UGS(Cloud Code·Economy·Remote Config 등)는 `IExecTarget`의 또 다른 구현(`ugs` CLI). 클라(Unity) + 백엔드(UGS)를 걸친 기능을 양쪽 다 검증 → 풀스택. 단 무거우므로 Phase 4로 미룬다.

---

## 4. 인터페이스 골격 (스케치 — 확정 아님, Phase 1에서 구체화)

```csharp
// 두뇌: 맥락(목표 + 이전 생성물 + 검증 에러 누적)을 주면 다음 응답을 돌려준다.
interface IAgentBackend {
    Task<AgentReply> CompleteAsync(AgentContext ctx, CancellationToken ct);
}
record AgentContext(string System, IReadOnlyList<Turn> History);
record AgentReply(string Text, IReadOnlyList<FileEdit> Edits); // 텍스트 + 파일변경 제안

// 손: 생성물을 프로젝트에 적용하고 검증 결과를 돌려준다.
interface IExecTarget {
    Task<ApplyResult>  ApplyAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct);
    Task<VerifyResult> VerifyAsync(VerifySpec spec, CancellationToken ct); // 컴파일/플레이모드 검증
}
record VerifyResult(bool Ok, string Log, IReadOnlyList<string> Errors);
```

- **맥락 정규화:** 같은 History를 API는 `messages`로, CLI 백엔드는 포맷된 프롬프트로 변환.
- **출력 정규화:** 응답 텍스트에서 코드펜스/편집블록을 파싱해 `FileEdit`로. (형식은 system 프롬프트로 강제)
- **백엔드 스위치:** `AGENT_BACKEND=api|claude-code|codex` 환경변수.

---

## 5. 루프 상세 (Phase 1)

1. **생성** — `backend.CompleteAsync(ctx)` → 코드(FileEdit).
2. **적용** — `target.ApplyAsync(edits)` → `Assets/` 밑에 파일 쓰기 → `unity` CLI로 리컴파일 트리거(또는 `unity command eval`로 즉시 실행).
3. **검증** — `target.VerifyAsync` → 컴파일 에러 목록 수집(우선), 이후 플레이모드 assert(`eval`로 상태 확인).
4. **피드백** — 검증 실패면 에러 로그를 History에 추가.
5. **판정** — Ok면 종료. 아니면 1로. `maxSteps`(예: 6) 초과 시 중단.

**첫 마일스톤 정의:** 입력 *"HP 컴포넌트 만들어줘"* → 루프가 스크립트 생성 → 컴파일 → 에러 시 자가수리 → **컴파일 통과**로 종료. (도메인 지식·스킬 0, 순수 루프 증명)

---

## 6. 로드맵

| Phase | 내용 | 산출물 |
|---|---|---|
| **1** ✅ | 로컬 순수 루프 골격 (`ApiBackend` + `UnityEditorTarget`) | 자가수리로 컴파일 통과하는 루프 — **달성**(§6.5) |
| **2** 🚧 | CLI 백엔드(ClaudeCode/Codex ✅) + 플레이모드 검증 | agent-agnostic **CLI 두 축 달성**(Claude Code·Codex), 플레이모드 남음 |
| **3** | 도메인 Skills(최적화·아키텍처·함정) | 산출 코드 "품질" 대조 데모 |
| **4** | `UgsTarget`(Cloud Code 배포·검증) | 클라+백엔드 풀스택 |

---

## 6.5 구현 현황 (Phase 1 — 달성)

골격은 [`Orchestrator/`](../Orchestrator/README.md) 에 구현됐고, 첫 마일스톤(*"HP 컴포넌트 자가수리 컴파일 통과"*)을
`--demo`(스크립트 백엔드)로 **2스텝 만에** 실측 확인했다. `dotnet build` 경고 0/오류 0.

**설계 → 코드 매핑**
- `IAgentBackend` → `ApiBackend`(Anthropic Messages API `HttpClient` 직통, 의존성 0), `ScriptedBackend`(키 없이 루프 증명).
- `IExecTarget` → `UnityEditorTarget`.
- 루프 5단계 → `AgentLoop` (maxSteps 가드).

**핵심 인에이블러의 실제 인터페이스(`com.unity.pipeline 0.4.0-exp.1`)**
정찰(`unity command`) 결과, "리컴파일→에러 수집"이 목적별 명령으로 제공됨을 발견하고 그걸 채택했다
(초안의 `eval`+콘솔 리플렉션보다 견고):

| 루프 단계 | pipeline 명령 | 반환 |
|---|---|---|
| ② 적용 | (파일쓰기) + `recompile` | 비포커스에서도 강제 리컴파일. 즉시 반환 |
| ③ 검증 | `recompile_status` 폴링 | `{ status: idle\|compiling\|completed\|up_to_date, failed, errors[] }` |
| (Phase 2 훅) | `eval "<C#>"` | Roslyn 즉시 실행 → 플레이모드 assert 용 (`UnityEditorTarget.EvalAsync`) |

**결정된 열린 질문(§7)**
- 편집 형식 → **전체 파일 덮어쓰기**(diff 아님)로 확정. `FileEdit(RelativePath, Content)`.
- 검증 범위 → Phase 1 은 **컴파일만**. 런타임 assert 는 `eval` 훅으로 Phase 2.

---

## 7. 열린 질문 (나중에 결정)
- 편집 형식: 전체 파일 덮어쓰기 vs 부분 패치(diff)? (Phase 1은 전체 덮어쓰기로 단순하게)
- 플레이모드 검증을 어디까지? (컴파일만 → 런타임 assert → 시나리오 재생)
- 승인(HITL): 파일 쓰기/플레이모드 진입 전 사람 확인을 넣을지(안전) vs 완전 자동(속도).
- Skills 형식: 백엔드 무관 포터블 마크다운 vs Claude Code 전용 `.claude/skills`.
