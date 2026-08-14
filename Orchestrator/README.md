# Orchestrator — 루프 소유자 (Phase 1)

> 생성 → 적용 → **검증** → 수리 → 반복. 이 닫힌 루프를 **C# 콘솔이 직접 소유**한다.
> AI 백엔드는 "텍스트 생성기"로만 쓰고, 적용·검증·재시도·판정은 전부 여기서 한다.

이 프로젝트의 핵심 주장은 *"코드를 생성한다"가 아니라 **"Unity에서 실제로 도는, 검증된 결과"** 를 만든다* 이다.
그 주장을 실현하는 실체가 바로 이 오케스트레이터다.

---

## 루프 5단계 (DESIGN.md §5)

```
목표(자연어)
 → ① 생성   IAgentBackend.CompleteAsync   (파일편집 + 런타임 ASSERT 스니펫 out)
   ①-b 검사  SkillLibrary.Inspect         (도메인 스킬 정적 검사 — 위반 시 적용 전 반려)
 → ② 적용   IExecTarget.ApplyAsync        (Assets/ 파일쓰기 + `recompile` 트리거)
 → ③ 검증   IExecTarget.VerifyAsync
       ③-a  컴파일       `recompile_status` 폴링 → 컴파일 에러?
       ③-b  런타임 동작   `editor_play` → `eval` 로 assert 실행 → `editor_stop`
       ③-c  성능 예산     핫패스 N회 실행 시간 실측 + 프로파일 통계 → 예산 초과?
 → ④ 피드백  에러/실패 사유를 대화(History)에 추가해 백엔드로 되돌림
 → ⑤ 판정   둘 다 통과 → 종료 / 실패 → ①로  (maxSteps 가드, 기본 6)
```

**③-b 가 이 프로젝트의 차별점이다.** 컴파일만 보면 "그럴듯하지만 안 도는 코드"를 통과시킨다.
플레이모드에서 실제로 실행해 보고(`Awake` 까지 도는 진짜 런타임) 의도대로 동작하는지 확인해야
비로소 *"검증된 결과"* 가 된다(D4).

두 축이 **pluggable** 이다:

| 축 | 인터페이스 | 구현 |
|---|---|---|
| 두뇌 | `IAgentBackend` | `ClaudeCodeBackend`(이 AI, 키 없음) · `CodexBackend`(Codex, 키 없음) · `ApiBackend`(API 키) · `ScriptedBackend`(데모) |
| 손 | `IExecTarget` | `UnityEditorTarget`(클라 — `unity` CLI) · `UgsTarget`(백엔드 — `ugs` CLI) |

네 백엔드가 **같은 `IAgentBackend` 계약**으로 루프에 동등하게 꽂힌다. 특히 서로 다른 두 CLI 에이전트
(**Claude Code · Codex**)가 루프 코드 변경 0으로 같은 루프를 도는 것으로 **agent-agnostic** 을 실증했다
— "백엔드는 텍스트 생성기, 루프가 결과물"(D1/D3)의 증거.

손도 둘이다. 손이 바뀌면 **만들 것(언어)도 검증 방법도 바뀐다**:

| | `--target unity` | `--target ugs` |
|---|---|---|
| 만드는 것 | Unity C# 컴포넌트 (`Assets/Scripts/`) | Cloud Code JS (`CloudCode/`) |
| 1차 검증 | 컴파일 (`recompile_status`) | **배포** (`ugs deploy`) |
| 런타임 검증 | 플레이모드 assert (`eval`) | 미지원 — CLI 에 호출 명령 없음 |

그래서 `IExecTarget` 은 "적용·검증"만 갖지 않고 **자기를 설명한다**:
`GenerationBrief`(이 손에 맞는 생성 규격) · `VerifyLabel`(1차 검증 이름) · `Supports(kind)`(가능한 검증) ·
`IsConnectedAsync`/`ConnectionHint`(사전 점검과 해결 안내). 덕분에 루프는 **형식만** 소유하고
언어·검증 방식은 손이 정한다. 조립 결과는 `--print-prompt` 로 그대로 볼 수 있다.

---

## 실행

> **공통 전제:** GameDev-AgentLoop 를 Unity 에디터에서 열어 `com.unity.pipeline` 서버를 띄운다.
> 확인: `unity pipeline list` → `서버 연결 가능: true`.

### 1) CLI 에이전트로 — 키 없이 (`--claude` / `--codex`)

