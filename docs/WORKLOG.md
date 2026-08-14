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

### 남은 것 / 다음
- 시나리오 재생(다중 프레임 동작), `run_tests`(테스트 러너) 연동.
- 드로우콜·메모리 예산을 판정 기준으로 승격(현재는 진단 맥락으로만 기록).
