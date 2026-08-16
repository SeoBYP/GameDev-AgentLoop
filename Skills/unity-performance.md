---
name: unity-performance
title: Unity performance — hot path rules
always: true
targets: unity
---

## GUIDANCE
- Never call `GetComponent`, `GameObject.Find`, `FindObjectOfType`, `FindFirstObjectByType`,
  or `Camera.main` inside `Update` / `FixedUpdate` / `LateUpdate`.
  → Resolve them once in `Awake` or `Start` and cache the result in a field.
- **Do not allocate** in code that runs every frame. The usual culprits are string
  concatenation/interpolation, LINQ, temporary collections created with `new`, and lambda captures.
- In coroutines, do not build `new WaitForSeconds(...)` inside a loop — cache it in a field and reuse it.
- For distance comparisons prefer `(a - b).sqrMagnitude < r * r` over `Vector3.Distance(a, b) < r`
  (avoids a square root).
- `Debug.Log` is expensive on a release hot path. Do not log every frame.
- Do not declare an `Update()` with an empty body — Unity still calls empty message methods.
- Replace frequent `Instantiate`/`Destroy` with an object pool.
- Do not read properties like `transform.position` several times in one frame; read once into a local.

## CHECKS
- id: no-getcomponent-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \bGetComponents?\s*<
  message: Do not call GetComponent in Update-family methods. Cache the reference in Awake/Start.

- id: no-find-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \b(GameObject\.Find|FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType)\b
  message: Do not call Find-family lookups in Update-family methods. Use a serialized field or cache in Awake.

- id: no-camera-main-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \bCamera\.main\b
  message: Do not use Camera.main in Update-family methods. Cache the Camera reference in Awake/Start.

- id: no-debuglog-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \bDebug\.Log
  message: Do not call Debug.Log in Update-family methods — logging every frame is expensive.

- id: no-linq-in-update
  scope: Update, FixedUpdate, LateUpdate
  forbid: \.(Where|Select|OrderBy|FirstOrDefault|Any|All|ToList|ToArray)\s*\(
  message: Do not use LINQ in Update-family methods — it allocates. Use a for loop instead.
