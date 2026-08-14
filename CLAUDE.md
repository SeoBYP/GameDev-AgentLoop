# GameDev-AgentLoop

## 무엇
CLI AI 에이전트(Claude Code / Codex / Anthropic API)가 Unity 게임 개발 코드·에셋을 생성하고,
**Unity에서 실제로 동작하는지 검증하고 스스로 고치는 "닫힌 루프"** 를 만드는 프로젝트.

> 목표는 "코드를 생성한다"가 아니라 **"검증된, 실제로 도는 결과"** 를 만드는 것.
> 대부분의 에이전트 데모는 그럴듯하지만 안 도는 코드를 뱉는다. 여기서는 루프가 실행·검증·수리까지 책임진다.

## 이 레포 구성
- `Assets/`, `Packages/`, `ProjectSettings/` — **Unity 프로젝트** = 루프의 *타깃*. `unity` CLI가 여기서 코드를 적용·검증한다.
- `Orchestrator/` *(예정)* — **C# 콘솔 = 루프 소유자.** 생성→적용→검증→피드백→반복을 돌린다.

## 핵심 설계 결정 (확정 — 바꾸지 말 것, 바꾸려면 먼저 논의)
1. **루프는 우리 것.** AI 백엔드는 "텍스트 생성기"로만 쓴다 (Claude Code의 자체 에이전트 루프에 위임하지 않는다). → 백엔드가 진짜 교체 가능해지고, 루프 자체가 결과물(포폴 가치)이 된다.
2. **두 축이 pluggable:**
   - `IAgentBackend` (두뇌): `ApiBackend`(먼저) · `ClaudeCodeBackend` · `CodexBackend`
   - `IExecTarget` (손): `UnityEditorTarget`(`unity` CLI, 먼저) · `UgsTarget`(`ugs` CLI, 나중)
3. **언어 = C# 단일** (Unity와 같은 스택, 사용자의 강점). 언어를 둘로 만들지 않는다. "둘 다"는 백엔드/타깃 인터페이스로 실현.
4. **검증이 1급 시민.** 컴파일 통과 + (가능하면) 플레이모드 동작 확인까지가 "성공"의 기준.

## 루프 5단계
```
목표(자연어)
 → ① 생성   백엔드가 코드/에셋 생성 (텍스트 out)
 → ② 적용   unity CLI (unity command eval / 파일쓰기 + 리컴파일)
 → ③ 검증   컴파일 에러? 플레이모드 동작 assert? (eval로 상태 확인)
 → ④ 피드백  에러·결과를 다음 맥락에 넣어 백엔드에 되돌림
 → ⑤ 판정   통과 → 종료 / 실패 → ①로 (maxSteps 가드)
```

## 로드맵
- **Phase 1 (지금):** 로컬 순수 루프 골격. `ApiBackend` + `UnityEditorTarget`. 도메인/스킬/UGS 전부 0.
  - 첫 마일스톤: *"HP 컴포넌트 만들어줘"* → 생성 → 컴파일 에러 나면 **스스로 읽고 고쳐서** 통과할 때까지.
- **Phase 2:** CLI 백엔드(`ClaudeCodeBackend`/`CodexBackend`) 꽂아 **agent-agnostic 증명** + 플레이모드 동작 검증 강화.
- **Phase 3:** 도메인 **Skills**(성능 최적화·클라 아키텍처·Unity 함정) 레이어 — 산출물 "품질"을 강제.
- **Phase 4:** `UgsTarget`(UGS Cloud Code 배포·호출 검증) — 클라 + 백엔드 풀스택.

## 전제 도구
- **Unity CLI** (`unity` 바이너리 + `com.unity.pipeline` 패키지) — 실행 중 에디터 제어, `unity command eval`로 C# 즉시 실행/검증(재컴파일·도메인리로드 없이). Unity 6.0 LTS+.
- **.NET SDK** — 오케스트레이터.
- **Anthropic API 키** — `ApiBackend` (EditorPrefs/환경변수, 레포에 커밋 금지).
- *(Phase 4)* **UGS CLI** (`ugs`).

## 작업 방식 (중요)
- 사용자는 **Unity/C# 개발자**. 기본은 **개념 설명 + 코드 리뷰** — 사용자가 직접 코드를 짜고 내가 리뷰한다.
  - 아주 헷갈려 하면 예시 코드로 설명, 그래도 막히면 그때 조금 작성.
  - **예외:** 프레임워크/스캐폴딩/설계 골격은 위임받아 내가 작성해도 된다(사용자가 명시할 때).
- 커밋은 **사용자가 요청할 때만.** 커밋 메시지 한국어 OK.
- 비밀(API 키)은 절대 커밋 금지. `unity`/`ugs` CLI 인증도 레포 밖.

