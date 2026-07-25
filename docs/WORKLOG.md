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
- Phase 2 계속: `CodexBackend`(headless) 로 agent-agnostic 대조 + `eval` 기반 플레이모드 assert.