별도 API 키 없이, **이미 로그인한 CLI 에이전트를 두뇌로** 루프를 돌린다.

```bash
dotnet run --project Orchestrator -- --claude "간단한 HP 컴포넌트를 만들어줘"           # Claude Code (기본 sonnet)
dotnet run --project Orchestrator -- --codex --model gpt-5.5 "오브젝트 풀을 만들어줘"    # Codex
```

전제: 해당 CLI(`claude` / `codex`)가 PATH 에 있고 **로그인**돼 있어야 한다(만료 시 각 CLI 로 재로그인).
`--claude` 기본 모델은 `sonnet`. `--codex` 는 계정이 지원하는 모델을 `--model` 로 지정한다
(codex 설정 기본 모델이 CLI 버전보다 최신이면 지정 필요).

### 2) API 키로 — `ApiBackend`

```bash
# 키는 절대 커밋 금지 — 환경변수로만 (CLAUDE.md)
$env:ANTHROPIC_API_KEY = "sk-ant-..."      # PowerShell
dotnet run --project Orchestrator -- "간단한 HP 컴포넌트를 만들어줘"
```

### 3) 키 없이 배관 증명 — `--demo` / `--demo-play`

스크립트 백엔드로 루프를 결정적으로 보여준다(키 불필요).

```bash
dotnet run --project Orchestrator -- --demo         # 컴파일 자가수리: 세미콜론 누락 → 에러 → 수리 → 통과
dotnet run --project Orchestrator -- --demo-play    # 런타임 검증: 컴파일은 통과하나 동작이 틀린 코드 → 수리
dotnet run --project Orchestrator -- --demo-skills  # 품질 강제: 도메인 규칙 위반 → 적용 거부 → 수리
dotnet run --project Orchestrator -- --demo-perf    # 성능 실측: 동작은 맞으나 핫패스가 느린 코드 → 수리
```

### 도메인 스킬 — `--skills` / `--list-skills`

`Skills/*.md` 의 규칙을 시스템 프롬프트에 주입하고, 생성물에 정적 검사를 강제한다
(자세한 형식·효과는 [../Skills/README.md](../Skills/README.md)).

```bash
dotnet run --project Orchestrator -- --list-skills            # 로드된 스킬·검사 목록
dotnet run --project Orchestrator -- --claude "..."           # 스킬 적용(기본)
dotnet run --project Orchestrator -- --claude "..." --skills off   # 스킬 없이(대조용)
```

### 검증 기준을 사람이 지정 — `--assert`

AI 가 낸 `ASSERT` 블록 대신 **사람이 준 기준**으로 채점한다(AI 가 자기 코드를 자기 기준으로
느슨하게 채점하는 것을 막는 장치). 플레이모드에서 실행되는 C# 스니펫을 그대로 넘긴다:

```bash
dotnet run --project Orchestrator -- --claude "스태미나 컴포넌트" \
  --assert 'var go = new UnityEngine.GameObject(); var s = go.AddComponent<Stamina>(); s.Use(500); int c = s.Current; UnityEngine.Object.DestroyImmediate(go); return c == 0;'
```

### UGS Cloud Code 로 — `--target ugs`

같은 루프로 **백엔드(UGS Cloud Code)** 를 만들고 배포까지 검증한다.

```bash
# 전제: ugs CLI 설치(npm i -g ugs) + 서비스 계정 자격 + 프로젝트 지정
cp .env.example .env      # 값 채우기 (.gitignore 로 보호됨)
dotnet run --project Orchestrator -- --target ugs --claude "일일 보상 지급 스크립트"
```

자격 증명은 **`.env`** 로 관리한다. 오케스트레이터가 시작할 때 읽어 프로세스 환경변수로 올리고,
자식 프로세스(`ugs` CLI)가 그대로 상속하므로 **CLI 배포와 REST 호출이 같은 자격**을 쓴다.
값은 절대 로그에 남기지 않는다(적용된 **키 이름만** 출력). 이미 설정된 OS 환경변수가 있으면 그쪽이 우선.

| 변수 | 용도 |
|---|---|
| `UGS_CLI_SERVICE_KEY_ID` / `UGS_CLI_SERVICE_SECRET_KEY` | 서비스 계정 자격 (대시보드 → Administration → Service Accounts) |
| `UGS_CLI_PROJECT_ID` / `UGS_CLI_ENVIRONMENT_NAME` | 배포 대상 (인자 `--ugs-project-id`/`--ugs-env` 가 우선) |

