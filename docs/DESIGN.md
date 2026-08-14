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
| **2** ✅ | CLI 백엔드(ClaudeCode/Codex) + 플레이모드 검증 | agent-agnostic 실증 + **런타임 동작 검증** — 달성(§6.5) |
| **3** ✅ | 도메인 Skills(최적화·아키텍처·함정) | 산출 코드 "품질" 대조 데모 — 달성(§6.7) |
| **4** ✅ | `UgsTarget`(Cloud Code 배포·호출 검증) | 실제 UGS 프로젝트로 **배포 + 호출 검증 관통**(§6.8) |

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
- 검증 범위 → 컴파일 + **플레이모드 런타임 assert** 까지 구현(§6.6). 시나리오 재생은 이후.

---

## 6.6 플레이모드 런타임 검증 (Phase 2 — 달성)

"컴파일 통과"는 성공 기준으로 약하다. 그럴듯하지만 **안 도는** 코드가 통과하기 때문이다.
그래서 검증을 두 단계로 나눴다:

| 단계 | 무엇을 보나 | 어떻게 |
|---|---|---|
| ③-a 컴파일 | 빌드되나 | `recompile` → `recompile_status` 폴링 |
| ③-b 런타임 | **의도대로 동작하나** | `editor_play` → `eval` 로 assert 실행 → `editor_stop` |

**검증 기준의 출처.** 백엔드가 출력 계약에 따라 `ASSERT:` 블록(플레이모드에서 실행되는 C# 스니펫,
통과 시 `"OK"` / 실패 시 사유 문자열 반환)을 함께 낸다. 사람이 `--assert` 로 주면 그쪽이 우선한다.
→ 한계 인정: 기본값은 **생성자가 채점자를 겸한다.** 느슨한 기준을 낼 유인이 있으므로,
피드백에 "assert 말고 구현을 고쳐라"를 명시하고, 사람 주입 경로를 열어 뒀다.
기준을 생성자와 완전히 분리하는 건 Phase 3(도메인 Skills·스펙)의 몫.

**실측으로 확인한 함정 두 가지 (코드에 반영)**
1. 에디트 모드에선 `Awake` 가 돌지 않는다 → 진짜 플레이모드 진입이 필요.
2. 리컴파일 직후엔 도메인 리로드 때문에 진입이 조용히 거부된다 →
   `editor_status` 의 `status:"ready"`/`compiling`/`domainReloadInProgress` 로 게이팅 후 진입, 1회 재시도.
   그래도 실패하면 **모델에 피드백하지 않고** 인프라 오류로 중단한다
   (인프라 실패를 "코드가 틀렸다"로 되돌리면 멀쩡한 코드를 고치며 스텝을 낭비한다).

에디터는 `finally` 에서 반드시 `editor_stop` — 검증이 실패해도 플레이모드에 남지 않는다.

---

## 6.7 도메인 Skills (Phase 3 — 달성)

컴파일·런타임 검증은 *"도는가"* 를 본다. 하지만 도는 코드에도 **품질** 차이가 있다
(Update 안 탐색, public 필드 남발, 매 프레임 할당…). 그걸 강제하는 레이어.

**형식은 포터블 마크다운 — §7 열린 질문 결정.**
`.claude/skills` 같은 특정 CLI 전용 포맷은 Codex/API 백엔드에서 안 먹어 D1(백엔드 교체 가능)을 깬다.
따라서 스킬은 **오케스트레이터가 소유**하고 모든 백엔드에 동일하게 적용한다.

스킬 하나 = `.md` 하나이고 두 부분을 갖는다:

| 섹션 | 역할 | 루프에서 |
|---|---|---|
| `GUIDANCE` | 지켜야 할 규칙(산문) | 시스템 프롬프트에 주입 → **예방** |
| `CHECKS` | 정적 검사(스코프 + 금지 정규식) | ①-b 에서 실행 → 위반 시 **적용 전 반려** |

**지침만으론 권고, 검사가 붙어야 강제.** 그래서 검사를 1급으로 뒀다.
검사는 파일을 프로젝트에 쓰기 **전에** 돌아, 위반 코드는 Unity 를 건드리지도 못한다(빠른 실패).

**실측 대조** (같은 목표·모델, 스킬만 on/off):
`--skills off` 는 `public float moveSpeed`, `transform.position` 3회 반복 접근, void 반환을 냈고,
스킬 적용 시 `[SerializeField] private`, `sqrMagnitude` 비교, 지역변수 캐싱, 이벤트 통지,
`bool` 반환 + 입력 검증으로 바뀌었다. 검사 반려 경로는 `--demo-skills`(위반 3건 → 적용 거부 → 수리).

**한계**: 검사는 정규식 + 중괄호 매칭 수준이라 국소 규칙만 잡는다(의존성 0 유지가 목적).
구문 수준 규칙이 필요해지면 Roslyn 분석기로 승격하면 된다.

---

## 6.8 UgsTarget — 두 번째 손 (Phase 4)

