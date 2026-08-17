# 작업 로그 (WORKLOG)

이 프로젝트에서 **무엇을, 왜** 했는지를 시간순으로 박제한다. 코드가 "어떻게"라면 이 문서는 "왜"다.

---

## 2026-07 · Phase 1 — 로컬 순수 루프 골격 + 첫 마일스톤

### 목표
`Orchestrator/` C# 콘솔 스캐폴드 + 루프 5단계 골격(`IAgentBackend`·`IExecTarget` + `ApiBackend` 1개 +
`UnityEditorTarget` 1개)을 만들고, **첫 마일스톤 — "HP 컴포넌트 자가수리 컴파일 통과"** 를 실제로 돌려 증명한다.

### 한 일

**1) 전제 도구 설치**
- `.NET 10 SDK` — 이미 설치돼 있음(10.0.300).
- **Unity CLI 설치** — 공식 CDN 스크립트로 `1.0.0-beta.3` 설치
  (`irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex`, 채널 beta).
  경로: `%LOCALAPPDATA%\Unity\bin\unity.exe`.
- **`com.unity.pipeline` 설치** — `unity pipeline install` 로 프로젝트 manifest 에 `0.4.0-exp.1` 추가.
  에디터를 열자(`unity open`) 패키지가 컴파일되며 로컬 서버가 포트 7800 에 떴다
  (`unity pipeline list` → `서버 연결 가능: true`). 라이선스는 Unity Personal.
  > 인증(`unity auth login`)은 **불필요**했다 — pipeline 설치·`command eval` 모두 로컬 동작이라 Unity Cloud 로그인이 없어도 된다.

**2) pipeline 명령 정찰 (중요한 발견)**
`unity command` 로 pipeline 이 등록한 명령 목록을 확인했더니, 애초에 이 프로젝트가 필요로 하는
"리컴파일시키고 에러 받아오기"가 **목적별 명령으로 이미 제공**되고 있었다:
- `recompile` — 강제 리컴파일(비포커스에서도 동작).
- `recompile_status` — `{ status, failed, errors[] }`. **완료 상태와 컴파일 에러를 한 번에** 준다.
- `get_console_logs` / `console` — 구조화된 콘솔 로그.
- `eval` — Roslyn 으로 C# 즉시 실행.

→ 처음엔 `eval` + 내부 `LogEntries` 리플렉션으로 콘솔을 긁으려 했으나, 위 명령을 발견하고
`UnityEditorTarget` 을 `recompile`/`recompile_status` 기반으로 **재작성**했다. 훨씬 견고하고 버전 의존이 적다.

**3) Orchestrator 스캐폴드 작성**
- 계약(`IAgentBackend`/`IExecTarget` + 모델 레코드) → 백엔드(`ApiBackend`/`ScriptedBackend`) →
  타깃(`UnityEditorTarget`) → 루프(`AgentLoop`) → 진입점(`Program.cs`).
- 의존성 0(Anthropic 도 `HttpClient` 직통) — DESIGN.md D3 의 "가장 단순·확정적" 원칙을 코드로 드러냄.
- `dotnet build` → 경고 0 / 오류 0.

**4) 첫 마일스톤 검증 (엔드투엔드)**
- API 키가 환경에 없어서(키는 절대 다루지/커밋하지 않음), 키 없이도 루프를 증명하도록
  `ScriptedBackend` + `--demo` 를 추가했다: *일부러 세미콜론 빠진 `Health.cs` → (컴파일 에러 피드백) → 고친 `Health.cs`*.
- `orchestrator --demo` 실행 → **2스텝 만에 자가수리로 컴파일 통과** ✅.
  step 1 에서 실제 Unity Roslyn 이 뱉은 `Health.cs(6,29): error CS1002: ; expected` 를 루프가 읽어
  피드백하고, step 2 에서 통과. → 루프 5단계 + Unity 검증 엔진이 실제로 도는 것을 확인.

### 왜 이런 선택을 했나 (요약)
- **키를 다루지 않음** — `ANTHROPIC_API_KEY` 는 환경변수로만, 레포/문서에 절대 안 남긴다(CLAUDE.md).
  그래서 실제 모델 실행 대신 `--demo`(스크립트 백엔드)로 루프 메커니즘을 증명했다. 실제 실행은 사용자가 키만 넣으면 동일하게 동작.
- **`ScriptedBackend` 를 넣은 이유** — 루프의 결정적 회귀 테스트이자, "백엔드는 갈아끼워진다"(D3)의 살아있는 증거.
  `IAgentBackend` 계약만 맞추면 루프 입장에선 API 백엔드와 완전히 동등하다.
- **`recompile_status` 채택** — 콘솔 리플렉션(취약)보다 pipeline 이 주는 구조화된 결과를 쓴다. 검증의 신뢰성 = 프로젝트의 신뢰성.

**5) `ClaudeCodeBackend` 당겨오기 — "이 AI 채팅으로도 돼야 한다"**
피드백: `--demo`(스크립트)는 배관만 증명할 뿐, 진짜 AI 자가수리가 아니다. 그리고 별도 API 키가 필요한
`ApiBackend` 보다, **지금 쓰는 Claude Code CLI 자체를 두뇌로** 쓰는 게 더 자연스럽다(키 불필요).
→ 로드맵 Phase 2 의 `ClaudeCodeBackend` 를 앞당겨 구현했다:
- `claude -p`(headless print)로 **1회 응답만** 받고 도구를 전부 비활성(`--disallowedTools`) → 순수 텍스트 생성기.
  Claude Code 의 자체 에이전트 루프에 위임하지 않는다(D1 준수). `ApiBackend`/`ScriptedBackend` 와 동등하게 꽂힌다.
