# UGS 호출 검증 설계 (Cloud Code Runtime Assert)

> 현재 `UgsTarget` 은 **배포까지만** 검증한다. "배포됐다"는 "의도대로 동작한다"가 아니다 —
> Unity 타깃에서 *컴파일 통과 ≠ 동작 정상* 이었던 것과 정확히 같은 격차다.
> 이 문서는 그 격차를 메우는 **스크립트 호출 검증**의 설계다. (구현 전 설계 확정용)

---

## 1. 왜 필요한가

| | Unity 타깃 | UGS 타깃 (현재) | UGS 타깃 (이 설계) |
|---|---|---|---|
| 1차 검증 | 컴파일 | 배포 | 배포 |
| 런타임 검증 | 플레이모드 assert (`eval`) | **없음** | **스크립트 호출 + 응답 검증** |

Phase 2 에서 얻은 교훈이 그대로 적용된다: 배포만 보면 "문법은 맞지만 로직이 틀린" Cloud Code 가 통과한다.

---

## 2. 검증 경로 (공식 문서로 확인된 사실)

### 2-1. 토큰 교환 — 서비스 계정 → 베어러 토큰
```
POST https://services.api.unity.com/auth/v1/token-exchange?projectId=<PID>&environmentId=<EID>
Authorization: Basic base64("<KEY_ID>:<SECRET_KEY>")
→ { "accessToken": "..." }
```
- **`environmentId` 필수** (Cloud Code Client API 는 문서상 optional 이지만 실제로는 요구됨).
- Cloud Code 용도로는 **scope 지정 불필요**.
- 토큰 수명 **약 1시간** → 루프가 길어지면 재교환 필요.

### 2-2. 스크립트 호출 — Cloud Code Client API
```
POST https://cloud-code.services.api.unity.com/v1/projects/<PID>/scripts/<SCRIPT_NAME>
Authorization: Bearer <accessToken>
Content-Type: application/json

{ "params": { "numericParam": 123, "stringParam": "abc" } }
→ { "output": { ... } }
```
- 파라미터가 없어도 `params` 는 빈 객체로 보낸다.
- 성공 응답은 `output` 아래에 스크립트의 `return` 값이 담긴다.

### 2-3. environmentId 얻기
`ugs env list --json` 으로 환경 이름 → ID 를 해석한다(CLI 에 `list` 존재).
사용자가 `--ugs-env-id` 로 직접 줄 수도 있게 한다(조회 1회 생략).

---

## 3. 자격 증명 조달 — 설계상 가장 중요한 제약

**`ugs login` 만으로는 호출 검증을 못 한다.** 그 명령은 자격을 CLI 자체 설정에 저장하고,
오케스트레이터는 그 저장소를 읽지 않는다(읽어서도 안 된다).

→ 호출 검증을 쓰려면 **환경변수 경로**를 쓴다:

```
UGS_CLI_SERVICE_KEY_ID=<key id>
UGS_CLI_SERVICE_SECRET_KEY=<secret>
```

- `ugs` CLI 도 **같은 환경변수를 인정**하므로, 배포(CLI)와 호출(오케스트레이터)이 **하나의 자격**으로 통일된다.
- 비밀키는 토큰 교환 요청을 만들 때만 메모리에서 쓰고, **로그·에러 메시지·예외에 절대 싣지 않는다.**
  (실패 시 "토큰 교환 실패(401)" 수준으로만 보고한다.)
- 환경변수가 없으면 호출 검증을 **조용히 건너뛰지 않고**, 사전 점검에서 이유를 밝히고 배포 검증만 수행한다.

---

## 4. ASSERT 계약 — UGS 판

Unity 는 C# 스니펫을 받아 플레이모드에서 실행했다. UGS 는 **선언적 호출 명세(JSON)** 로 받는다.

````
ASSERT:
```json
[
  { "script": "grantDailyReward", "params": { "streak": 3 },  "expect": { "granted": true, "coins": 300 } },
  { "script": "grantDailyReward", "params": { "streak": 0 },  "expect": { "granted": false } },
  { "script": "grantDailyReward", "params": { "streak": -1 }, "expectError": true }
]
```
````

**왜 JS 스니펫이 아니라 JSON 인가**
로컬에서 JS 를 실행하려면 JS 엔진 의존성이 생긴다(의존성 0 원칙 위반). 그리고 검증에 필요한 건
"부르고 → 응답을 비교"뿐이라 선언적 명세로 충분하다. 실행은 **실제 클라우드에서** 일어난다.

