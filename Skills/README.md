# Skills — 도메인 지식 레이어 (Phase 3)

루프가 만들어 내는 코드의 **품질**을 강제하는 레이어. 스킬 하나 = 마크다운 파일 하나이고,
각 파일은 두 가지를 담는다:

| 섹션 | 역할 | 언제 쓰이나 |
|---|---|---|
| `## GUIDANCE` | 생성 시 지킬 규칙(산문) | 시스템 프롬프트에 주입 → **예방** |
| `## CHECKS` | 산출물에 강제할 정적 검사 | 파일 적용 **전** 검사 → **반려** |

지침만 주면 "권고"에 그친다. 검사가 붙어야 **강제**가 된다 — 이게 Phase 3 의 핵심이다.

## 왜 마크다운인가 (DESIGN §7 결정)

`.claude/skills` 같은 특정 CLI 전용 포맷으로 두면 Codex·API 백엔드에서는 먹지 않는다.
그러면 "백엔드는 교체 가능하다"(D1)가 깨진다. 그래서 스킬은 **오케스트레이터가 소유하는
포터블 마크다운**으로 두고, 어떤 백엔드를 쓰든 동일하게 주입·검사한다.

## 형식

```markdown
---
name: unity-performance      # 필수, 식별자
title: Unity 성능 — 핫패스 규칙
always: true                 # true 면 항상 적용
when: 성능, 최적화            # always 가 아닐 때 목표 문자열과 매칭할 키워드
targets: unity               # 적용할 타깃(생략하면 모든 타깃). 예: unity | ugs
---

## GUIDANCE
- 모델이 따라야 할 규칙을 산문으로.

## CHECKS
- id: no-getcomponent-in-update
  scope: Update, FixedUpdate, LateUpdate   # 검사할 메서드. `*` 면 파일 전체
  forbid: \bGetComponents?\s*<             # 스코프 안에서 발견되면 위반(정규식)
  message: Update 계열에서 GetComponent 를 호출하지 마세요. ...
```

`forbid-empty-body: true` 를 쓰면 "스코프 메서드의 몸통이 비어 있으면 위반"이 된다.

## 현재 스킬

| 파일 | 내용 | 검사 |
|---|---|---|
| `unity-performance.md` | 핫패스에서 탐색·할당 금지, sqrMagnitude, 풀링 | 5 |
| `unity-pitfalls.md` | fixedDeltaTime, DestroyImmediate, 이벤트 해지, 클램프 | 3 |
| `client-architecture.md` | 캡슐화, 단일 책임, 이벤트 통지, 테스트 가능성 | 1 |

확인: `dotnet run --project Orchestrator -- --list-skills`

## 효과 (실측)

같은 목표·같은 모델로 스킬만 껐다 켰을 때의 차이:

| 규칙 | `--skills off` | 스킬 적용 |
|---|---|---|
| public 필드 금지 | `public float moveSpeed = 5f;` | `[SerializeField] private float _moveSpeed = 5f;` |
| 제곱근 회피 | `transform.position == target` | `(newPos - target).sqrMagnitude <= thresholdSqr` |
| 프로퍼티 반복 접근 | `transform.position` 3회 읽음 | 지역 변수에 담아 재사용 |
| 상태 변화 통지 | 없음 | `event Action<Vector3> OnTargetChanged` |
| 성공/실패 반환 | `void SetDestination(...)` | `bool SetTargetPosition(...)` + 입력 검증 |

검사가 반려하는 경로는 `--demo-skills` 로 확인할 수 있다(위반 3건 검출 → 적용 거부 → 수정 → 통과).

## 스킬 추가하기

`Skills/` 에 `.md` 를 하나 더 놓으면 끝이다(코드 수정 불필요 — 런타임에 로드).
검사를 새로 넣을 때는 **오탐부터 확인**한다. 기존 산출물에 돌려 보고 정상 코드가 걸리지 않는지 본 뒤 추가할 것.
