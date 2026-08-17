# Sample: Roguelike

A small grid roguelike, **written by the loop**, not by hand.

## Why this exists

Three measurement instruments have saturated (ARCHITECTURE §10):

| instrument | result | headroom |
|---|---|---|
| 18 smoke goals | 18/18, holdout mean 1.33 | 0.33 |
| 12 hard goals | 12/12 one-shot, mean 1.00 | 0 |
| 8 injected faults | 8/8 repaired, mean 2.125 | 0.125 (floor is 2, not 1) |

They share one cause: each measures **one isolated component**. At that size the loop is already
good, and the run-to-run noise (0.22 steps) is larger than the remaining headroom — so even a
perfect loop could not be shown to be better.

What is left unmeasured needs a codebase where features have to **talk to each other**:

- **§7 multi-session** — one Editor is an exclusive resource. Two sessions building two features
  against it should contend, and one should stall. That cannot happen with a single goal.
- **§8 code rot** — the inventory owner adds what quest needs, quest changes shape, and the addition
  becomes dead weight nobody removes. That needs code to accumulate over many sessions.

So this sample is **not a game demo**. It is the environment where those two failures actually occur.
The game only has to be real enough that systems form contracts.

## Why a roguelike, and why no assets

The goal is `code-first` — no art, no prefabs, no scene authoring:

- Grid + turn logic is **pure computation**, so it is verifiable exactly. No screenshot judgement,
  no "looks right".
- Seeded generation makes runs **deterministic**, so a test that passes is evidence and not luck.
- Roguelike systems are famous for cross-cutting contracts (equipment changes damage, death drops
  loot, status effects change turn order). That is exactly the §8 pressure we want.

## Architecture rule the loop must follow

```
Assets/Scripts/Core/     plain C# — game rules. No MonoBehaviour. Deterministic, seeded.
Assets/Scripts/Unity/    thin MonoBehaviour drivers — input and presentation only, no rules.
Assets/Tests/PlayMode/   Roguelike.Tests — [Test] for Core, [UnityTest] only for drivers.
```

Rules live in `Core` because that is what the contracts are between, and because `[Test]` needs no
frames — verification stays fast and honest. A driver that contains a rule is a bug.

**[now] the model honours it, and the prediction that it would not was wrong.** The brief is written
for MonoBehaviour components (`new GameObject()` + `AddComponent<T>()`, `[SerializeField]`), so slice 1
was expected to emit `AddComponent<DungeonGrid>()` and fail to compile, with the repair cost as the
measurement. It did not happen: the model wrote a plain class in `namespace Roguelike.Core` and
constructed it directly in the tests. **One step, compile ✅, 8/8 tests ✅.** So the brief's component
idiom is read as an example, not a mandate, and no softening was needed — which is the reason for not
softening it in advance.

## Setup

This is a **separate Unity project**, like `Benchmark/Sandbox`, so the game's growing codebase never
mixes with AgentLoop's own runtime and its 13 input smoke tests (the Test Runner runs the whole
suite, so sharing a project would make every game verification also run the loop's tests).

Unlike the sandbox, **generated `.cs` files are committed** — code accretion has to be visible in git
history for §8 to be measurable at all.

1. Open `Sample/Roguelike` in the Unity Editor (this starts its pipeline server).
2. Confirm it is reachable: `unity pipeline list` should list `Roguelike` with a server port.
3. Run a slice from the repo root:

```bash
dotnet run --project Orchestrator -- --claude --project Sample/Roguelike "<goal text>"
```

## Slices

Small steps, in order. Each slice is one loop run. Only the next few are written out — the rest are
directions, because the point is to discover what is needed while building, not to plan it up front.

| # | slice | new system | contract it creates | recorded |
|---|---|---|---|---|
| 1 | dungeon grid | `Grid` | — foundation | ✅ 1 step, 8/8 tests, 136s |
| 2 | actor + stats | `Actor` | — foundation | ✅ 1 step, 18/18 tests, 57s |
| 3 | movement | `Movement` | Movement → Grid (walkability, bounds) | |
| 4 | turn scheduler | `Turn` | Turn → Actor (speed) | |
| 5+ | combat · inventory · equipment · loot · vision · status | | where §7 and §8 start | |

The isolation rationale checks out on the first run: verification reported `8/8 tests passed`, i.e. only
this project's tests. In the main project the Test Runner would also have run AgentLoop's own 13 input
smoke tests on every game verification.

Slice 2 reported `18/18` — its own 10 plus slice 1's 8. The suite accumulates, so every later slice is
verified against every earlier contract. That accumulation is the substrate for §8: it is also what
will make removing anything expensive.

### Debt left on purpose, recorded not fixed

Both are correct against the goal that was asked. Neither is fixed here, because the point is to find
out whether the loop notices them when a later slice collides — and a debt fixed pre-emptively teaches
nothing.

- **slice 1 — connectivity.** Floor tiles are placed independently, so a grid can contain unreachable
  pockets. Connectivity was never asked for. Movement (slice 3) and any pathing will collide with it.
- **slice 2 — revive is silent.** `Died` is latched by a `_hasDied` flag, so it fires exactly once as
  specified. But `Heal` can raise a dead actor's health above zero, making `IsAlive` true again while
  the latch stays set — a second death then raises nothing. Literally correct, and untested: the
  generated suite has `Died_Event_Raised_Exactly_Once_On_Repeated_Lethal_Damage` but no revive case.
  Status effects and combat are where this will surface.

### Slice 1 — dungeon grid

```
Create a deterministic dungeon grid for a roguelike. Write a plain C# class DungeonGrid under
Assets/Scripts/Core/ — it must NOT be a MonoBehaviour and must not require a GameObject. The
constructor takes width, height and an int seed. Every tile is either Wall or Floor. Expose Width,
Height, TileAt(int x, int y) and IsWalkable(int x, int y), where IsWalkable returns false for any
out-of-bounds coordinate instead of throwing. Generation must be reproducible: two grids built with
the same width, height and seed have identical tiles, and two different seeds produce different
tiles. Guarantee two invariants: the outermost ring of tiles is always Wall, and at least one Floor
tile exists. A width or height below 3 must throw ArgumentOutOfRangeException.
```

### Slice 2 — actor

```
Add a plain C# Actor class under Assets/Scripts/Core/ for a roguelike. It holds a name, a grid
position (int x, int y), max health and current health, and it must NOT be a MonoBehaviour. Health
changes only through TakeDamage(int) and Heal(int); both reject negative amounts by throwing
ArgumentOutOfRangeException and both clamp so health never leaves the 0..max range. Expose IsAlive.
Raise an event when the actor dies, and raise it exactly once even if TakeDamage is called again
afterwards. MoveTo(int x, int y) updates the position and returns nothing; it does not know about
the map.
```

### The two experiments this is for

- **§7** — build slice 5 (combat) and slice 6 (inventory) in **two concurrent sessions** against the
  one open Editor, and record what actually happens to each. The prediction is that verification
  serializes and one side stalls; `NodeOutcome.Blocked` exists for exactly this and has never fired
  in a real contention case.
- **§8** — after the equipment and loot slices, ask a session to extend inventory for combat's sake,
  then change combat's shape, and measure what is left behind. Whatever the loop cannot notice is
  the argument for a rot pass.
