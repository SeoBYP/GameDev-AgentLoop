---
name: client-architecture
title: Client architecture — component design
always: true
targets: unity
---

## GUIDANCE
- **Encapsulation**: expose values to the inspector with `[SerializeField] private` fields, never
  `public` fields. If outside code must read the value, add a separate read-only property (`=> _field`).
- **Methods hold the invariants**: do not let outside code assign state fields directly.
  Changes happen through meaningful methods (`TakeDamage`, `Consume`, …) that clamp and validate.
- **Single responsibility**: one component does one thing. Health tracking, UI refresh, and death VFX
  do not belong in the same class. → The component owns the state and raises events; others react.
- **Coupling**: do not find other components by name or tag. Inject them as serialized references or
  communicate through events. A global singleton is a last resort — overusing it destroys
  testability and reuse.
- **Events**: announce state changes with `event Action<T>`. Subscribers unsubscribe in their own
  lifecycle callbacks.
- **Make intent explicit**: use named constants or serialized fields instead of magic numbers.
  Return `bool` or a result type so callers can branch on success/failure.
- **Testability**: take external dependencies like time or input as method arguments
  (e.g. `Tick(float dt)`), so verification can drive them directly instead of waiting for frames.

## CHECKS
- id: no-public-mutable-field
  scope: *
  forbid: (?m)^[ \t]*public[ \t]+(?!(?:class|struct|enum|interface|event|const|delegate|readonly|static|override|virtual|abstract|async|partial)\b)[\w<>\[\]\.,]+[ \t]+\w+[ \t]*(?:=[ \t]*[^;{()>][^;{()]*)?;
  message: Do not expose public fields. Use a [SerializeField] private field for the inspector, and add a read-only property if outside code needs to read it.