필요한 역할: **Cloud Code Editor** + **Unity Environments Viewer** (대상 프로젝트에 부여).

인증·프로젝트가 준비되지 않았으면 **AI 를 부르기 전에** 안내와 함께 종료한다(`ConnectionHint`).
옵션: `--ugs-project-id` · `--ugs-env` · `--cloud-code-dir <경로>`(기본 `CloudCode/`).

공통 옵션: `--max-steps N` · `--project <경로>` · `--model <id>` · `--assert <C#>` ·
`--target unity|ugs` · `--print-prompt`(조립된 시스템 프롬프트 출력).

---

## 첫 마일스톤 결과 (실측)

### 이 AI(Claude Code)를 두뇌로 — 키 없이 (`--claude`)

![claude-code 백엔드 실행 스크린샷](../docs/images/claude-backend-run.png)

로그인 → 스모크 테스트(`OK`) → `--claude` 실행. 실제 AI가 생성한 `Health.cs` 를 루프가 Unity에 적용·리컴파일해
**1스텝 만에 컴파일 통과** — 별도 API 키 없이 동작한다. (생성된 코드는 클램프·이벤트까지 갖춘 실물: [../Assets/Scripts/Health.cs](../Assets/Scripts/Health.cs))

### 런타임 검증 — "컴파일 통과 ≠ 동작 정상" (`--demo-play`)

이 프로젝트의 존재 이유를 가장 잘 보여주는 실행이다. **컴파일은 멀쩡히 통과하지만 클램프가 빠진**
스태미나 컴포넌트를, 플레이모드 런타임 assert 가 잡아내고 수리한다:

```
──────── step 1/6 ────────
① 생성  → 파일 편집 1개
② 적용  → 1개 파일 적용 + 리컴파일 트리거: Assets/Scripts/Stamina.cs
③ 검증  → 컴파일 통과 ✅                      ← 여기서 멈추면 "성공"으로 보인다
③ 검증  → 플레이모드 진입, 런타임 assert 실행 (AI 생성)
③ 검증  → 플레이모드 assert 실패 ❌
      · Use(500) 후 Current 는 0 이어야 하는데 -400 였습니다.   ← 실제 런타임 관측값

──────── step 2/6 ────────
① 생성  → 파일 편집 1개
② 적용  → 1개 파일 적용 + 리컴파일 트리거: Assets/Scripts/Stamina.cs
③ 검증  → 컴파일 통과 ✅
③ 검증  → 플레이모드 진입, 런타임 assert 실행 (AI 생성)
③ 검증  → 플레이모드 assert 통과 ✅

✅ 성공 — 2스텝 만에 컴파일 + 런타임 동작 검증 통과
```

실제 AI 백엔드로도 동일하게 동작한다 — `--claude` 로 쿨다운 타이머를 생성하면, 모델이 **스스로
ASSERT 블록을 작성**하고 루프가 그걸 플레이모드에서 실행해 검증한다
(산출물 [../Assets/Scripts/CooldownTimer.cs](../Assets/Scripts/CooldownTimer.cs)).

### 품질 강제 — 도메인 스킬 (`--demo-skills`, `--skills off` 대조)

지침을 프롬프트로 주는 데 그치지 않고, **적용 전에 검사로 반려**한다:

```
──────── step 1/3 ────────
① 생성  → 파일 편집 1개
①-b 검사 → 스킬 위반 3건 ❌ (적용하지 않음)
      · [client-architecture/no-public-mutable-field] Follower.cs: public 필드를 노출하지 마세요...
      · [unity-performance/no-getcomponent-in-update]  Follower.cs: Update 계열에서 GetComponent 를... (문제 메서드: Update)
      · [unity-performance/no-debuglog-in-update]      Follower.cs: Update 계열에서 Debug.Log 를... (문제 메서드: Update)

──────── step 2/3 ────────
①-b 검사 → 스킬 통과 ✅ → ② 적용 → ③ 컴파일 ✅ → ③ 런타임 assert ✅
```

**대조 실험**(같은 목표·같은 모델, 스킬만 on/off — 산출물 차이):

