# 기여 가이드

[English](CONTRIBUTING.md)

봐 주셔서 감사합니다. 이슈와 PR 환영합니다.

## 무엇보다 중요한 규칙 하나

**이 프로젝트는 재지 않은 것을 주장하지 않는다.**

*그럴듯해 보이는* AI 생성 코드가 쓸모없다는 문제의식에서 출발한 프로젝트이므로,
같은 기준을 루프 자신에게도 적용한다. 구체적으로는:

- 검증 층이나 예산을 추가한다면, 그게 **실제 실패를 잡는 실행 로그**를 함께 넣는다.
- 설계했지만 확인하지 못한 것은 표시한다 — 문서는 이걸 위해
  `[현재]` / `[계획]` / `[미실측]` 표기를 쓴다.
- **그럴듯한 인과 설명은 근거가 아니다.** 뒤집어서 다시 재 봐야 안다.
  (이 레포의 주석 하나가 터치 입력에 `queueEventOnly: false` 가 필요하다고 적고 있었는데,
  반대로 재 보니 진짜 요건은 **프레임 넘기기**였다.)

## 개발 환경

```bash
git clone https://github.com/SeoBYP/GameDev-AgentLoop.git
cd GameDev-AgentLoop
dotnet build Orchestrator
```

**Unity 6**(6000.x) + `com.unity.pipeline`, **Unity CLI**, **.NET 10 SDK** 가 필요하다.
이 레포를 Unity 에디터에서 열어 pipeline 서버를 띄워 둬야 한다 —
안 그러면 모든 검증 단계가 타임아웃난다.

```bash
unity pipeline list      # 연결 가능한 서버가 보여야 한다
```

## PR 전에 돌릴 것

```bash
dotnet build Orchestrator          # 경고 0 · 오류 0
agentloop --demo                   # 컴파일 자가수리
agentloop --demo-play              # 런타임에서 잡히는 동작 오류
agentloop --demo-skills            # 도메인 규칙 반려
agentloop --demo-perf              # 시간 예산
agentloop --demo-draw              # 렌더 예산
```

데모 5종이 **루프의 회귀 테스트**다. 스크립트 백엔드라 API 키 없이 결정적으로 돌고,
변경 전과 **같은 판정**을 내야 한다. PlayMode 테스트를 건드렸다면 Unity 테스트 스위트도 돌린다.

## 도메인 스킬 추가하기

스킬은 `Skills/*.md` 에 포터블 마크다운으로 산다.
`GUIDANCE`(프롬프트 주입) + `CHECKS`(적용 전 정적 검사) 두 부분이다.

두 가지를 지켜야 한다:

1. **모델이 실제로 하는 실수를 겨냥한다.** 이론상 나쁜 것 말고.
   이 레포에서 처음 쓴 검사 5개는 아무것도 못 잡았다 — 요즘 모델은 기본기가 좋다.
2. **오탐률부터 잰다.** 제안하기 전에 `Assets/Scripts/` 의 기존 생성물에 돌려 본다.

## 스타일

- 주석과 커밋 메시지는 한국어/영어 모두 좋다.
- **CLI 표면(`--help`)은 영어**로 둔다 — 도구의 첫 진입점이라 가장 넓게 읽혀야 한다.
- `docs/DESIGN.md` · `docs/WORKLOG.md` 는 개발 기록이라 한국어로 유지한다.
- 오케스트레이터는 **의존성 0**(BCL 만)을 지킨다. 의도적인 제약이다 —
  와이어 포맷이 투명해지고, "버그가 루프에 있음"이 명확해진다.

## 문제 신고

이런 내용이 있으면 도움이 된다:

- 실행한 정확한 명령, 생성이 이상했다면 `agentloop --print-prompt` 출력
- Unity 버전, `unity pipeline list` 에 서버가 보였는지
- 어느 검증 층에서 실패했는지, 재현되는지

## 보안

**비밀은 절대 커밋하지 않는다.** `ANTHROPIC_API_KEY` 와 UGS 서비스 계정 자격은
환경변수나 gitignore 된 `.env` 에 둔다. `.env.example` 은 추적되므로 **placeholder 만** 담는다.

`eval` 기반 검증은 **샌드박스가 아니다** — 사용자의 에디터 프로세스 안에서 돈다.
`SnippetGuard` 는 경계가 아니라 완화책이다. 감사 가능성이 중요하면 `--tests-only` 를 쓴다.

## 라이선스

기여하면 그 기여물이 [MIT License](LICENSE) 로 배포되는 데 동의하는 것으로 본다.