- Windows 는 npm shim(`claude.cmd`)이라 `cmd.exe /c claude` 로 감쌌다. 프롬프트는 stdin 으로 전달(인자 순서 이슈 회피).
- `Program` 에 `--claude` 선택 추가. 백엔드 실패는 크래시 대신 깔끔히 보고(예: 로그인 만료).

**발견(정직히 기록):** 이 대화형 세션은 인증돼 있지만, 하위 프로세스로 부른 `claude -p` 는
`401 OAuth access token has expired` 를 냈다 = 디스크에 저장된 CLI 로그인이 만료됨.
→ `ClaudeCodeBackend` 배선·빌드는 완료(0경고/0에러), 실제 모델 호출은 **`claude` 재로그인 후** 동작한다(인증은 사용자 몫).

### 남은 것 / 다음
- `--claude` 실행 = `claude` CLI 재로그인 1회면 키 없이 실제 자가수리 동작.
- (선택) `ApiBackend` 는 `ANTHROPIC_API_KEY` 설정 시 동일 루프.

---

## 2026-07 · Phase 2 (일부) — agent-agnostic (CodexBackend)

### 목표
같은 루프에 **또 다른 독립 CLI 에이전트(Codex)** 를 꽂아, "백엔드는 진짜 교체 가능, 루프가 결과물"을
두 에이전트로 입증한다.

### 한 일
- **`CodexBackend`** 구현 — `codex exec --sandbox read-only -o <file>` (파일 변경 차단, 최종 메시지만 파일로).
  ClaudeCodeBackend 과 동일 패턴. 프롬프트 평탄화는 `Util/PromptText` 로 공유(두 CLI 백엔드 재사용).
- `Program` 에 `--codex` 추가. 모델은 `--model` 로 지정.
- **`--codex`(gpt-5.4-mini)로 오브젝트 풀 생성 → 같은 루프가 적용·검증 → 1스텝 통과** ✅.
  산출물 [../Assets/Scripts/ObjectPool.cs](../Assets/Scripts/ObjectPool.cs) 는 Queue 풀링·널가드까지 갖춘 실물.

### 발견 / 함정 (정직히 기록)
- **Codex 모델 미스매치** — 사용자 Codex 는 ChatGPT 계정 인증. CLI(0.133.0)가 계정 기본 모델
  `gpt-5.6-sol` 보다 구버전이라 "requires newer CLI" 오류. models_cache 에서 지원 모델(`gpt-5.5`/`gpt-5.4-mini`)을
  찾아 `--model` 로 지정해 해결. (인증·배선은 정상 — 프롬프트가 Codex 에 온전히 전달됨을 로그로 확인.)
- **에디터 liveness** — 세션 도중 GameDev-AgentLoop 에디터가 닫혀 pipeline 서버 미연결
  (`서버 연결 가능: false`). 그 탓에 `recompile_status` 폴링이 120s 타임아웃(`<recompile timeout>`).
  → 재실행으로 복구. + **`UnityEditorTarget.IsConnectedAsync` preflight** 추가:
  루프 시작 전 서버 연결을 확인해, 미연결이면 **AI 호출 전에 즉시 실패**(비용/시간 낭비 방지).

---

## 2026-08 · Phase 2 (완료) — 플레이모드 런타임 검증

### 목표
"컴파일 통과"를 넘어 **"의도대로 동작하는가"** 까지 검증한다. 이게 프로젝트의 존재 이유(D4).

### 한 일
- `VerifyKind.PlayModeAssert` + `VerifySpec(Kind, AssertCode)` 추가.
- `UnityEditorTarget`: `editor_status`(준비 대기) → `editor_play` → `eval`(assert) → `editor_stop`(finally).
- 출력 계약에 **`ASSERT:` 블록** 추가 — 백엔드가 코드와 함께 런타임 검증 스니펫을 낸다
  (통과 시 `"OK"`, 실패 시 사유 문자열 반환). `EditParser.ParseAssert` 로 파싱.
- `--assert` 플래그: 사람이 준 기준이 AI 의 ASSERT 보다 **우선**.
- `--demo-play`: **컴파일은 통과하지만 클램프가 빠진** `Stamina` → 런타임 assert 가 잡아냄 → 수리.

### 실측 결과
- `--demo-play`: step1 컴파일 통과 ✅ + 런타임 실패 ❌ (`Use(500) 후 Current 는 0 이어야 하는데 -400`)
  → step2 수리 → **2스텝 만에 컴파일 + 런타임 통과**.
- `--claude`(실제 AI): 쿨다운 타이머를 생성하며 **스스로 ASSERT 블록을 작성**,
  루프가 플레이모드에서 실행해 **1스텝 통과** ([../Assets/Scripts/CooldownTimer.cs](../Assets/Scripts/CooldownTimer.cs)).
- `--assert`(사람 지정): 로그에 "사람 지정"으로 표시되며 AI 기준을 덮어쓰고 클램프 버그를 잡아냄.

### 발견 / 함정 (실측으로 잡음)
1. **에디트 모드에선 `Awake` 가 안 돈다** → `AddComponent<T>()` 만으론 초기화가 안 됨.
   진짜 `editor_play` 진입이 있어야 실제 런타임 상태를 검증할 수 있다.