| 스킬 규칙 | `--skills off` | 스킬 적용 |
|---|---|---|
| public 필드 금지 | `public float moveSpeed = 5f;` | `[SerializeField] private float _moveSpeed = 5f;` |
| 제곱근 회피 | `transform.position == target` | `(newPos - target).sqrMagnitude <= thresholdSqr` |
| 프로퍼티 반복 접근 | `transform.position` 3회 읽음 | 지역 변수 캐싱 |
| 상태 변화 통지 | 없음 | `event Action<Vector3> OnTargetChanged` |
| 성공/실패 반환 | `void SetDestination(...)` | `bool SetTargetPosition(...)` + 입력 검증 |

### 성능 프로파일링 — "동작 정상 ≠ 충분히 빠름" (`--demo-perf`)

동작 검증까지 통과한 코드도 **핫패스에서 매 호출 할당하면** 게임에선 실패다.
Phase 3 스킬이 이를 *정적으로 추측*한다면, 이 단계는 **실측**한다:

```
step 1  ③ 검증 → 컴파일 통과 ✅
        ③ 검증 → 플레이모드 assert 통과 ✅        ← 동작은 정확하다
        ③ 검증 → 성능 예산 초과 ❌  ScoreTracker: 50000회 41.03ms (호출당 0.82µs)
                                    [프로파일: drawCalls=24 mono=943MB cpuFrame=2.55ms]
step 2  ③ 검증 → 성능 예산 통과 ✅  ScoreTracker: 50000회 11.82ms (호출당 0.24µs)

✅ 성공 — 2스텝 만에 동작 + 성능까지 검증 통과
```

**측정 하네스는 오케스트레이터가 소유한다**(`PerfHarness`). 백엔드는 *무엇을 얼마나 부를지와 예산*만
`PERF` 블록으로 선언하고, 워밍업·타이머·정리 코드는 루프가 만든다 —
생성자가 자기 벤치마크를 느슨하게 써서 통과시키는 걸 막기 위해서다.
피드백에도 *"예산을 늘리지 말고 구현을 고쳐라"* 를 명시한다.

> **왜 시간으로 재나 (실측 근거)**: Unity Mono 는 Boehm GC(세대 없음)라
> `GC.CollectionCount`/`GetTotalAllocatedBytes` 가 없거나 항상 0 이고, `GetTotalMemory` 도 힙 크기라
> 수집이 따라잡으면 0 으로 보인다. 반면 **시간은 안정적으로 드러난다** —
> 같은 일을 하는 두 구현을 5만 회 돌렸을 때 무할당 4.8ms vs 매 호출 할당 30.2ms(6배).
> 할당 비용과 GC 압력이 결국 시간에 반영된다.
>
> **한계(정직히)**: 절대 ms 예산은 기기 성능에 의존한다. 프로파일링의 본질적 성격이며,
> 데모는 양쪽(41ms / 11.8ms)에 충분한 마진을 두고 예산(25ms)을 잡았다.
> 처음엔 12ms 로 잡았다가 같은 코드가 13.9ms↔11.9ms 로 흔들려 플래키했고, 그 경험으로 마진을 넓혔다.

### agent-agnostic — Codex 도 같은 루프로

`--codex`(gpt-5.4-mini)로 **오브젝트 풀**을 생성 → 같은 루프가 적용·검증 → **1스텝 통과**.
서로 다른 두 CLI 에이전트가 **루프 코드 변경 0**으로 동작한다(백엔드 교체 가능성의 실증):

- Claude Code → [../Assets/Scripts/Health.cs](../Assets/Scripts/Health.cs)
- Codex → [../Assets/Scripts/ObjectPool.cs](../Assets/Scripts/ObjectPool.cs)

### 배관 증명 — `--demo`

`--demo` 실행 로그 — **2스텝 만에 자가수리로 컴파일 통과**:

```
──────── step 1/6 ────────
① 생성  → 파일 편집 1개
①-b 검사 → 스킬 통과 ✅
② 적용  → 1개 파일 적용 + 리컴파일 트리거: Assets/Scripts/DemoHealth.cs
③ 검증  → 컴파일 에러 1건 ❌
      · Assets\Scripts\DemoHealth.cs(5,44): error CS1002: ; expected

──────── step 2/6 ────────
① 생성  → 파일 편집 1개
①-b 검사 → 스킬 통과 ✅
② 적용  → 1개 파일 적용 + 리컴파일 트리거: Assets/Scripts/DemoHealth.cs
③ 검증  → 컴파일 통과 ✅

✅ 성공 — 2스텝 만에 컴파일 통과 (런타임 assert 없음)
```

