# Orchestrator — 루프 소유자 (Phase 1)

> 생성 → 적용 → **검증** → 수리 → 반복. 이 닫힌 루프를 **C# 콘솔이 직접 소유**한다.
> AI 백엔드는 "텍스트 생성기"로만 쓰고, 적용·검증·재시도·판정은 전부 여기서 한다.

이 프로젝트의 핵심 주장은 *"코드를 생성한다"가 아니라 **"Unity에서 실제로 도는, 검증된 결과"** 를 만든다* 이다.
그 주장을 실현하는 실체가 바로 이 오케스트레이터다.

---

## 루프 5단계 (DESIGN.md §5)

```
목표(자연어)
 → ① 생성   IAgentBackend.CompleteAsync   (텍스트/파일편집 out)
 → ② 적용   IExecTarget.ApplyAsync        (Assets/ 파일쓰기 + `recompile` 트리거)
 → ③ 검증   IExecTarget.VerifyAsync       (`recompile_status` 폴링 → 컴파일 에러?)
 → ④ 피드백  에러를 대화(History)에 추가해 백엔드로 되돌림
 → ⑤ 판정   통과 → 종료 / 실패 → ①로  (maxSteps 가드, 기본 6)
```

두 축이 **pluggable** 이다:

| 축 | 인터페이스 | 지금 | 나중 |
|---|---|---|---|
| 두뇌 | `IAgentBackend` | `ClaudeCodeBackend`(**이 AI, 키 없음**) · `ApiBackend`(API 키) · `ScriptedBackend`(데모) | `CodexBackend` |
| 손 | `IExecTarget` | `UnityEditorTarget`(`unity` CLI) | `UgsTarget`(`ugs` CLI) |

세 백엔드가 **같은 `IAgentBackend` 계약**으로 루프에 동등하게 꽂힌다 — "백엔드는 텍스트 생성기, 루프는 우리 것"(D1)의 증거.

---

## 실행

> **공통 전제:** GameDev-AgentLoop 를 Unity 에디터에서 열어 `com.unity.pipeline` 서버를 띄운다.
> 확인: `unity pipeline list` → `서버 연결 가능: true`.

### 1) 이 AI(Claude Code)로 — 키 없이 (`--claude`)

별도 API 키 없이, **이미 로그인한 Claude Code CLI 를 두뇌로** 루프를 돌린다.

```bash
dotnet run --project Orchestrator -- --claude "간단한 HP 컴포넌트를 만들어줘"
```

전제: `claude` CLI 가 PATH 에 있고 **로그인**돼 있어야 한다. 만료 시 `claude -p` 가 401 을 내므로
`claude` 를 한 번 실행해 재로그인한다. 모델은 기본 `sonnet`(`--model opus` 등으로 변경).

### 2) API 키로 — `ApiBackend`

```bash
# 키는 절대 커밋 금지 — 환경변수로만 (CLAUDE.md)
$env:ANTHROPIC_API_KEY = "sk-ant-..."      # PowerShell
dotnet run --project Orchestrator -- "간단한 HP 컴포넌트를 만들어줘"
```

### 3) 키 없이 배관 증명 — `--demo`

스크립트 백엔드로 **자가수리 루프의 배관**을 결정적으로 보여준다
(일부러 세미콜론 빠진 `Health.cs` → 컴파일 에러 → 고친 `Health.cs` → 통과).

```bash
dotnet run --project Orchestrator -- --demo
```

공통 옵션: `--max-steps N` · `--project <UnityProjectPath>` · `--model <id>`.

---

## 첫 마일스톤 결과 (실측)

`--demo` 실행 로그 — **2스텝 만에 자가수리로 컴파일 통과**:

