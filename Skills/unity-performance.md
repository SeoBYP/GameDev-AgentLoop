---
name: unity-performance
title: Unity 성능 — 핫패스 규칙
always: true
targets: unity
---

## GUIDANCE
- `Update` / `FixedUpdate` / `LateUpdate` 안에서 `GetComponent`, `GameObject.Find`,
  `FindObjectOfType`, `FindFirstObjectByType`, `Camera.main` 을 호출하지 않는다.
  → `Awake` 또는 `Start` 에서 한 번 캐싱해 필드에 담아 두고 재사용한다.
- 매 프레임 도는 코드에서 **GC 할당을 만들지 않는다.** 문자열 결합/보간, LINQ,
  `new` 로 만드는 임시 컬렉션·람다 캡처가 대표적인 원인이다.
- 코루틴에서 `new WaitForSeconds(...)` 를 루프 안에서 만들지 말고, 필드에 캐싱해 재사용한다.
- 거리 비교는 `Vector3.Distance(a, b) < r` 대신 `(a - b).sqrMagnitude < r * r` 을 쓴다(제곱근 회피).
- `Debug.Log` 는 릴리스 핫패스에서 비싸다. 매 프레임 로그를 남기지 않는다.
- 몸통이 빈 `Update()` 는 아예 선언하지 않는다. Unity 는 빈 메시지 메서드도 호출한다.
- 잦은 생성·파괴(`Instantiate`/`Destroy`)는 오브젝트 풀로 대체한다.
- `transform.position` 같은 프로퍼티를 한 프레임에 여러 번 읽지 말고 지역 변수에 담아 쓴다.

## CHECKS
- id: no-getcomponent-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \bGetComponents?\s*<
  message: Update 계열에서 GetComponent 를 호출하지 마세요. Awake/Start 에서 캐싱한 참조를 사용하세요.

- id: no-find-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \b(GameObject\.Find|FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType)\b
  message: Update 계열에서 Find 계열 탐색을 호출하지 마세요. 참조는 직렬화 필드나 Awake 캐싱으로 확보하세요.

- id: no-camera-main-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \bCamera\.main\b
  message: Update 계열에서 Camera.main 을 쓰지 마세요. Awake/Start 에서 캐싱한 Camera 참조를 사용하세요.

- id: no-debuglog-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \bDebug\.Log
  message: Update 계열에서 Debug.Log 를 호출하지 마세요(매 프레임 로그는 비쌉니다).

- id: no-linq-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \.(Where|Select|OrderBy|FirstOrDefault|Any|All|ToList|ToArray)\s*\(
  message: Update 계열에서 LINQ 를 쓰지 마세요(GC 할당 발생). for 루프로 대체하세요.