에러 문자열(`DemoHealth.cs(5,44): error CS1002`)은 **실제 실행 중인 Unity 에디터의 Roslyn 컴파일러**에서
나온 것이다. 루프가 그걸 읽어 다음 스텝 맥락에 넣고, 백엔드가 고친다. 이게 "검증이 1급 시민"(D4)의 실체다.

---

## 파일 구성 — 무엇을·왜

```
Orchestrator/
├─ Program.cs              진입점: 인자 파싱, 프로젝트 루트 해석, 백엔드/타깃 조립, 루프 실행
├─ Contracts/             ── 두 축이 공유하는 "얇은 계약" (교체 가능성의 핵심)
│  ├─ IAgentBackend.cs        두뇌 계약: "맥락 주면 응답" 하나뿐
│  ├─ IExecTarget.cs          손 계약: 적용 + 검증
│  └─ LoopModels.cs           Turn/AgentContext/FileEdit/AgentReply/VerifyResult ...
├─ Backends/              ── IAgentBackend 구현들
│  ├─ ClaudeCodeBackend.cs    `claude -p` headless (키 없음) — 이 AI 를 두뇌로
│  ├─ CodexBackend.cs         `codex exec` headless (키 없음) — agent-agnostic 증명
│  ├─ ApiBackend.cs           Anthropic Messages API 직통 (HttpClient, 의존성 0)
│  └─ ScriptedBackend.cs      미리 정한 응답 재생 → 키 없이 루프 증명(--demo/--demo-play)
├─ Targets/              ── IExecTarget 구현들 (손)
│  ├─ UnityEditorTarget.cs    클라 — 적용·컴파일검증 + 플레이모드 런타임 assert
│  └─ UgsTarget.cs            백엔드 — Cloud Code JS 적용 + `ugs deploy` 배포 검증
├─ Skills/               ── 도메인 지식 레이어 (Phase 3)
│  ├─ Skill.cs               스킬·검사·위반 모델
│  ├─ SkillLibrary.cs        Skills/*.md 로드·선택·지침 생성·정적 검사 실행
│  └─ CSharpSource.cs        메서드 몸통 추출(중괄호 매칭) — 스코프 한정 검사용
├─ Loop/                 ── 루프 그 자체
│  ├─ AgentLoop.cs            5단계 오케스트레이션 ("루프는 우리 것"의 실체) + 출력 계약
│  └─ LoopOptions.cs          maxSteps 가드 · 사람 지정 assert · 결과 타입
└─ Util/
   ├─ ProcessRunner.cs        외부 CLI 실행 + stdout/stderr 캡처 (+ stdin 전달)
   ├─ PromptText.cs           AgentContext → headless 한 방 프롬프트 (CLI 백엔드 공용)
   └─ EditParser.cs           응답 텍스트 → FileEdit(FILE:) + 런타임 assert(ASSERT:) 파싱
```

### 왜 이렇게 나눴나 (설계 결정 → 코드)

- **`IAgentBackend` 가 극단적으로 얇은 이유 (D1)** — 백엔드는 "맥락 → 텍스트" 계약만 갖는다.
  Claude Code의 자체 에이전트 루프에 위임하지 않는 것이 핵심이다. 그래야 백엔드가 *진짜* 교체 가능해지고
  (API↔CLI 비교), 루프 자체가 결과물(포폴 가치)이 된다. `ScriptedBackend` 가 `ApiBackend` 와
  완전히 동등하게 루프에 꽂히는 것이 이 얇음의 증거다.

- **`ApiBackend` 가 SDK 대신 `HttpClient` 직통인 이유 (D3)** — Phase 1 백엔드는 제일 단순·확정적이어야
  "버그가 루프에 있음"이 명확하다. 의존성 0 · 와이어 포맷 투명 · 버전 드리프트 없음. 포폴 리뷰어가 전부 읽을 수 있다.

- **`UnityEditorTarget` 이 이 프로젝트의 차별점 (D4)** — 대부분의 에이전트 데모가 여기서 멈춘다("코드 생성").
  이 타깃은 `com.unity.pipeline` 이 여는 로컬 서버에 `unity command` 로 붙어, 실행 중인 에디터에서
  **재컴파일을 시키고 그 결과(에러 목록)를 되받아 오고**, 나아가 **플레이모드에 진입해 실제로 실행**한다.
  검증이 있어야 자가수리가 가능하고, 런타임 검증이 있어야 "컴파일만 되는 코드"를 걸러낼 수 있다.

