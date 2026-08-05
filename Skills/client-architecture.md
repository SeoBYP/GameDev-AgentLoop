---
name: client-architecture
title: 클라이언트 아키텍처 — 컴포넌트 설계
always: true
targets: unity
---

## GUIDANCE
- **캡슐화**: 인스펙터 노출은 `public` 필드가 아니라 `[SerializeField] private` 필드로 한다.
  외부에 읽기가 필요하면 `public` 읽기 전용 프로퍼티(`=> _field`)를 따로 연다.
- **불변식은 메서드가 지킨다**: 상태 필드를 외부에서 직접 대입하게 두지 않는다.
  값 변경은 의미 있는 메서드(`TakeDamage`, `Consume` 등)를 통해서만 일어나고, 그 안에서 클램프·검증한다.
- **단일 책임**: 한 컴포넌트는 한 가지 일만 한다. 체력 관리와 UI 갱신과 사망 연출을 한 클래스에 넣지 않는다.
  → 상태는 컴포넌트가 갖고, 반응은 이벤트로 외부에 알린다.
- **결합도**: 다른 컴포넌트를 이름·태그로 찾지 말고, 직렬화 참조로 주입받거나 이벤트로 통신한다.
  전역 싱글턴은 최후 수단이며, 남용하면 테스트와 재사용이 무너진다.
- **이벤트**: 상태 변화는 `event Action<T>` 로 알린다. 구독자는 자신의 생명주기에서 해지한다.
- **의도 드러내기**: 매직 넘버 대신 이름 있는 상수/직렬화 필드를 쓴다.
  메서드는 성공/실패를 `bool` 이나 결과 타입으로 돌려주어 호출자가 분기할 수 있게 한다.
- **테스트 가능성**: 시간·입력 같은 외부 의존을 메서드 인자로 받으면(예: `Tick(float dt)`)
  플레이모드 검증에서 프레임을 기다리지 않고 바로 검증할 수 있다.

## CHECKS
- id: no-public-mutable-field
  scope: *
  forbid: (?m)^[ \t]*public[ \t]+(?!(?:class|struct|enum|interface|event|const|delegate|readonly|static|override|virtual|abstract|async|partial)\b)[\w<>\[\]\.,]+[ \t]+\w+[ \t]*(?:=[ \t]*[^;{()>][^;{()]*)?;
  message: public 필드를 노출하지 마세요. 인스펙터 노출은 [SerializeField] private 필드로 하고, 외부 읽기가 필요하면 읽기 전용 프로퍼티를 여세요.