2. **리컴파일 직후엔 플레이모드 진입이 조용히 거부된다**(도메인 리로드 중).
   처음엔 이게 `<플레이모드 진입 실패>` 로 나면서 **인프라 실패를 AI 에게 "네 코드가 틀렸다"고 피드백**하는
   버그가 있었다(실제 `--claude` 실행에서 멀쩡한 코드를 한 스텝 낭비하며 재생성함).
   → `editor_status` 의 `status:"ready"`/`compiling`/`domainReloadInProgress` 게이팅 + 1회 재시도,
   그래도 실패하면 **피드백하지 않고 인프라 오류로 중단**하도록 고쳤다. 이후 재실행에서 1스텝 통과 확인.
3. PowerShell 로 eval 스니펫을 직접 넘기면 내부 큰따옴표가 사라진다(수동 프로브 한정).
   오케스트레이터는 `ProcessRunner` 가 `ArgumentList` 를 쓰므로 영향 없음.

### 정직한 한계
기본 경로에서는 **AI 가 자기 코드의 채점 기준(ASSERT)도 스스로 낸다.** 느슨한 기준을 낼 유인이 있다.
완화책: 피드백에 "assert 말고 구현을 고쳐라" 명시 + `--assert` 사람 주입 경로.
근본 해결(기준을 생성자와 분리)은 Phase 3 의 몫.

---

## 2026-08 · Phase 3 — 도메인 Skills (품질 강제)

### 목표
"도는가"를 넘어 **"잘 만들었는가"** 를 강제한다. 도는 코드에도 Update 안 탐색·public 필드 남발·
매 프레임 할당 같은 품질 문제가 남기 때문.

### 결정: 포터블 마크다운 (DESIGN §7 열린 질문 해소)
`.claude/skills` 전용 포맷을 쓰면 Codex/API 백엔드에서 안 먹어 D1(백엔드 교체 가능)이 깨진다.
→ 스킬은 **오케스트레이터가 소유하는 `Skills/*.md`** 로 두고 모든 백엔드에 동일 적용.

### 한 일
- `Skills/` 에 스킬 3종: `unity-performance`(검사 5) · `unity-pitfalls`(3) · `client-architecture`(1).
  각 파일은 `## GUIDANCE`(프롬프트 주입) + `## CHECKS`(정적 검사)로 구성.
- `Orchestrator/Skills/`: `SkillLibrary`(로드·선택·지침 생성·검사 실행), `CSharpSource`(중괄호 매칭으로
  메서드 몸통 추출 → "Update 안에서만" 같은 스코프 한정 검사), 의존성 0 유지(YAML 파서도 안 씀).
- 루프에 **①-b 단계** 추가: 생성 직후 **프로젝트에 쓰기 전에** 검사 → 위반 시 반려·피드백.
- 플래그: `--list-skills`, `--skills off`(대조용), `--skills-dir`, 데모 `--demo-skills`.

### 실측 — 대조 실험 (같은 목표·같은 모델, 스킬만 on/off)
| 규칙 | `--skills off` | 스킬 적용 |
|---|---|---|
| public 필드 | `public float moveSpeed = 5f;` | `[SerializeField] private float _moveSpeed = 5f;` |
| 제곱근 회피 | `transform.position == target` | `(newPos - target).sqrMagnitude <= thresholdSqr` |
| 프로퍼티 반복 접근 | `transform.position` 3회 | 지역변수 캐싱 |
| 상태 변화 통지 | 없음 | `event Action<Vector3>` |
| 성공/실패 | `void` | `bool` + NaN 입력 검증 |

`--demo-skills`: 위반 3건(public 필드 / Update 안 GetComponent / Update 안 Debug.Log) 검출 →
**적용하지 않고** 반려 → 수정본 통과.

### 발견
- **요즘 모델은 기본기가 좋다.** 스킬 없이도 `Camera.main` 은 `Awake` 에 캐싱했다.
  그래서 처음 만든 검사 5개는 아무것도 잡지 못했다. 실제로 자주 어기는 규칙
  (**public 필드 노출**)을 검사로 추가하자 비로소 대조가 드러났다.
  → 교훈: 검사는 "이론상 나쁜 것"이 아니라 **모델이 실제로 하는 실수**를 겨냥해야 값어치가 있다.
- 검사 정규식은 추가 전에 **기존 산출물 5개에 돌려 오탐을 먼저 확인**했다
  (프로퍼티 `=>`, `event`, `const`, 메서드 선언이 걸리지 않는지). 오탐은 루프를 망가뜨린다.
- **검사는 소급 적용된다** — `no-public-mutable-field` 를 추가하자 기존 `--demo`/`--demo-play`
  스크립트(`public int Max = 100;`)가 반려되며 `--demo` 가 maxSteps 까지 돌다 실패했다.
  스크립트 백엔드는 같은 답을 반복하므로 영원히 못 고친다. → 데모 코드도 규칙을 지키도록 고쳤고
  (`[SerializeField] private` + 읽기 전용 프로퍼티), 겸사겸사 데모 파일을 AI 산출물과 분리했다
  (`Health.cs` → `DemoHealth.cs`). 규칙을 추가할 땐 **레포 전체가 그 규칙을 만족하는지** 확인해야 한다.

### 한계
검사는 정규식 + 중괄호 매칭 수준이라 국소 규칙만 잡는다(의존성 0 유지가 목적).
구문 수준 규칙이 필요해지면 Roslyn 분석기로 승격하면 된다.

---

## 2026-08 · Phase 4 — UgsTarget (두 번째 손)

### 목표
손을 바꿔 같은 루프로 **백엔드(UGS Cloud Code)** 를 만들고 검증한다 → 두 축 pluggable 완성.

