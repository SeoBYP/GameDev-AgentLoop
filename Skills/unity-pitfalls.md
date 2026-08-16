---
name: unity-pitfalls
title: Unity pitfalls — the ones that bite
always: true
targets: unity
---

## GUIDANCE
- **Physics step**: in `FixedUpdate` use `Time.fixedDeltaTime`, not `Time.deltaTime`.
  (`Time.deltaTime` does return fixedDeltaTime inside FixedUpdate, but say what you mean.)
- **Runtime destruction**: use `Destroy` in runtime code. `DestroyImmediate` is editor-only and
  causes undefined behavior and warnings at runtime.
- **Event leaks**: if you subscribe with `+=`, unsubscribe with `-=` in `OnDisable`/`OnDestroy`.
  Otherwise destroyed objects keep getting called — `MissingReferenceException` or a leak.
- **Initialization order**: initialize yourself in `Awake`; anything that needs *other* objects goes
  in `Start`. The order in which `Awake` runs across objects is not guaranteed.
- **Coroutines** stop when the GameObject is disabled. If work must continue while disabled, do not
  rely on a coroutine.
- **Float comparison**: never compare `float` with `==`. Use `Mathf.Approximately` or an epsilon.
- **Serialized field null guards**: references that may be unwired in the inspector must be
  null-checked before use, or validated in `Awake` with a clear error.
- **Clamp inputs**: values like health or resources must always be clamped to a valid range
  (`Mathf.Clamp`, `Mathf.Max/Min`). Silently accepting negatives or overflow becomes a runtime bug.

## CHECKS
- id: no-deltatime-in-fixedupdate
  scope: FixedUpdate
  forbid: \bTime\.deltaTime\b
  message: Use Time.fixedDeltaTime inside FixedUpdate.

- id: no-destroyimmediate-in-runtime
  scope: *
  forbid: \bDestroyImmediate\s*\(
  message: Do not use DestroyImmediate in runtime scripts (it is editor-only). Use Destroy.

- id: no-empty-update
  scope: Update, FixedUpdate, LateUpdate
  forbid-empty-body: true
  message: Do not declare an Update-family method with an empty body — Unity calls it every frame anyway.
