# GameDev-AgentLoop

> AI가 짠 Unity 코드가 **정말 도는지, 잘 만들었는지, 충분히 빠른지**를
> 실제로 실행해 검증하고 스스로 고치는 **닫힌 루프**.

대부분의 AI 코딩 데모는 "코드를 생성했다"에서 끝난다. 하지만 게임 개발에서 진짜 질문은 그다음이다 —
*컴파일은 되나? 의도대로 동작하나? 프레임 예산 안에 드나?* 이 프로젝트는 그 질문들을
**측정으로 답하고, 실패하면 되먹여 고치는** 오케스트레이터다.

![Claude Code를 백엔드로 — API 키 없이 생성→적용→컴파일 검증까지 1스텝 통과](docs/images/claude-backend-run.png)

---

## 이 프로젝트가 실제로 증명한 것

각 단계는 **한 겹씩 더 깊은 "성공의 정의"** 를 강제한다. 모두 실행 로그로 실측했다.

| 검증 층 | 통과해도 놓치는 것 | 어떻게 잡나 |
|---|---|---|
| ③-a **컴파일** | *"그럴듯한데 안 도는 코드"* | `unity command recompile_status` 로 실제 컴파일 에러 수집 |
| ③-b **런타임 동작** | *"컴파일은 되는데 로직이 틀린 코드"* | **Unity Test Runner** 로 실행(없으면 플레이모드 `eval` assert) |
| ③-b′ **시나리오·입력** | *"한 프레임만 보면 맞아 보이는 코드"* | **`[UnityTest]` 코루틴** + **가상 입력 주입**(`InputTestFixture`)으로 조작 시나리오 검증 |
| ③-c **성능 예산** | *"동작은 맞는데 매 프레임 할당하는 코드"* | 핫패스를 5만 회 돌려 **경과 시간 실측** |
| ③-c′ **렌더 예산** | *"빠른데 드로우콜을 폭증시키는 코드"* | 씬에 남기는 **드로우콜·삼각형 증가분 실측** |
| ①-b **도메인 품질** | *"돌지만 잘못 만든 코드"* | 스킬 정적 검사로 **적용 전 반려** |

검증은 **자산으로 남는다** — AI가 구현과 함께 PlayMode 테스트를 작성하고,
그 테스트는 레포에 남아 이후 실행마다 회귀를 잡는다. 결과 화면도 `--capture` 로 증거로 남길 수 있다.

### 실측 로그 — "동작 정상 ≠ 충분히 빠름"

```
step 1  ③ 컴파일 통과 ✅
        ③ 플레이모드 assert 통과 ✅     ← 동작은 정확하다
        ③ 성능 예산 초과 ❌  ScoreTracker: 50000회 41.03ms (호출당 0.82µs)
                             [프로파일: drawCalls=24 mono=943MB cpuFrame=2.55ms]
step 2  ③ 성능 예산 통과 ✅  ScoreTracker: 50000회 11.82ms (호출당 0.24µs)

✅ 성공 — 2스텝 만에 동작 + 성능까지 검증 통과
```

핫패스에서 매 호출 `List`를 새로 만들던 구현이 **동작 검증은 통과**했지만 성능 실측에서 걸렸고,
버퍼 재사용으로 고쳐 **3.5배** 빨라졌다. 정적 분석의 "추측"이 아니라 **측정**이다.

---

## 아키텍처 — 두 축이 pluggable

```
        ┌──────────── Orchestrator (C#) : 루프 소유 ────────────┐
목표 →  │  ①생성 → ①-b품질 → ②적용 → ③검증(a·b·c) → ④피드백 → ⑤판정  │
        └──┬──────────────────────────────────┬────────────────┘
     IAgentBackend (두뇌)                IExecTarget (손)
   ├ ClaudeCodeBackend  키 없음        ├ UnityEditorTarget  클라 (unity CLI)
   ├ CodexBackend       키 없음        └ UgsTarget          백엔드 (ugs CLI + REST)
   ├ ApiBackend         API 키
   └ ScriptedBackend    데모
```

**루프는 우리 것이다.** AI 백엔드는 "맥락 → 텍스트" 계약만 갖는 순수 생성기로 쓰고,
적용·검증·재시도·판정은 오케스트레이터가 소유한다. 그래서 백엔드가 **진짜로 교체 가능**하다.

- **agent-agnostic 실증** — Claude Code와 Codex, 서로 다른 두 CLI 에이전트가
  **루프 코드 변경 0**으로 같은 루프를 돌았다.
- **손이 바뀌면 만들 것도 검증도 바뀐다** — Unity 타깃은 C#을 만들어 컴파일·플레이모드로 검증하고,
  UGS 타깃은 Cloud Code JS를 만들어 배포·REST 호출로 검증한다.
  그래서 `IExecTarget`이 자기 생성 규격과 가능한 검증을 **스스로 선언**한다(`--print-prompt`로 확인).

| | `--target unity` | `--target ugs` |
|---|---|---|
| 산출물 | C# 컴포넌트 (`Assets/Scripts/`) | Cloud Code JS (`CloudCode/`) |
| 1차 검증 | 컴파일 | **배포** (`ugs deploy`) |
| 런타임 검증 | 플레이모드 assert | **스크립트 호출** (Cloud Code REST) |
| 성능 검증 | 핫패스 시간 실측 | — |

---

## 빠른 실행