### 한 일
- **`ugs` CLI 설치** — `npm install -g ugs` (1.9.0). `unity cloud` 는 조직/프로젝트 관리만 하고
  Cloud Code 는 커버하지 않아 별도 CLI 가 맞았다.
- **`UgsTarget`** 구현 — Apply=Cloud Code `.js` 파일 쓰기, Verify=`ugs deploy`(배포가 곧 검증).
- **`IExecTarget` 확장** — 타깃이 자기를 설명하도록:
  `GenerationBrief`(생성 규격) · `VerifyLabel` · `Supports(kind)` · `IsConnectedAsync`/`ConnectionHint`.
  시스템 프롬프트가 **[루프 형식] + [손의 규격] + [스킬 지침]** 조립으로 바뀌었다.
- **스킬 타깃 필터** — front-matter `targets: unity` 추가. UGS 실행 시 Unity 스킬이 딸려가지 않는다.
- **`--print-prompt`** — 인증 없이도 타깃별 프롬프트 조립 결과를 확인하는 수단.
- 플래그: `--target unity|ugs` · `--ugs-project-id` · `--ugs-env` · `--cloud-code-dir`.

### 실측 (인증 경계까지)
- `--target ugs --print-prompt` → Cloud Code JS 규격, ASSERT 없음, 런타임 검증 지원 False ✅
- `--target ugs --list-skills` → Unity 스킬 3종 모두 **미선택** ✅
- `--target ugs` 실행 → 미인증 감지 후 **AI 호출 전** 안내 출력하고 종료(exit 2) ✅
- Unity 데모 3종(`--demo`/`--demo-play`/`--demo-skills`) 회귀 없음 ✅

### 발견 / 정직한 한계
- **`ugs` CLI 에 스크립트 호출 명령이 없다.** `cloud-code scripts` 는 create/publish/get/list/update/delete 뿐.
  → `Supports(PlayModeAssert) == false` 로 선언하고 루프가 런타임 단계를 건너뛰게 했다.
  없는 기능을 있는 척하지 않는다. 호출 검증은 Cloud Code REST + 플레이어 토큰 경로가 필요하다.
- **실제 배포는 사용자 인증이 필요하다.** 서비스 계정 키는 Unity Cloud 대시보드에서만 발급되고,
  비밀키는 오케스트레이터가 다루지 않는다(`ugs login` 또는 환경변수로 CLI 가 보관).
  그래서 "배포 성공" 경로는 아직 미실측이며, 문서에 그대로 적었다.
- 사용자 클라우드에 실제 배포하는 것은 **외부에 영향을 주는 작업**이라 임의로 하지 않고 확인을 받기로 했다.

---

## 2026-08 · Phase 5 — 성능 프로파일링 검증

### 목표
"동작 정상"을 넘어 **"충분히 빠른가"** 까지 강제한다. 게임에서 매 프레임 할당하는 컴포넌트는
정확해도 실패작이기 때문. Phase 3 스킬이 *정적 추측*이라면 이건 **실측**이다.

### 측정 방법을 찾는 과정 (실측으로 결정)
관리 힙 할당을 직접 재려 했으나 Unity Mono 는 **Boehm GC**(세대 없음)라 줄줄이 실패했다:

| 시도 | 결과 |
|---|---|
| `GC.GetTotalAllocatedBytes(true)` | 런타임에 없음(컴파일 실패) |
| `GC.CollectionCount(0)` | 할당 경로에서도 항상 0 |
| `GC.GetTotalMemory(false)` | 힙 크기 → 수집이 따라잡아 0 |
| `Profiler.GetMonoUsedSizeLong()` | 절대값은 나오나 델타 0 |
| **`Stopwatch`** | ✅ 무할당 **4.8ms** vs 매 호출 할당 **30.2ms** (5만 회, 6배) |

→ 할당은 결국 **시간으로 드러난다.** 시간으로 재고 `get_performance_stats`(드로우콜·메모리·프레임타임)를
보조 지표로 함께 남기기로 확정.

### 한 일
- `VerifyKind.Performance` + `PERF:` 출력 블록(JSON) + `EditParser.ParsePerf`.
- **`PerfHarness`** — 백엔드는 `{component, call, iterations, maxTotalMs}` 만 선언하고
  워밍업·타이머·정리 스니펫은 오케스트레이터가 만든다(자기 벤치마크 느슨하게 쓰는 것 방지).
- `UnityEditorTarget.VerifyPerformanceAsync` — 플레이모드 진입 → 측정 → `editor_stop`,
  `get_performance_stats` 를 진단 맥락으로 함께 기록.
- 루프 ③-c 단계 + `BuildPerfFeedback`("예산 늘리지 말고 구현을 고쳐라").
- `--demo-perf`(동작은 맞지만 핫패스 할당), `--no-perf`(대조용).

### 실측
```
step 1  컴파일 ✅ / 플레이모드 assert ✅ / 성능 41.03ms > 예산 25ms ❌
step 2  컴파일 ✅ / 플레이모드 assert ✅ / 성능 11.82ms ✅
✅ 2스텝 만에 동작 + 성능까지 검증 통과
```
동작 검증을 **통과한** 코드가 성능에서 걸렸다 — 이 단계의 존재 이유가 그대로 드러난 로그.

### 발견
- **예산 캘리브레이션이 중요하다.** 처음 12ms 로 잡았더니 같은 코드가 13.9ms↔11.9ms 로 흔들려
  결과가 갈렸다(플래키). 41ms/11.8ms 사이 25ms 로 넓히니 결정적으로 재현된다.
  → 절대 ms 예산은 기기 의존적이며, 마진을 넓게 잡아야 한다는 걸 문서에 명시.