## 지금 상태
- **Phase 1 + Phase 2 완료.** `Orchestrator/` 콘솔에 루프 5단계 골격이 있고, 컴파일 자가수리와
  **플레이모드 런타임 검증**을 모두 실측했다. `dotnet build` 경고 0/오류 0.
  - 백엔드 4종: `ClaudeCodeBackend`(`claude -p`, 키 없음)·`CodexBackend`(`codex exec`, 키 없음)·`ApiBackend`(Anthropic HttpClient 직통)·`ScriptedBackend`(--demo). 서로 다른 두 CLI 에이전트가 같은 루프를 돌아 **agent-agnostic 실증**.
  - 타깃: `UnityEditorTarget` — 검증 2단계. ③-a 컴파일(`recompile`/`recompile_status`) + ③-b **플레이모드 런타임 assert**(`editor_status` ready 대기 → `editor_play` → `eval` → `finally` `editor_stop`). 루프 전 `IsConnectedAsync` preflight.
  - 출력 계약: `FILE:` 블록(전체 파일) + `ASSERT:` 블록(플레이모드 검증 스니펫, `"OK"`/실패사유 반환). `--assert` 로 사람이 기준 주입 시 우선.
  - 데모: `--demo`(컴파일 자가수리 2스텝) · `--demo-play`(컴파일 통과하나 동작 틀린 코드 → 런타임 assert 가 잡아냄).
  - 주의: 루프 실행엔 GameDev-AgentLoop 에디터가 **떠 있어야** 함(`서버 연결 가능: true`). 닫히면 recompile 타임아웃.
  - 함정(해결됨): 에디트 모드는 `Awake` 안 돎 → 진짜 플레이모드 필요. 리컴파일 직후엔 도메인 리로드로 진입 거부 → ready 게이팅 + 재시도, 인프라 실패는 모델에 피드백하지 않음.
- **도구 설치됨:** Unity CLI(`%LOCALAPPDATA%\Unity\bin\unity.exe`, beta 1.0.0-beta.3) + `com.unity.pipeline 0.4.0-exp.1`(서버 포트 7800). .NET 10 SDK.
  - 루프 실행 전제: 이 프로젝트를 에디터에서 열어 pipeline 서버를 띄운다(`unity pipeline list` → `서버 연결 가능: true`).
  - `unity auth login` 불필요(로컬 동작). `ANTHROPIC_API_KEY` 는 환경변수로만, 실제 모델 실행 시 필요.
- **Phase 3 완료** — 도메인 Skills(`Skills/*.md`, 포터블 마크다운). `GUIDANCE`(프롬프트 주입=예방) +
  `CHECKS`(정적 검사=강제). 루프 ①-b 에서 **적용 전** 검사해 위반 시 반려. `--list-skills`/`--skills off`/`--demo-skills`.
  - 검사 추가 시 원칙: "이론상 나쁜 것"이 아니라 **모델이 실제로 하는 실수**를 겨냥하고, 기존 산출물로 **오탐부터 확인**.
- **Phase 4 진행 중** — `UgsTarget`(`ugs` CLI 1.9.0, npm 설치됨). Apply=Cloud Code JS 쓰기, Verify=`ugs deploy`.
  `IExecTarget` 이 자기를 설명하도록 확장: `GenerationBrief`·`VerifyLabel`·`Supports(kind)`·`IsConnectedAsync`/`ConnectionHint`.
  시스템 프롬프트 = [루프 형식] + [손의 규격] + [스킬 지침] (`--print-prompt` 로 확인). 스킬은 `targets:` 로 타깃 필터.
  - **한계(정직히)**: `ugs` CLI 에 스크립트 호출 명령이 없어 배포까지만 검증(`Supports(PlayModeAssert)==false`).
  - **미실측**: 실제 배포 성공 경로 — 서비스 계정 키(사용자가 `ugs login`) 필요. 비밀키는 다루지 않는다.
- **Phase 4 완료** — 실제 UGS 프로젝트(GameDevAgentLoop/production)로 배포 + REST 호출 검증 관통.
  자격은 `.env`(UGS_CLI_SERVICE_KEY_ID/SECRET, gitignore 보호). 역할은 Cloud Code **Publisher**+Editor 필요.
  함정: `--services cloud-code-scripts` / `module.exports.params` 미선언 시 파라미터 걸러짐 / `env list` 는 `--environment-name` 거부.
- **Phase 5 완료** — 성능 프로파일링 검증(③-c). `PERF:` 블록 + `PerfHarness`(하네스는 루프 소유).
  Unity Mono 는 Boehm GC 라 `GC.*` 할당 측정 API 가 무용 → **시간으로 측정**(무할당 4.8ms vs 할당 30.2ms/5만회).
  `--demo-perf`: 동작은 맞지만 핫패스 할당 → 41ms 초과 → 수리 → 11.8ms 통과.
  절대 ms 예산은 기기 의존적 → 마진 넓게(12ms 로 잡았다 플래키해서 25ms 로 조정).
- 다음: 시나리오 재생(다중 프레임), `run_tests` 연동, 드로우콜·메모리 예산 승격.
- 상세: [docs/DESIGN.md](docs/DESIGN.md) · 작업 로그 [docs/WORKLOG.md](docs/WORKLOG.md) · [Orchestrator/README.md](Orchestrator/README.md)
