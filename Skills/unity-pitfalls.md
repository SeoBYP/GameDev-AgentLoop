---
name: unity-pitfalls
title: Unity 함정 — 자주 틀리는 것들
always: true
targets: unity
---

## GUIDANCE
- **물리 프레임**: `FixedUpdate` 에서는 `Time.deltaTime` 이 아니라 `Time.fixedDeltaTime` 을 쓴다.
  (`Time.deltaTime` 은 FixedUpdate 안에서 fixedDeltaTime 을 반환하긴 하지만, 의도를 드러내려면 명시적으로 쓴다.)
- **런타임 파괴**: 런타임 코드에서는 `Destroy` 를 쓴다. `DestroyImmediate` 는 에디터 전용이며
  런타임에서 쓰면 예기치 못한 동작·경고를 유발한다.
- **이벤트 누수**: `+=` 로 구독했으면 `OnDisable`/`OnDestroy` 에서 반드시 `-=` 로 해지한다.
  해지하지 않으면 파괴된 오브젝트가 계속 호출되어 `MissingReferenceException` 이나 누수가 난다.
- **초기화 순서**: 자기 자신의 초기화는 `Awake`, 다른 오브젝트 참조가 필요한 초기화는 `Start` 에서 한다.
  `Awake` 순서는 보장되지 않는다.
- **코루틴**: 게임오브젝트가 비활성화되면 코루틴은 멈춘다. 비활성 중에도 돌아야 하면 코루틴에 의존하지 않는다.
- **부동소수 비교**: `float` 를 `==` 로 비교하지 않는다. `Mathf.Approximately` 또는 허용 오차를 쓴다.
- **직렬화 필드 널 가드**: 인스펙터에서 연결되지 않았을 수 있는 참조는 사용 전에 널 검사하거나,
  `Awake` 에서 검사해 명확한 에러를 남긴다.
- **입력 값 방어**: 체력·자원 같은 값은 항상 유효 범위로 클램프한다(`Mathf.Clamp`, `Mathf.Max/Min`).
  음수·최대치 초과가 조용히 통과하면 런타임 버그가 된다.

## CHECKS
- id: no-deltatime-in-fixedupdate
  scope: FixedUpdate
  forbid: \bTime\.deltaTime\b
  message: FixedUpdate 에서는 Time.fixedDeltaTime 을 사용하세요.

- id: no-destroyimmediate-in-runtime
  scope: *
  forbid: \bDestroyImmediate\s*\(
  message: 런타임 스크립트에서 DestroyImmediate 를 쓰지 마세요(에디터 전용). Destroy 를 사용하세요.

- id: no-empty-update
  scope: Update, FixedUpdate, LateUpdate
  forbid-empty-body: true
  message: 몸통이 빈 Update 계열 메서드는 선언하지 마세요. Unity 가 빈 메서드도 매 프레임 호출합니다.
