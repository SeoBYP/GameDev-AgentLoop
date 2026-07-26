# GameDev-AgentLoop

> CLI AI 에이전트(Claude Code / Codex / API)가 Unity 게임 개발 코드를 생성하고,
> **Unity에서 실제로 동작하는지 검증하고 스스로 고치는 닫힌 루프.**

대부분의 에이전트는 그럴듯하지만 안 도는 코드를 뱉는다. 이 프로젝트는 **생성 → 적용 → 검증 → 수리 → 반복**을
C# 오케스트레이터가 소유해, "코드 생성"을 **"동작 검증된 결과"** 로 바꾼다. 검증은 Unity CLI(`unity command eval`)로 실행 중인 에디터에서 직접 한다.

![Claude Code를 백엔드로 — API 키 없이 생성→적용→컴파일 검증까지 1스텝 통과](docs/images/claude-backend-run.png)

> **이 AI(Claude Code)를 두뇌로** 꽂아 키 없이 실행 — 로그인 → 생성 → Unity 적용·리컴파일 → **컴파일 통과 ✅** (1스텝).

## 구조
- `Assets/` 등 — Unity 프로젝트 (루프의 *타깃*). `com.unity.pipeline` 로 CLI가 에디터를 조작·검증.
- `Orchestrator/` — C# 콘솔 (루프 소유자). → [Orchestrator/README.md](Orchestrator/README.md)

## 설계
- **루프는 우리 것**, AI 백엔드는 텍스트 생성기로만 (교체 가능).
- 두 축 pluggable: `IAgentBackend`(두뇌) × `IExecTarget`(손).
- 언어 = C# 단일.

자세한 건 [docs/DESIGN.md](docs/DESIGN.md) · 작업 로그 [docs/WORKLOG.md](docs/WORKLOG.md) · 세션 컨텍스트 [CLAUDE.md](CLAUDE.md).

## 빠른 실행

```bash
# 전제: GameDev-AgentLoop 를 Unity 에디터에서 열어 pipeline 서버를 띄운다 (unity pipeline list → 서버 연결 가능: true)

# 이 AI(Claude Code)를 두뇌로 — API 키 없이 (claude CLI 로그인 필요)
dotnet run --project Orchestrator -- --claude "간단한 HP 컴포넌트를 만들어줘"

# Anthropic API 키로 (키는 환경변수로만; 레포 커밋 금지)
dotnet run --project Orchestrator -- "간단한 HP 컴포넌트를 만들어줘"

# 키 없이 루프 배관 증명 (일부러 깨진 Health.cs → 컴파일 에러 → 수리 → 통과)
dotnet run --project Orchestrator -- --demo
```

## 상태
**Phase 1 + Phase 2(일부) — agent-agnostic 실증.** 루프 5단계 골격 완성, 백엔드 4종
(`ClaudeCodeBackend`·`CodexBackend`·`ApiBackend`·`ScriptedBackend`)이 같은 계약으로 꽂힌다.
- `--demo` 자가수리 2스텝 통과 · `--claude`(Claude Code, 키 없음) 1스텝 통과 · `--codex`(Codex, 키 없음) 1스텝 통과
- 서로 다른 두 CLI 에이전트가 **루프 코드 변경 0**으로 동작 → agent-agnostic 입증.

다음: 플레이모드 검증(`eval` 기반 런타임 assert).