```
목표: 간단한 Health(HP) 컴포넌트를 만들어줘. ...
백엔드: scripted:demo   타깃: unity:6000.5.4f1   maxSteps: 6

──────── step 1/6 ────────
① 생성  → 파일 편집 1개
② 적용  → 1개 파일 적용 + 리컴파일 트리거: Assets/Scripts/Health.cs
③ 검증  → 컴파일 에러 1건 ❌
      · Assets\Scripts\Health.cs(6,29): error CS1002: ; expected

──────── step 2/6 ────────
① 생성  → 파일 편집 1개
② 적용  → 1개 파일 적용 + 리컴파일 트리거: Assets/Scripts/Health.cs
③ 검증  → 컴파일 통과 ✅

✅ 성공 — 2스텝 만에 컴파일 통과
```

에러 문자열(`Assets\Scripts\Health.cs(6,29): error CS1002`)은 **실제 실행 중인 Unity 에디터의 Roslyn 컴파일러**에서 나온 것이다. 루프가 그걸 읽어 다음 스텝 맥락에 넣고, 백엔드가 고친다. 이게 "검증이 1급 시민"(D4)의 실체다.

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
│  ├─ ApiBackend.cs           Anthropic Messages API 직통 (HttpClient, 의존성 0)
│  └─ ScriptedBackend.cs      미리 정한 응답 재생 → 키 없이 루프 증명(--demo)
├─ Targets/              ── IExecTarget 구현들
│  └─ UnityEditorTarget.cs    `unity command recompile/recompile_status` 로 적용·검증
├─ Loop/                 ── 루프 그 자체
│  ├─ AgentLoop.cs            5단계 오케스트레이션 ("루프는 우리 것"의 실체)
│  └─ LoopOptions.cs          maxSteps 가드 + 결과 타입
└─ Util/
   ├─ ProcessRunner.cs        `unity` CLI 실행 + stdout/stderr 캡처
   └─ EditParser.cs           응답 텍스트 → FileEdit (FILE: + ```csharp 펜스 파싱)
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
  **재컴파일을 시키고 그 결과(에러 목록)를 되받아 온다.** 검증이 있어야 자가수리가 가능하다.

- **`EditParser` 가 백엔드가 아니라 Util 에 있는 이유** — 출력 형식(`FILE:` + 펜스)은 백엔드 무관이다
  (시스템 프롬프트로 강제). 그래서 모든 백엔드가 같은 파서를 재사용한다.

전체 설계·근거(Decision Log)는 [../docs/DESIGN.md](../docs/DESIGN.md) 참고.

---

## Unity 연동 상세 — `com.unity.pipeline` 명령 매핑

`UnityEditorTarget` 이 실제로 쓰는 pipeline(0.4.0-exp.1) 명령:

| 단계 | 명령 | 반환 |
|---|---|---|
| ② 적용 | (직접 파일쓰기) + `unity command recompile` | 리컴파일 트리거(비포커스에서도 동작) |
| ③ 검증 | `unity command recompile_status` 폴링 | `{ status: idle\|compiling\|completed\|up_to_date, failed: bool, errors: [] }` |
| (Phase 2) | `unity command eval "<C#>"` | Roslyn 즉시 실행 — 플레이모드 assert 훅으로 남겨둠 (`EvalAsync`) |

`--json` 봉투: `{ success, command, data: { result, success, ... }, errors, warnings }`.
`recompile_status` 의 `data.result` 는 JSON **문자열**이라 한 번 더 파싱한다(코드 주석 참고).

---

## 확장 지점 (다음 단계)

- **새 백엔드** — `IAgentBackend` 구현 1개 추가 + `Program.cs` 에서 선택 분기.
  `ClaudeCodeBackend` 는 `claude -p`(headless) 를 `ProcessRunner` 로 호출해 1회 응답만 받으면 된다.
- **새 타깃** — `IExecTarget` 구현 1개 추가. `UgsTarget` 은 `ugs` CLI 로 Cloud Code 배포·호출을 검증.
- **검증 강화** — `VerifySpec` 에 `PlayModeAssert` 등을 추가하고 `UnityEditorTarget.EvalAsync` 로
  런타임 상태를 확인(예: `new GameObject().AddComponent<Health>().TakeDamage(30)` 후 `Current==70` assert).