손을 바꾸면 같은 루프가 **클라(Unity)** 대신 **백엔드(UGS Cloud Code)** 를 만든다. 두뇌가 넷이 된 데 이어
손이 둘이 되면서 "두 축이 pluggable"(D2/D5)이 코드로 완성된다.

**손이 바뀌면 바뀌는 것**

| | `UnityEditorTarget` | `UgsTarget` |
|---|---|---|
| 산출물 | C# 컴포넌트 (`Assets/Scripts/`) | Cloud Code JS (`CloudCode/`) |
| 1차 검증 | 컴파일 (`recompile_status`) | **배포** (`ugs deploy`, publish 포함) |
| 런타임 검증 | 플레이모드 assert (`eval`) | **스크립트 호출** (Cloud Code REST) |
| assert 형식 | C# 스니펫 (`"OK"` 반환) | JSON 호출 명세 (응답 부분일치) |

**그래서 `IExecTarget` 이 자기를 설명하게 했다.** 초안(§4)의 인터페이스는 `Apply`/`Verify` 뿐이었지만,
타깃마다 *만들 언어*와 *가능한 검증*이 달라 루프가 그걸 알아야 했다:
`GenerationBrief`(생성 규격) · `VerifyLabel` · `Supports(kind)` · `IsConnectedAsync`/`ConnectionHint`.
→ 시스템 프롬프트 = **[루프의 형식 계약] + [손의 생성 규격] + [스킬의 품질 지침]** 으로 조립된다
(`--print-prompt` 로 확인). 루프는 형식만 소유한다.

**호출 검증 — CLI 에 없어서 REST 로 직접 붙였다.**
`ugs cloud-code scripts` 에는 create/publish/get/list/update/delete 만 있고 **invoke/run 이 없다.**
그래서 `UgsInvoker` 가 서비스 계정 → 토큰 교환 → Cloud Code Client API 호출을 직접 수행한다
(설계·엔드포인트: **[docs/UGS-INVOKE-DESIGN.md](UGS-INVOKE-DESIGN.md)**). 의존성 0 원칙에 따라 `HttpClient` 직통.
assert 는 JS 스니펫이 아니라 **선언적 JSON 호출 명세**이고, 응답은 **부분 일치**로 비교한다
(부가 필드로 검증이 깨지지 않게).

**실측(실제 UGS 프로젝트)**: `--target ugs --claude` 로 레벨업 보상 스크립트를 생성 →
배포 → 호출 검증까지 **1스텝 통과**. 배포된 스크립트를 REST 로 독립 재확인했다
(`level=60 → coins 3000`, `level=100 → 3000 클램프`, `level=0 → 거부`).

**실측으로 얻은 함정 4가지 (코드·지침에 반영)**
1. `--services` 값은 `cloud-code` 가 아니라 **`cloud-code-scripts`**. 틀리면 조용히 "No content deployed".
2. **게시 권한은 별도** — `Cloud Code Editor` 로는 부족하고 `Cloud Code Publisher` 가 필요하다.
3. 권한 실패(403)로 중단되면 **반쯤 생성된 스크립트 레코드**가 남아 이후 배포가 500 을 낸다. 삭제 후 재배포.
4. **`module.exports.params` 를 선언하지 않으면 파라미터가 걸러진다** — 배포는 성공하는데 호출하면
   `params` 가 비어 와 로직이 틀린다. *"배포 성공 ≠ 동작 정상"* 의 실사례이고,
   호출 검증이 없으면 절대 못 잡는다. → `GenerationBrief` 에 선언 필수로 명시.
5. `ugs env list` 는 `--environment-name` 을 받지 않는다(주면 도움말만 출력) — 명령별로 플래그를 가려서 붙인다.

**검증 범위(현재)**: 파일 적용 · `ugs deploy` 호출/결과 파싱 · 타깃별 프롬프트 조립 · 타깃별 스킬 필터 ·
미인증 시 사전 차단까지 실측 확인. **실제 배포 성공 경로**는 서비스 계정 키가 필요해 사용자 설정 후 확인한다
(비밀키는 오케스트레이터가 다루지 않는다 — `ugs login` 또는 환경변수로 CLI 가 보관).

---

## 7. 열린 질문 (나중에 결정)
- ~~편집 형식: 전체 파일 덮어쓰기 vs 부분 패치(diff)?~~ → **전체 덮어쓰기로 확정**(§6.5).
- ~~플레이모드 검증을 어디까지?~~ → **컴파일 + 런타임 assert 까지 구현**(§6.6). 시나리오 재생은 남음.
- ~~Skills 형식: 포터블 마크다운 vs `.claude/skills`~~ → **포터블 마크다운으로 확정**(§6.7).
- 승인(HITL): 파일 쓰기/플레이모드 진입 전 사람 확인을 넣을지(안전) vs 완전 자동(속도).
  → 부분적으로 해소: 스킬 검사가 **적용 전 자동 게이트** 역할을 한다. 사람 확인은 여전히 미정.
- 검증 기준의 독립성: 지금은 기본적으로 생성자(AI)가 ASSERT 도 낸다. 기준을 사람/스펙에서
  분리해 오는 방법(테스트 파일, 수용 기준 DSL 등)은 미정.