- 다른 데모(`--demo`/`--demo-play`/`--demo-skills`)는 PERF 블록이 없어 ③-c 를 건너뛴다 — 회귀 없음.

---

## 2026-08 · Phase 6 — 테스트 러너 · 시나리오 재생 · 시각 증거

### 계기
레퍼런스 [unity-cli-loop](https://github.com/hatayama/unity-cli-loop) 분석. 저쪽은 *AI 가 도구를 골라 루프를 도는*
구조라 우리(루프를 코드가 소유)와 접근이 다르지만, **검증 수단**은 배울 게 많았다:
테스트 러너 연동 · 입력 시뮬레이션 · 스크린샷. 루프는 그대로 두고 검증 도구만 흡수했다.

### 한 일
- **테스트 러너 연동(6-A)** — `VerifyKind.Tests`. 테스트 파일이 오면 ASSERT 대신 테스트 러너로 검증.
  별도 블록을 만들지 않고 **경로(`/Tests/`)로 감지**한다 — 테스트는 그냥 `FILE:` 블록이다.
- **어셈블리 구조** — 테스트 asmdef 는 `Assembly-CSharp` 를 참조할 수 없어서
  런타임에도 asmdef 를 줬다(`AgentLoop.Runtime` ← `AgentLoop.Tests`).
- **시나리오 재생(6-B)** — 별도 기능 없이 **`[UnityTest]` 코루틴**이 곧 다중 프레임 검증.
  생성 지침에 "여러 프레임에 걸쳐 펼쳐지는 동작은 `[UnityTest]` 로 검증하라" 추가.
- **시각 증거(6-C)** — `capture_game_view` 로 결과 화면 PNG 저장(`--capture`).
  **판정에는 쓰지 않는다**(기준 이미지 없이 시각 회귀 판정 불가). 사실상 빈 화면이면 경고만.
- 스킬 정적 검사가 `/Tests/` 를 건너뛰도록 수정(테스트는 규칙이 다르다).

### 실측
- 스모크 테스트로 인프라 확인 → `list_tests` 2개 발견, `run_tests` 2/2 통과.
- **실제 AI 실행**: "여러 프레임에 걸쳐 도착하는지 `[UnityTest]` 로 검증해줘" →
  구현 + 테스트 파일 2개 생성 → **테스트 6/6 통과** → 성능 예산까지 통과(2스텝).
  생성된 테스트에 `while (target.IsMoving) yield return null;` 로 프레임을 넘기며 도착을 확인하는
  진짜 시나리오 검증이 들어 있었다.
- 캡처: 130KB PNG 저장 + `Assets/` 임시 폴더·meta 정리 확인.

### 발견 / 함정
- **PlayMode 테스트는 동기 실행 불가** — 플레이모드 진입 시 도메인 리로드가 HTTP 요청을 끊는다.
  CLI 가 직접 안내한다: `run_tests --async_tests` 후 `test_status` 폴링. (`data.result` 는 JSON 문자열)
- **asmdef 도입의 부작용** — 도메인 리로드 직후 `eval` 이 새 어셈블리를 몰라
  `The type or namespace name 'X' could not be found` 로 실패하는 창이 있다.
  실제로 성능 검증 1스텝이 이 때문에 헛돌았다. → `EvalWithRetryAsync` 로 조용히 1회 재시도.
  (모델 잘못이 아닌 인프라 실패는 피드백하지 않는다는 원칙의 연장)
- **캡처 경로가 `Assets/` 아래로 갇힌다** — 절대경로를 줘도 authoring root 제약으로 무시된다.
  Assets 밑 임시 폴더에 찍고 밖으로 옮긴 뒤 `.meta` 까지 정리하도록 구현.

---

## 2026-08 · Phase 6 보강 — 컨텍스트 절약 · eval 안전 가드

레퍼런스 분석에서 **우리 쪽 실제 결함**으로 드러난 둘을 메웠다.

### 컨텍스트 절약
레퍼런스는 큰 결과를 파일로 빼고 **경로만** AI 에게 주지만, 우리 백엔드는 **도구 없는 텍스트 생성기**라
경로를 줘도 못 읽는다. 그래서 구조에 맞게 다시 풀었다:
- **히스토리 윈도우**(기본 4턴 + 목표) — 계약이 "매번 전체 파일"이라 과거 시도는 버려도 안전하다.
- **피드백 상한** — 8건·400자, `…그 외 N건 생략`.
- **전체 원문은 파일로** — `%TEMP%/agentloop-runs/<ts>/stepNN-*.log` (사람 몫). 모델엔 요약만.
- 스텝마다 `(맥락 N자 / 턴 M개)` 를 찍어 **절약 효과가 보이게** 했다.

**실측**(6스텝): OFF 6,403 → **11,570자(턴 11)** 선형 증가 vs ON 6,403 → **8,489자(턴 5)** 평탄화.

### eval 안전 가드
검증 스니펫은 에디터 안에서 제한 없이 돈다 — 파일 삭제·프로세스·네트워크가 문법적으로 가능하다.
`SnippetGuard` 로 실행 **전에** 정적 차단(파일변경/디렉터리/프로세스/네트워크/레지스트리/에셋삭제/종료).
위반은 **모델 잘못**이므로 피드백으로 되돌린다(인프라 실패와 구분). `--allow-unsafe-eval` 로 우회.

**실측**: `File.Delete` 포함 assert → 플레이모드 진입조차 없이 차단. 정상 assert 는 오탐 없이 통과.

**한계 명시**: 샌드박스가 아니라 완화책이다. 정적 문자열 검사라 리플렉션·문자열 조립으로 우회 가능하다.

---

## 2026-08 · 입력 시뮬레이션 · eval 격리(가능한 범위)

### 입력 시뮬레이션 — 전용 도구 대신 테스트 능력으로
레퍼런스는 `simulate-mouse`/`simulate-keyboard` 전용 도구를 두지만 우리 pipeline 엔 없다.
대신 **Input System 의 `InputTestFixture`** 로 PlayMode 테스트 안에서 가상 장치를 만들어 입력을 주입했다.
우리 구조엔 이쪽이 더 맞다 — 입력 시뮬레이션이 별도 경로가 아니라 **테스트가 하는 일**이 되고
`[UnityTest]`(시나리오 재생)와 그대로 합쳐진다.

- asmdef 참조 추가: Tests ← `Unity.InputSystem`, `Unity.InputSystem.TestFramework` / Runtime ← `Unity.InputSystem`
- 스모크 테스트로 인프라 확인(가상 키보드 Press/Release 반영) → 7/7 통과
- 생성 지침에 `InputTestFixture` 패턴 예시 추가

**실측**: "스페이스바로 점프, 0.5초 뒤 자동 착지" →
모델이 `JumperTests : InputTestFixture` 로 `AddDevice<Keyboard>()` → `Press(spaceKey)` → 프레임 진행 검증.
**step 1 에서 자동 착지 버그를 잡고(9/10) step 2 에 10/10 통과.**

### eval 격리 — 할 수 있는 것/없는 것 구분
`eval` 은 **사용자의 에디터 프로세스 안에서** 돈다. 그 프로세스는 우리가 만들지도 소유하지도 않으므로
**오케스트레이터 코드로 샌드박싱할 수 없다.** "프로세스 분리 구현"이라고 쓰면 거짓이 된다.

대신 **임시 코드 실행 자체를 없앨 수는 있다** → `--tests-only`:
eval 경로(RuntimeAssert/Performance)를 쓰지 않고 **컴파일된 테스트 파일로만** 검증한다.
AI 가 만든 코드가 전부 git 에 남는 리뷰 가능한 파일이 된다. **격리가 아니라 감사 가능성**이라고 정직하게 부른다.
진짜 격리(전용 OS 계정·VM/컨테이너)는 코드가 아니라 **운영 절차**로 문서화했다(DESIGN §6.12).

### 발견 / 고친 것
- **내 인프라 결함을 AI 가 먼저 고쳤다** — `Jumper.cs` 가 `UnityEngine.InputSystem` 을 쓰는데
  Runtime asmdef 에 참조가 없어 컴파일 실패하자, 모델이 **asmdef 자체를 수정해 제출**했다.
  그 참조가 정확해서 그대로 인프라로 채택했다.
- **내 로직 버그** — tests-only 모드가 "이번 응답에 테스트가 있는가"만 봤다.
  구현만 고쳐 보낸 스텝에서 *이미 디스크에 있는* 테스트를 무시하고 거부했다.
  → 실행 단위로 `testsInProject` 를 누적하도록 수정(Auto 모드 품질도 같이 개선).

---

## 2026-08 · 렌더 예산 승격 · 마우스/게임패드 입력

### 드로우콜 예산 승격 — 메모리는 뺐다(실측 근거)
먼저 **측정이 가능한지부터** 확인했다. 플레이모드에서 큐브 60개를 스폰하고 통계를 비교:

| | drawCalls | triangles | monoUsedBytes |
|---|---|---|---|
| 베이스라인 | 24 | 1,703 | 868MB |
| 큐브 60개 | **264** | **4,583** | 864MB |
| 정리 후 | 24 | 1,703 | 945MB |

→ 렌더 지표는 또렷하게 반응하고 정확히 복귀한다. **메모리는 감소하기도 한다**(Boehm GC 요동) —
예산 기준으로 못 쓴다. 그래서 **렌더만 승격하고 메모리는 진단 기록으로 유지**했다.

- `PerfSpec` 에 선택적 `SceneBudget`(setup + maxDrawCallIncrease/maxTriangleIncrease) 추가.
- 측정: 베이스라인 → 씬 상태 생성 → 렌더 대기 → 증가분. **정리 코드 없음** —
  플레이모드 이탈이 런타임 생성물을 전부 되돌려 준다(추적 불필요).
- `--demo-draw`: 8×8 타일 개별 스폰 → **시간 0.01ms 통과, drawCalls +257 로 예산(120) 초과** →
  4×4 로 줄여 +64 통과. *"빠른데 무거운"* 코드는 시간만 재면 못 잡는다.

### 마우스·게임패드
`InputTestFixture` 로 키보드 외 장치도 검증했다 — 마우스 `Set(position)`+`Press(leftButton)`,
게임패드 `Set(leftStick)`+`Press(buttonSouth)`. 스모크 테스트 **12/12 통과**.
생성 지침의 입력 예시를 장치별로 확장(Keyboard/Mouse/Gamepad/Touch).

### 발견 / 고친 것
- **하네스 결함**: `call` 에 프로퍼티 읽기(`target.Count`)가 오면
  "Only assignment, call, ... can be used as a statement" 로 컴파일이 깨졌다.
  → `AsStatement()` 로 값 표현식은 `_ =` 로 감싸 문으로 만든다(같은 부류 실패를 통째로 차단).
- **드로우콜 캘리브레이션**: 큐브 1개가 drawCall ~4개(조명·패스)라 오브젝트 수로 예산을 추정하면 틀린다.
  처음 30으로 잡았다가 수정본(16개→+64)도 초과해서, 실측 후 120으로 조정했다.
  시간 예산 때와 같은 교훈 — **예산은 반드시 한 번 재보고 정한다.**
- eval 결과 원문(JSON)이 로그에 그대로 찍히던 것 정리(전문은 실행 로그 파일로).

### 남은 것 / 다음
- 빌드 성능과 에디터 측정치의 차이 보정(현재는 상대 비교용).

---

## 2026-08 · 터치 입력 실측

지침에만 적어 두고 안 재 본 마지막 장치. **적어 둔 예시가 실제로 컴파일되고 통과하는지**를 확인했다.

`SetTouch` 는 시그니처부터 패키지 소스에서 확인했다(추측 금지):

```
InputTestFixture.SetTouch(int touchId, TouchPhase phase, Vector2 position, Vector2 delta = default,
    bool queueEventOnly = true, Touchscreen screen = null, double time = -1, double timeOffset = 0)
```

`InputSmokeTest.VirtualTouch_BeganAndEnded_AreSeen` 추가 — `Touchscreen` 을 붙이고
Began → 좌표·페이즈 확인 → Ended 확인. **스모크 13/13 통과.**

### 발견 / 고친 것
- **`TouchPhase` 는 정규화가 필수.** `UnityEngine`(구 Input)과 `UnityEngine.InputSystem` 양쪽에
  같은 이름이 있어, 테스트가 두 네임스페이스를 다 `using` 하면 그냥 `TouchPhase` 는 **CS0104 모호 참조**다.
  키보드·마우스·패드에는 없던 함정이라 터치를 실제로 짜 보기 전엔 안 드러났다.
  → 생성 지침의 터치 예시를 `UnityEngine.InputSystem.TouchPhase.Began` 정규화 형태로 고쳤다.
- **내가 처음 단 주석이 틀렸다.** `queueEventOnly` 기본값이 `true` 라 "즉시 반영하려면 `false` 로 줘야 한다"고
  적었는데, **뒤집어서 재 보니 기본값으로도 통과했다.** 진짜 요건은 `yield return null`(프레임 넘기기)였다.
  - 부정 실험으로 확정: 프레임을 안 넘기고 읽으면 `Expected: Began / But was: None` 으로 **실패**한다.
    통과가 헛통과가 아님(falsifiable)까지 같이 확인된다.
  - 교훈은 이 프로젝트가 계속 배운 것과 같다 — **"그럴듯한 설명"은 근거가 아니다.**
    문서에 쓸 인과는 한 번 뒤집어 봐야 안다.
- 생성 지침의 "항상 `yield return null`" 문구에 *왜*(입력은 큐잉된다)를 덧붙였다. 규칙만 주면 모델이 생략한다.

---

## 2026-08 · 성능을 루프 밖으로 (베이스라인이 알려 준 것)

벤치마크 베이스라인(18목표)을 돌리다 **2목표에서 멈췄다.** 숫자가 틀리게 나오고 있었기 때문이다.

### 무엇이 보였나
두 목표 **모두** step 1 에서 같은 모양으로 실패했다:

```
③ verify → Test Runner passed ✅  7/7 tests passed      ← 코드는 맞았다
③ verify → perf budget ❌  'Health' could not be found  ← 어셈블리가 아직 안 돌아왔다
```

PERF 스니펫은 `eval` 로 도는데, **PlayMode 테스트 직후의 `eval`** 은 도메인 리로드 창에 걸린다.
`EvalWithRetryAsync` 가 2.5초 뒤 1회 재시도하지만 그걸로 부족했다.

문제는 실패 자체가 아니라 **그 실패가 모델에게 "네 코드가 틀렸다"로 되먹여졌다**는 것이다.
모델은 이미 7/7 통과한 코드를 다시 만들고 step 2 에서 통과했다.
→ **모든 목표가 인프라 이유로 1스텝씩 손해**를 본다. 평균 2.0스텝이라는 숫자가 나오는데
진짜 값은 1.0 에 가깝다. 그 기준으로 이후 모든 개선을 판단하게 될 뻔했다.

2/2 재현이면 노이즈가 아니라 계통 오차다. 그래서 중단했다.

### 진짜 원인은 더 위에 있었다
사용자 지적이 정확했다 — **성능을 매 검증마다 잴 이유가 없고, 애초에 성능은 빌드에서 재야 한다.**

우리 문서가 이미 그 증거를 갖고 있었다:
- *"절대 ms 예산은 기기 의존적"* — 같은 코드가 13.9ms↔11.9ms
- *"빌드 성능과 에디터 측정치 차이 보정"* — 처음부터 미해결 항목

에디터 측정치는 IL2CPP 아닌 Mono·burst 없음·에디터 오버헤드 때문에 **상대 신호**일 뿐인데,
`maxTotalMs` 라는 이름으로 **절대 게이트**처럼 써 왔다. 없던 권위를 부여한 것이다.

### 고친 것
- 인라인 성능 검증을 **옵트인**(`--perf`)으로 내렸다. 기본 사슬은 정확성만 본다.
  `Supports(VerifyKind.Performance)` 를 타깃이 선언하게 해서, 루프의 기존 `Skip` 기계가 그대로 처리한다 —
  새 분기가 없다.
- 성능을 안 볼 거면 생성 지침에서 **PERF 블록을 요구하지도 않는다.** 쓰라고 해 놓고 무시하면
  토큰만 쓰고, 모델이 자기 예산을 스스로 정하는 습관도 그대로 남는다.
- 데모 2종(`--demo-perf`·`--demo-draw`)은 `--perf` 를 스스로 켠다 — 회귀 스위트는 그대로 돈다.

**이게 재시도 땜질보다 나은 이유**: 테스트가 있으면 `eval` 을 쓰는 경로가 없어져,
경합 자체가 사라진다. 증상이 아니라 구조를 고쳤다.

### 곁가지로 더 나쁜 걸 봤다 — **거짓 통과**
옵트인 전환 뒤 `--demo-perf` 가 한 번 **1스텝에 통과**했다. 회귀 케이스가 죽은 줄 알고 조사했는데,
3회 반복해 보니 정상이었다(step1 39.87/41.04/41.25ms ❌ → step2 12.64/11.38/12.32ms ✅, 편차 ±2%).
**1회 결과로 판단한 내가 틀렸다.**

그런데 그 이상값이 더 중요한 걸 드러냈다. 통과했을 때의 수치 **11.47ms 는 *수리된 버전*의 값**이고,
그 실행은 `Apply` 가 5.7초(평소 0.5초)로 비정상적으로 느렸다.
→ **리컴파일이 끝나기 전에 eval 이 이전 어셈블리를 측정**한 것으로 보인다.

벤치마크에서 본 것과 **같은 경합인데 방향이 반대**다:
- 벤치: 타입을 못 찾음 → **거짓 실패** (멀쩡한 코드를 실패로)
- 여기: 낡은 어셈블리를 잼 → **거짓 통과** (검증했다고 말하지만 낡은 코드를 쟀다)

**거짓 통과가 더 위험하다.** 거짓 실패는 스텝을 낭비할 뿐이지만, 거짓 통과는 검증의 의미 자체를 없앤다.
성능을 옵트인으로 내린 뒤 기본 경로(테스트 러너)는 eval 을 쓰지 않아 이 경합에서 벗어났지만,
`--perf` 와 ASSERT 경로에는 남아 있다. 최적화 패스를 만들 때 **측정 대상이 방금 그 코드가 맞는지**를
확인하는 장치가 필요하다.

### 참고: 상대 신호는 견고하다
동일 조건에서 두 구현을 한 스니펫으로 재 보면(에디트 모드, 4회):
```
alloc=40.76ms  noalloc=2.78ms  ratio=14.7x
alloc=41.04ms  noalloc=2.78ms  ratio=14.8x
alloc=40.73ms  noalloc=2.91ms  ratio=14.0x
alloc=43.46ms  noalloc=2.80ms  ratio=15.5x
```
**구현 간 차이(≈15배)는 반석같이 안정적**이다. 흔들리는 건 절대값과 측정 맥락이다.
→ 성능은 "예산 게이트"가 아니라 **회귀 비교**로 쓰는 게 맞다는 걸 숫자가 말한다.

### 다음
최적화는 별도 패스로 간다(ARCHITECTURE §9.4). **[미실측]** `unity command --runtime` 으로
실행 중인 Player 에 붙을 수 있는지가 관건이다 — 되면 빌드에서 진짜 성능을 잴 수 있다.

---

## 2026-08 · 베이스라인 기록 (`20260817-121624`)

성능을 루프 밖으로 뺀 뒤 18목표를 다시 돌렸다. **약 30분, 인프라성 실패 0건.**

| 분할 | 통과 | 평균 스텝 | 평균 벽시계 |
|---|---|---|---|
| **holdout** | **6/6 (100%)** | **1.33** | 141.4s |
| train | 12/12 (100%) | 1.17 | 80.0s |
| all | 18/18 (100%) | 1.22 | 100.5s |

### 수정이 실제로 먹혔다는 증거
같은 목표(`health-clamp`)의 1차 대 2차:

| | 1차 (성능 인라인) | 2차 (성능 옵트인) |
|---|---|---|
| 스텝 | 2 | **1** |
| 시간 | 236.8s | **36.5s** |
| 어셈블리 미인식 | 4건 | **0건** |

1차의 2스텝은 모델 실수가 아니라 도메인 리로드 경합이었다는 게 이걸로 확정된다.

### 수리가 필요했던 4개는 전부 진짜 결함이었다
| 목표 | step1 실패 | 잡은 층 |
|---|---|---|
| `damage-over-time` | 테스트 4/5 | 테스트 러너 |
| `grid-spawner` | 테스트 6/7 | 테스트 러너 |
| `wave-spawner` | 테스트 5/6 | 테스트 러너 |
| `inventory-stack` | 도메인 규칙 위반 | 스킬 정적 검사 — **적용 전 반려** |

검증 층이 각각 제 몫을 했다. 노이즈가 하나도 섞이지 않은 스윕은 이번이 처음이다.

### 정직하게 — 이 베이스라인의 한계
- **성공률에 여유가 없다.** 100% 라 이 지표는 이제 **퇴보만** 잡는다. 개선은 못 보여 준다.
- **평균 스텝 신호가 얇다.** 18개 중 14개가 원샷이라 범위가 1.00~1.22 뿐이다.
  어떤 변경이 이걸 유의미하게 움직이려면 상당히 커야 한다.
- → 나중에 더 날카로운 계기가 필요하면 정직한 해법은 **더 어려운 목표**를 넣는 것이지,
  이 숫자를 느슨하게 읽는 게 아니다. 그때까지 이건 진척 계기판이 아니라 **회귀 방지 장치**다.

샌드박스는 18목표 후에도 생성물 0개로 깨끗했다(정리 로직 정상).
