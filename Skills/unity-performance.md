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
- Use the `NonAlloc` physics queries (`RaycastNonAlloc`, `OverlapSphereNonAlloc`, …) with a reusable
  results array. The plain `RaycastAll` / `OverlapSphere` variants allocate a new array per call.
- Do not build strings per frame — concatenation and `$"..."` interpolation both allocate.
- Do not sort every frame. Sort when the data changes, or keep the collection ordered on insert.

## CHECKS
<!--
  Scope note: the hot path is not only `Update`. Our own generation brief and benchmark goals ask
  for a `Tick(float deltaTime)` entry point so behavior can be verified without waiting for frames,
  and models duly reduce `Update` to `Update() => Tick(Time.deltaTime);`. Measured: of the runtime
  files generated so far, 5 put all per-frame work in `Tick` — where these checks were not looking.
  `Tick` is therefore part of the hot-path scope.

  Validation status: across 65 hot-path bodies from 76 real model responses, every check below
  produced **0 false positives**. The last three also produced 0 true positives — but no goal in the
  current benchmark uses physics queries, per-frame strings, or sorting, so they are **unvalidated
  rather than useless**. Revisit once the sample game exercises those paths.
-->
- id: no-getcomponent-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \bGetComponents?\s*<
  message: Do not call GetComponent in Update-family methods. Cache the reference in Awake/Start.

- id: no-find-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \b(GameObject\.Find|FindObjectOfType|FindObjectsOfType|FindFirstObjectByType|FindAnyObjectByType)\b
  message: Do not call Find-family lookups in Update-family methods. Use a serialized field or cache in Awake.

- id: no-camera-main-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \bCamera\.main\b
  message: Do not use Camera.main in Update-family methods. Cache the Camera reference in Awake/Start.

- id: no-debuglog-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \bDebug\.Log
  message: Do not call Debug.Log in Update-family methods — logging every frame is expensive.

- id: no-linq-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \.(Where|Select|OrderBy|FirstOrDefault|Any|All|ToList|ToArray)\s*\(
  message: Do not use LINQ in Update-family methods — it allocates. Use a for loop instead.

- id: no-allocating-physics-query-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \bPhysics2?D?\.(RaycastAll|OverlapSphere|OverlapBox|OverlapCapsule|OverlapCircleAll|OverlapAreaAll|SphereCastAll|BoxCastAll|CapsuleCastAll)\s*\(
  message: Do not use allocating physics queries per frame. Use the NonAlloc variants with a reusable results buffer.

- id: no-string-concat-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: (\$"|"[^"]*"\s*\+|\+\s*"[^"]*")
  message: Do not build strings in Update-family methods — concatenation and interpolation allocate every frame.

- id: no-sort-in-update
  scope: Update, FixedUpdate, LateUpdate, Tick
  forbid: \.(Sort|OrderBy|OrderByDescending)\s*\(|\bArray\.Sort\s*\(
  message: Do not sort every frame. Sort when the data changes, or keep the collection ordered on insert.
