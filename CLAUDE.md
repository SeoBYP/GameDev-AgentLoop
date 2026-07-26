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
- **Phase 1 구현 완료 + 첫 마일스톤 달성.** `Orchestrator/` 콘솔에 루프 5단계 골격이 있고, `--demo` 로
  자가수리 컴파일 통과(2스텝)를 실측 검증했다. `dotnet build` 경고 0/오류 0.
  - 백엔드 4종: `ClaudeCodeBackend`(`claude -p`, 키 없음)·`CodexBackend`(`codex exec`, 키 없음)·`ApiBackend`(Anthropic HttpClient 직통)·`ScriptedBackend`(--demo). 서로 다른 두 CLI 에이전트가 같은 루프를 돌아 **agent-agnostic 실증**(Phase 2 일부).
  - 타깃: `UnityEditorTarget` — `unity command recompile`/`recompile_status` 로 적용·컴파일검증. 루프 전 `IsConnectedAsync` preflight(서버 미연결 시 즉시 실패).
  - 주의: 루프 실행엔 GameDev-AgentLoop 에디터가 **떠 있어야** 함(`서버 연결 가능: true`). 닫히면 recompile 타임아웃.
- **도구 설치됨:** Unity CLI(`%LOCALAPPDATA%\Unity\bin\unity.exe`, beta 1.0.0-beta.3) + `com.unity.pipeline 0.4.0-exp.1`(서버 포트 7800). .NET 10 SDK.
  - 루프 실행 전제: 이 프로젝트를 에디터에서 열어 pipeline 서버를 띄운다(`unity pipeline list` → `서버 연결 가능: true`).
  - `unity auth login` 불필요(로컬 동작). `ANTHROPIC_API_KEY` 는 환경변수로만, 실제 모델 실행 시 필요.
- 다음: Phase 2(`ClaudeCodeBackend`/`CodexBackend` headless, `eval` 기반 플레이모드 assert).
- 상세: [docs/DESIGN.md](docs/DESIGN.md) · 작업 로그 [docs/WORKLOG.md](docs/WORKLOG.md) · [Orchestrator/README.md](Orchestrator/README.md)