**전제**: 이 프로젝트를 Unity 에디터에서 열어 pipeline 서버를 띄운다
(`unity pipeline list` → `서버 연결 가능: true`).

```bash
# 키 없이 루프 증명 — 네 가지 실패 유형을 각각 재현·수리한다
dotnet run --project Orchestrator -- --demo         # 컴파일 에러 → 자가수리
dotnet run --project Orchestrator -- --demo-play    # 컴파일은 통과하나 동작이 틀림
dotnet run --project Orchestrator -- --demo-skills  # 도메인 규칙 위반 → 적용 거부
dotnet run --project Orchestrator -- --demo-perf    # 동작은 맞으나 성능 예산 초과
dotnet run --project Orchestrator -- --demo-draw    # 빠르지만 드로우콜 폭증 → 렌더 예산 초과

# 실제 AI 로 (API 키 불필요 — CLI 로그인만)
dotnet run --project Orchestrator -- --claude "간단한 HP 컴포넌트를 만들어줘"

# 테스트까지 생성시키고(레포에 남음) 결과 화면도 캡처
dotnet run --project Orchestrator -- --claude --capture \
  "목표 지점으로 이동하는 컴포넌트. 여러 프레임에 걸쳐 도착하는지 [UnityTest] 로 검증해줘"

# 임시 코드 실행 없이 — 컴파일된 테스트 파일로만 검증(감사 가능성)
dotnet run --project Orchestrator -- --claude --tests-only \
  "스페이스바로 점프하는 컴포넌트. 가상 키보드 입력으로 검증해줘"
dotnet run --project Orchestrator -- --codex --model gpt-5.5 "오브젝트 풀을 만들어줘"

# 백엔드(UGS Cloud Code)를 타깃으로
dotnet run --project Orchestrator -- --target ugs --claude "일일 보상 지급 스크립트"
```

---

## 기술 스택 / 전제 도구

| 도구 | 용도 |
|---|---|
| **.NET 10** | 오케스트레이터(C# 단일 스택, **의존성 0**) |
| **Unity 6 LTS** + `com.unity.pipeline` | 실행 중 에디터 제어 — 리컴파일·플레이모드·`eval`·프로파일 통계 |
| **Unity CLI** (`unity`) | 에디터와 통신하는 로컬 서버 브리지 |
| **UGS CLI** (`ugs`) + Cloud Code REST | 백엔드 배포·호출 검증 |
| **Claude Code / Codex CLI** | 두뇌(headless 1회 응답) |

의존성 0을 유지한 이유: Anthropic 호출도 `HttpClient` 직통이라 와이어 포맷이 투명하고,
"버그가 루프에 있음"이 명확하다. 리뷰어가 전부 읽을 수 있는 크기를 지향했다.

---

## 구조

| 경로 | 내용 |
|---|---|
| [Orchestrator/](Orchestrator/README.md) | 루프 소유자 — 계약·백엔드·타깃·검증. **여기부터 보면 된다** |
| [Skills/](Skills/README.md) | 도메인 지식 레이어 — 지침(예방) + 정적 검사(강제) |
| [docs/DESIGN.md](docs/DESIGN.md) | 설계와 근거(Decision Log) — *왜* 이렇게 정했는지 |
| [docs/WORKLOG.md](docs/WORKLOG.md) | 작업 로그 — 무엇을 하며 무엇에 부딪혔는지 |
| [docs/UGS-INVOKE-DESIGN.md](docs/UGS-INVOKE-DESIGN.md) | UGS 호출 검증 설계·실측 |
| `Assets/`, `CloudCode/` | 루프가 실제로 만들어 낸 산출물 |

---

## 개발하며 실제로 부딪힌 문제들

문서에 결과만 적지 않고 **함정과 그 해결**을 남겼다. 몇 가지:

- **에디트 모드에선 `Awake`가 안 돈다** → 진짜 플레이모드 진입이 필요했다.
- **리컴파일 직후엔 플레이모드 진입이 조용히 거부된다** → 초기엔 이 인프라 실패를
  *"네 코드가 틀렸다"* 로 AI에게 되먹여 멀쩡한 코드를 재생성하며 스텝을 낭비했다.
  → ready 게이팅 + 재시도, **인프라 실패는 피드백하지 않고 중단**하도록 수정.
- **Unity Mono는 Boehm GC**라 `GC.CollectionCount`/`GetTotalAllocatedBytes`가 무용지물이었다.
  → 할당을 **시간**으로 측정하도록 방향 전환(무할당 4.8ms vs 매 호출 할당 30.2ms, 6배 차이).
- **UGS: `module.exports.params` 선언을 빠뜨리면 파라미터가 걸러진다** →
  배포는 성공하는데 호출하면 로직이 틀렸다. *"배포 성공 ≠ 동작 정상"* 을 만들다가 직접 겪었다.
- **요즘 모델은 기본기가 좋다** → 처음 만든 정적 검사 5개는 아무것도 못 잡았다.
  모델이 *실제로* 자주 어기는 규칙(public 필드 노출)을 넣고서야 대조가 드러났다.

---

## 상태

**Phase 1~5 완료.** 컴파일 자가수리 → agent-agnostic → 런타임 검증 → 품질 강제 →
UGS 풀스택 → 성능 프로파일링까지 모두 실측으로 확인했다. `dotnet build` 경고 0 / 오류 0.

다음 후보: 시나리오 재생(다중 프레임), 테스트 러너(`run_tests`) 연동, 드로우콜·메모리 예산.