**비교 규칙**
- `expect` 는 **부분 일치(subset match)** — 명시한 키만 비교한다.
  응답에 타임스탬프·요청ID 같은 부가 필드가 붙어도 깨지지 않는다.
- 중첩 객체는 재귀적으로 부분 일치. 배열은 길이+요소 순서까지 일치.
- `expectError: true` 는 "이 입력은 실패해야 한다"(HTTP 4xx/5xx 또는 스크립트 오류).
- 실패 메시지는 모델이 고칠 수 있을 만큼 구체적으로:
  `grantDailyReward({"streak":3}) → coins: expected 300, got 100`

---

## 5. 코드 변경 지점

| 대상 | 변경 |
|---|---|
| `VerifyKind.PlayModeAssert` | **`RuntimeAssert`** 로 개명 (타깃 중립 — Unity 는 플레이모드, UGS 는 호출) |
| `IExecTarget.VerifyLabel` (속성) | **`LabelFor(VerifyKind)`** 메서드로 통합 → Unity "컴파일"/"플레이모드 assert", UGS "배포"/"스크립트 호출" |
| `UgsTarget.Supports` | `RuntimeAssert` → **true** (자격 증명이 있을 때) |
| `UgsInvoker` *(신규)* | 토큰 교환 + 스크립트 호출 + 부분일치 비교. `HttpClient` 직통(ApiBackend 와 같은 원칙, 의존성 0) |
| `UgsTarget.GenerationBrief` | ASSERT(JSON) 규격 추가 + "검증 가능하게 만들라"는 지침(§6 참고) |
| `Program` | `--ugs-env-id` 옵션 추가 |
| `EditParser` | ASSERT 블록은 이미 언어 무관하게 파싱됨(펜스 언어 태그를 안 봄) — **변경 불필요** |

루프(`AgentLoop`)는 **바뀌지 않는다.** 이미 `Supports(kind)` 로 런타임 단계를 게이팅하고 있어서,
UGS 가 `true` 를 반환하는 순간 같은 흐름(①→①-b→②→③-a→③-b→④→⑤)이 그대로 돈다.
→ Phase 4 에서 인터페이스를 "손이 자기를 설명한다"로 만들어 둔 것이 여기서 값을 한다.

---

## 6. 정직한 한계 / 실측으로 확정할 것

1. **trusted client 호출에는 `playerId` 가 없다.**
   서비스 계정 토큰으로 부르면 플레이어 맥락이 비어 있어, `context.playerId` 에 의존하는 스크립트는
   이 방식으로 검증할 수 없다.
   → 생성 지침에 *"플레이어 식별자는 `context` 대신 `params` 로 받아 테스트 가능하게 하라"* 를 넣는다.
   (테스트 가능성을 위해 시간·입력을 인자로 받게 한 `Tick(float dt)` 와 같은 원리 — `client-architecture` 스킬)

2. **배포 = 게시인가 — 미확인.**
   `ugs cloud-code scripts publish` 가 별도 명령으로 존재한다. `ugs deploy` 가 publish 까지 하는지
   확인해야 한다. 하지 않는다면 호출 전에 publish 단계를 넣어야 한다.
   → 인증이 갖춰지면 `deploy` → `scripts get <name>` 으로 게시 상태를 확인해 결정한다.

3. **토큰 만료(약 1시간).** 만료 시각을 보관하고 필요 시 재교환한다. 루프 한 번은 대개 1시간 미만이라
   초기 구현은 "실패 시 1회 재교환" 수준으로 충분하다.

4. **실제 클라우드에 부작용이 남는다.** 배포·호출 모두 사용자 프로젝트 상태를 바꾼다.
   테스트 전용 환경(`ugs env add test`)을 쓰고 `--ugs-env` 로 지정하는 것을 기본 안내로 한다.

---

## 7. 완료 기준

- `--target ugs` 로 목표를 주면: Cloud Code JS 생성 → 배포 → **실제 호출** → 응답 부분일치 검증까지 통과.
- 일부러 로직을 틀리게 한 스크립트가 **배포는 통과하지만 호출 검증에서 잡히는** 것을 실측으로 보인다
  (Unity 쪽 `--demo-play` 와 같은 대조 — "배포 성공 ≠ 동작 정상").