- **런타임 검증 기준은 누가 정하나 (정직한 한계)** — 기본값은 백엔드가 `ASSERT` 블록으로 스스로 낸다.
  편하지만 **AI 가 자기 코드를 자기 기준으로 채점**하는 구조라, 기준이 느슨하면 통과가 쉬워진다.
  그래서 (a) 피드백에 *"assert 를 느슨하게 바꾸지 말고 구현을 고쳐라"* 를 명시하고,
  (b) 사람이 `--assert` 로 **권위 있는 기준을 주입**하면 AI 의 것을 덮어쓰도록 했다.
  진짜 해결은 기준을 생성자와 분리하는 것(예: 사람이 쓴 스펙/테스트) — 그건 Phase 3 의 몫이다.

- **`EditParser` 가 백엔드가 아니라 Util 에 있는 이유** — 출력 형식(`FILE:` + 펜스)은 백엔드 무관이다
  (시스템 프롬프트로 강제). 그래서 모든 백엔드가 같은 파서를 재사용한다.

전체 설계·근거(Decision Log)는 [../docs/DESIGN.md](../docs/DESIGN.md) 참고.

---

## Unity 연동 상세 — `com.unity.pipeline` 명령 매핑

`UnityEditorTarget` 이 실제로 쓰는 pipeline(0.4.0-exp.1) 명령:

| 단계 | 명령 | 반환 |
|---|---|---|
| ② 적용 | (직접 파일쓰기) + `unity command recompile` | 리컴파일 트리거(비포커스에서도 동작) |
| ③-a 컴파일 | `unity command recompile_status` 폴링 | `{ status: idle\|compiling\|completed\|up_to_date, failed: bool, errors: [] }` |
| ③-b 런타임 | `editor_status` → `editor_play` → `eval "<C#>"` → `editor_stop` | 준비상태 확인 → 진입 → Roslyn 즉시 실행 → 복귀 |

`--json` 봉투: `{ success, command, data: { result, success, ... }, errors, warnings }`.
`recompile_status` 의 `data.result` 는 JSON **문자열**이라 한 번 더 파싱한다(코드 주석 참고).

**플레이모드 검증의 함정 두 가지** (실측으로 확인하고 코드에 반영):
1. **에디트 모드에선 `Awake` 가 안 돈다.** `AddComponent<T>()` 만으론 초기화가 안 되므로
   진짜 `editor_play` 진입이 필요하다. 그래야 `Awake` 가 돌고 실제 런타임 상태를 검증할 수 있다.
2. **리컴파일 직후엔 진입이 거부된다.** 도메인 리로드 중이면 `editor_play` 가 조용히 무시된다.
   → `editor_status` 의 `status:"ready"` / `compiling` / `domainReloadInProgress` 를 보고 기다린 뒤 진입하고,
   실패 시 1회 재시도한다. 그래도 실패하면 **모델에게 피드백하지 않고** 인프라 오류로 중단한다
   (인프라 문제를 "네 코드가 틀렸다"로 되돌리면 멀쩡한 코드를 고치다 스텝을 낭비한다).

에디터는 검증이 실패하든 취소되든 `finally` 에서 반드시 `editor_stop` 으로 복귀시킨다(부작용 방지).

---

## 확장 지점 (다음 단계)

- **새 백엔드** — `IAgentBackend` 구현 1개 추가 + `Program.cs` 에서 선택 분기.
  `ClaudeCodeBackend` 는 `claude -p`(headless) 를 `ProcessRunner` 로 호출해 1회 응답만 받으면 된다.
- **새 타깃** — `IExecTarget` 구현 1개 추가(자기 생성 규격·검증 방식·사전점검을 스스로 설명하면 된다).
- **UGS 호출 검증** — 현재 `ugs` CLI 에는 스크립트 호출 명령이 없어 배포까지만 검증한다.
  호출까지 보려면 Cloud Code REST 엔드포인트를 플레이어 토큰으로 직접 부르는 경로가 필요하다.
- **검증 강화(계속)** — 지금은 컴파일 + 플레이모드 assert. 다음은 시나리오 재생
  (여러 프레임에 걸친 동작), 성능 예산(`get_performance_stats`), 테스트 러너(`run_tests`) 연동 등.
