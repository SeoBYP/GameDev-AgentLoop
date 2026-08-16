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
| **5** ✅ | 성능 프로파일링 검증 | "동작 정상 ≠ 충분히 빠름" — 핫패스 실측·예산(§6.9) |
| **6** ✅ | 테스트 러너·시나리오·시각 | 검증을 **자산**으로 — 레포에 남는 테스트, 다중 프레임, 결과 화면(§6.10) |

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

## 6.9 성능 프로파일링 검증 (Phase 5)

게임에서 "동작이 맞다"는 성공의 절반이다. 매 프레임 할당하는 컴포넌트는 **정확해도 실패작**이다.
그래서 검증에 층을 하나 더 얹었다: **③-c 성능 예산**.

**Phase 3 와의 차이 — 추측 vs 측정.**
Phase 3 스킬(`no-getcomponent-in-update` 등)은 소스를 **정적으로** 훑어 "나쁠 것 같은 패턴"을 잡는다.
값싸고 예방적이지만, 규칙에 없는 비용은 못 잡고 실제 비용도 모른다.
Phase 5 는 핫패스를 **실제로 5만 회 돌려 시간을 재고** 예산과 비교한다. 정적 검사가 놓친 것을 잡는다.

**측정 방법을 시간으로 정한 근거(실측).**
Unity Mono 는 **Boehm GC**(세대 없음)라 관리 힙 할당을 직접 재는 API 가 쓸모없었다:

| 시도한 API | 결과 |
|---|---|
| `GC.GetTotalAllocatedBytes` | 런타임에 **존재하지 않음**(컴파일 실패) |
| `GC.CollectionCount(0)` | 할당 경로에서도 **항상 0** |
| `GC.GetTotalMemory(false)` | 힙 크기라 수집이 따라잡으면 **0** |
| `Profiler.GetMonoUsedSizeLong()` | 절대값은 나오나 델타가 **0** |
| **`Stopwatch` 경과 시간** | ✅ 무할당 4.8ms vs 매 호출 할당 30.2ms (5만 회, **6배**) |

→ 할당 비용과 GC 압력은 결국 **시간에 반영된다.** 시간으로 재고, 보조 지표로
`get_performance_stats`(드로우콜·mono 메모리·cpuFrameTime)를 함께 기록한다.

**하네스는 오케스트레이터가 소유한다.**
백엔드는 `PERF` 블록으로 *무엇을(component/call) 몇 번(iterations) 어느 예산(maxTotalMs)에* 만 선언하고,
워밍업·타이머·정리 코드는 `PerfHarness` 가 만든다. 생성자가 자기 벤치마크를 느슨하게 쓰는 걸 막기 위해서다.
피드백에도 *"예산을 늘리지 말고 구현을 고쳐라"* 를 명시했다(ASSERT 와 같은 원칙).

**정직한 한계**
- 절대 ms 예산은 **기기 성능에 의존**한다. 프로파일링의 본질이며, 마진을 넓게 잡아 완화한다.
  (데모: 41ms/11.8ms 사이에 25ms — 처음 12ms 로 잡았다가 같은 코드가 13.9↔11.9 로 흔들려 플래키했다.)
- 에디터 안에서 재는 값이라 빌드 성능과 같지 않다. 상대 비교·회귀 감지용으로 보는 게 맞다.
- 프레임 단위 지표(드로우콜 등)는 예산 판정이 아니라 **진단 맥락**으로만 남긴다.

---

## 6.10 테스트 러너 · 시나리오 재생 · 시각 증거 (Phase 6)

레퍼런스([unity-cli-loop](https://github.com/hatayama/unity-cli-loop))의 검증 도구 아이디어를
우리 루프에 맞게 흡수한 단계다. 저쪽은 *AI 가 도구를 골라 루프를 도는* 구조라 접근이 다르지만,
**검증 수단** 자체는 배울 게 많았다.

### 왜 테스트 러너인가 — 검증을 자산으로

`ASSERT` 스니펫은 한 번 쓰고 버려진다. 반면 **테스트 파일은 레포에 남아** 다음 실행에서도 돈다.

| | ASSERT 스니펫 | 테스트 러너 |
|---|---|---|
| 수명 | 일회용(eval) | **레포에 남는 자산** |
| 회귀 방지 | 없음 | **누적** — 이전에 만든 것까지 매번 재검증 |
| 다중 프레임 | 불가(한 프레임 동기 실행) | **`[UnityTest]` + `yield return null`** |

→ 루프는 **테스트 파일이 오면 테스트 러너를 쓰고**(우선), 없으면 ASSERT 로 폴백한다.
별도 블록을 만들지 않았다 — 테스트는 그냥 `FILE:` 블록으로 오고, 루프가 경로(`/Tests/`)로 감지한다.

**실측 계약**: PlayMode 테스트는 플레이모드 진입 시 도메인 리로드가 HTTP 요청을 끊어 **동기 실행이 불가능**하다
(CLI 가 직접 그렇게 안내한다). `run_tests --async_tests` 로 시작하고 `test_status` 를 폴링해야 한다.

### 어셈블리 구조 — asmdef 가 필요했던 이유

Unity Test Framework 의 테스트 어셈블리(asmdef)는 **기본 어셈블리(`Assembly-CSharp`)를 참조할 수 없다.**
따라서 런타임 스크립트에도 asmdef 를 줘야 테스트가 그걸 볼 수 있다:

```
Assets/Scripts/AgentLoop.Runtime.asmdef     ← 생성물이 들어가는 런타임 어셈블리
Assets/Tests/PlayMode/AgentLoop.Tests.asmdef ← Runtime + TestRunner + nunit 참조
```

**부작용(실측으로 발견·해결)**: 도메인 리로드 직후엔 `eval` 의 컴파일 컨텍스트가 새 어셈블리를 아직 몰라
`The type or namespace name 'X' could not be found` 로 실패하는 창이 있다.
모델 잘못이 아니므로 `EvalWithRetryAsync` 가 **조용히 1회 재시도**한다.

### 시나리오 재생

별도 기능을 만들지 않았다 — **`[UnityTest]` 코루틴이 곧 시나리오 재생**이다.
생성 지침에 *"동작이 여러 프레임에 걸쳐 펼쳐지면(이동·쿨다운·타이머) `[UnityTest]` 로 검증하라"* 를 넣었고,
실제로 모델이 `while (target.IsMoving) yield return null;` 로 도착까지 프레임을 넘기는 테스트를 만들었다.

### 시각 증거 — 정직한 범위

`capture_game_view` 로 결과 화면을 PNG 로 남긴다(`--capture`). **판정에는 쓰지 않는다.**
기준 이미지 없이 "화면이 맞다"를 판정할 수 없기 때문이다. 다만 PNG 가 극단적으로 작으면
(사실상 균일한 화면) 경고를 붙인다.

**실측 제약**: `--save_path` 에 절대경로를 줘도 pipeline 이 **`Assets/` 아래로 가둔다**(authoring root).
그래서 Assets 밑 임시 폴더에 찍고 밖으로 옮긴 뒤 `.meta` 까지 정리한다.

---

## 6.11 컨텍스트 절약과 eval 안전 가드 (Phase 6 보강)

레퍼런스 분석에서 **우리 쪽 실제 결함**으로 드러난 두 가지를 메웠다.

### 컨텍스트 — 왜 "경로만 넘기기"를 쓸 수 없었나

레퍼런스(unity-cli-loop)는 큰 결과를 파일로 빼고 **경로만** AI 에게 준다. 우리는 그렇게 못 한다:
그쪽 AI 는 파일 읽기 도구를 가진 에이전트지만, **우리 백엔드는 도구 없는 순수 텍스트 생성기**(D1)라
경로를 줘도 읽지 못한다. 같은 문제를 우리 구조에 맞게 풀어야 했다.

**두 가지 성장 원인**
1. 출력 계약이 *"매번 전체 파일"* 이라 스텝마다 파일 전문이 히스토리에 쌓인다(가장 큼).
2. 컴파일 에러·테스트 실패 스택트레이스가 통째로 피드백에 들어간다.

**해법**
- **히스토리 윈도우** — 목표 턴 + 최근 N턴만 보낸다(기본 4). 과거 시도는 최신 전체 파일로 대체되므로 버려도 안전하다.
- **피드백 상한** — 항목 8개·항목당 400자로 자르고 `…그 외 N건 생략` 을 붙인다.
- **전체 원문은 파일로** — 사람이 볼 몫(`%TEMP%/agentloop-runs/<ts>/stepNN-*.log`). 모델에는 요약만 간다.

**실측**(6스텝, 계속 실패하는 케이스):

| 스텝 | 윈도우 OFF | 윈도우 ON |
|---|---|---|
| 1 | 6,403자 (턴 1) | 6,403자 (턴 1) |
| 3 | 8,441자 (턴 5) | 8,441자 (턴 5) |
| 6 | **11,570자 (턴 11)** — 선형 증가 | **8,489자 (턴 5)** — 평탄화 |

스텝당 증가가 사라진다. 데모 컴포넌트가 작아 27% 절감이지만, 실제 파일이 클수록 격차는 커진다.

### eval 안전 가드

검증용 ASSERT/PERF 스니펫은 에디터 안에서 **제한 없이** 실행된다 — 파일 삭제·프로세스 실행·네트워크가
문법적으로 가능하다. 실행 **전에** 위험 호출을 정적으로 걸러낸다(`SnippetGuard`):
파일 변경 · 디렉터리 조작 · 프로세스 · 네트워크 · 레지스트리 · 에셋 삭제 · 종료.

위반은 **모델 잘못**이므로 피드백으로 되돌린다(인프라 실패와 다르다). `--allow-unsafe-eval` 로 우회 가능.

**한계를 분명히 한다 — 샌드박스가 아니라 완화책이다.** 정적 문자열 검사라 리플렉션이나 문자열 조립으로
우회할 수 있다. 진짜 격리는 실행을 별도 프로세스·권한으로 분리해야 한다.
그럼에도 두는 이유는 흔한 사고를 값싸게 막고 **무엇이 금지인지 모델에게 알려주기** 위해서다.

**실측**: `File.Delete` 를 포함한 assert 를 주입하니 플레이모드 진입조차 하지 않고 차단됐고,
정상 assert 는 오탐 없이 통과했다.

---

## 6.12 입력 시뮬레이션과 eval 격리

### 입력 시뮬레이션 — 도구가 아니라 테스트 능력으로

레퍼런스는 `simulate-mouse`/`simulate-keyboard` 같은 **전용 도구**로 입력을 넣는다.
우리 pipeline 에는 그런 명령이 없다. 대신 **Input System 의 `InputTestFixture`** 를 쓰면
PlayMode 테스트 안에서 **가상 장치를 만들어 입력을 주입**할 수 있다.

우리 구조에는 이쪽이 더 맞다 — 입력 시뮬레이션이 **별도 검증 경로가 아니라 테스트가 할 수 있는 일**이 되고,
`[UnityTest]`(시나리오 재생)와 자연스럽게 합쳐진다: *누르고 → 프레임 넘기고 → 결과 확인*.

```
Assets/Tests/PlayMode/AgentLoop.Tests.asmdef
  references: Unity.InputSystem, Unity.InputSystem.TestFramework
Assets/Scripts/AgentLoop.Runtime.asmdef
  references: Unity.InputSystem
```

`InputTestFixture` 를 상속하면 `Press`/`Release`/`PressAndRelease`/`Set`/`SetTouch` 를 쓸 수 있고,
장치는 `InputSystem.AddDevice<Keyboard>()` 등으로 만든다. 생성 지침에 이 패턴을 예시로 넣었다.

**실측**: "스페이스바로 점프하고 0.5초 뒤 자동 착지" 목표 →
모델이 `JumperTests : InputTestFixture` 를 만들어 `AddDevice<Keyboard>()` → `Press(spaceKey)` →
프레임 진행 → 상태 확인. **step 1 에서 자동 착지 버그를 잡아(9/10) step 2 에 10/10 통과.**

### eval 격리 — 할 수 있는 것과 없는 것을 나눈다

**할 수 없는 것부터 분명히 한다.** `unity command eval` 은 **사용자의 에디터 프로세스 안에서** 실행된다.
그 프로세스는 우리가 만들지도, 소유하지도 않는다. 따라서 **오케스트레이터 코드로는 eval 을 샌드박싱할 수 없다.**
"프로세스 분리"를 구현했다고 말하는 건 거짓이 된다.

**할 수 있는 것 — 임시 코드 실행 자체를 없앤다.**
AI 가 만든 코드가 실행되는 경로는 두 갈래다:

| 경로 | 실행 형태 | 감사 가능성 |
|---|---|---|
| `RuntimeAssert`·`Performance` | **임시 스니펫**을 eval 로 즉시 실행 | 남지 않음 |
| `Tests` | **컴파일된 테스트 파일** | git 에 남고, diff 되고, 되돌릴 수 있음 |

→ **`--tests-only`**: eval 경로를 아예 쓰지 않는다. 테스트가 없으면 통과시키지 않고 요구한다.
이 모드에서 AI 가 만든 코드는 **전부 리뷰 가능한 파일**로만 실행된다.
(성능 단언이 필요하면 테스트 안에서 `Stopwatch` 로 재도록 지침에 명시)

**이건 격리가 아니라 감사 가능성이다.** 정직하게 구분해서 부른다:
- `SnippetGuard`(§6.11) — 위험 API 정적 차단. **완화책**(리플렉션 우회 가능).
- `--tests-only` — 임시 코드 실행 제거. **감사 가능성**(권한은 그대로).
- **진짜 격리는 배포의 몫** — 아래.

**진짜 격리 레시피(배포 수준)**: 에디터 프로세스가 가진 권한이 곧 blast radius다. 낮추려면
- 전용 OS 계정으로 Unity 를 실행(사용자 홈·문서 접근 차단),
- 또는 VM/컨테이너 안에서 프로젝트와 에디터를 함께 격리,
- 리포지토리는 그 안에 체크아웃하고 결과만 밖으로 가져온다.

오케스트레이터가 대신 해 줄 수 없는 부분이라 코드가 아니라 **운영 절차**로 문서화한다.

---

## 7. 열린 질문 (나중에 결정)
- ~~편집 형식: 전체 파일 덮어쓰기 vs 부분 패치(diff)?~~ → **전체 덮어쓰기로 확정**(§6.5).
- ~~플레이모드 검증을 어디까지?~~ → **컴파일 + 런타임 assert 까지 구현**(§6.6). 시나리오 재생은 남음.
- ~~Skills 형식: 포터블 마크다운 vs `.claude/skills`~~ → **포터블 마크다운으로 확정**(§6.7).
- 승인(HITL): 파일 쓰기/플레이모드 진입 전 사람 확인을 넣을지(안전) vs 완전 자동(속도).
  → 부분적으로 해소: 스킬 검사가 **적용 전 자동 게이트** 역할을 한다. 사람 확인은 여전히 미정.
- 검증 기준의 독립성: 지금은 기본적으로 생성자(AI)가 ASSERT 도 낸다. 기준을 사람/스펙에서
  분리해 오는 방법(테스트 파일, 수용 기준 DSL 등)은 미정.
