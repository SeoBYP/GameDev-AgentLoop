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
| 3 | movement | `Movement` | Movement → Grid (walkability, bounds) | ❌ 4/4 steps failed → ✅ **1 step, 27/27** once `reads` is delivered |
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

### Slice 3 — movement (the first cross-system contract)

The first slice that cannot be written without both earlier ones. It also carries an explicit
"do not modify" clause: everything it needs (`IsWalkable`, `IsAlive`, `X`/`Y`, `MoveTo`) is already
exposed, so a change to `DungeonGrid` or `Actor` would mean the loop chose to reshape a neighbour's
code rather than use its interface — which is the §8 failure in miniature, and worth catching early.

```
Add a plain C# MovementSystem class under Assets/Scripts/Core/ for the roguelike. It takes a
DungeonGrid in its constructor and moves existing Actor instances on it; it must NOT be a
MonoBehaviour. TryMove(Actor actor, int dx, int dy) returns true and updates the actor's position
only when the destination tile is walkable, and otherwise returns false and leaves the actor where
it was. A dead actor never moves: TryMove returns false. Reject a null actor with
ArgumentNullException, a null grid in the constructor with ArgumentNullException, and any dx or dy
outside -1..1 with ArgumentOutOfRangeException, so only single-step moves are possible. Moving off
the edge of the grid must return false rather than throw. Do NOT modify DungeonGrid or Actor - use
the members they already expose.
```

## What slice 3 found — the loop never tells the model what already exists

This is the finding the sample was built to produce, and it arrived on the first slice that needed two
systems at once.

**Run 1, goal exactly as written above: 4 of 4 steps failed, 325.6s.** Every step died at compile:

```
step 1  CS0246  'Actor' / 'DungeonGrid' could not be found      (13,25)(6,22)(8,27)
step 2  CS0234  'Runtime' does not exist in the namespace 'Roguelike'
step 3  CS0246  ...the identical three                          (13,25)(6,22)(8,27)
step 4  CS0246  ...the same three, shifted one line             (14,25)(7,22)(9,27)
```

The generated file shows two guesses, both wrong:

```csharp
using Roguelike;              // the types are in Roguelike.Core
public class MovementSystem   // declared in the global namespace
    Vector2Int current = actor.Position;   // Actor has no Position; it has X and Y
```

**Cause: the model had never seen `DungeonGrid.cs` or `Actor.cs`.** The loop's context is the goal, the
model's own recent replies (history window), and verification errors. Project state is not in it, so the
namespace and the member names could only be guessed. And the feedback could not repair it: `CS0246`
reports that a type is absent without saying where it lives, so the model oscillated — steps 3 and 4
reproduced step 1's error coordinates exactly.

The three saturated instruments could not have caught this. All 30 goals and all 8 faults are
single components that depend on no pre-existing code, and with nothing to reference there is no
starvation to expose.

**Run 2, same slice with the existing API surface pasted into the goal by hand: 2 steps, 25/25 tests.**
The one remaining failure is the sharpest evidence in the whole experiment:

```
step 1  CS7036  no argument given for the required parameter 'height'
                of 'DungeonGrid.DungeonGrid(int, int, int)'
```

The hand-written surface listed properties and methods but **omitted the constructors** — and the
failure landed on exactly the member that was left out, nowhere else. Fixed in one step, because
`CS7036` states the signature it wants, unlike `CS0246`.

**Run 3, the goal from run 1 verbatim, with the surface generated automatically
([`ProjectSurface`](../../Orchestrator/Targets/ProjectSurface.cs), 852 chars): 1 step, 27/27 tests, 99.1s.**

| `reads` delivered as | steps | outcome |
|---|---|---|
| nothing | 4 | ❌ gave up, `CS0246` oscillating |
| hand-written, constructors omitted | 2 | ✅ 25/25, the failure on the omitted constructor |
| generated, complete | **1** | ✅ 27/27 |

Conclusions, all three measured:

1. **Cross-system generation needs the project's existing public surface in the prompt**, and what sets
   the step count is the digest's *completeness*, not its presence. Reading files on demand is not an
   option — these backends are toolless text generators, which is why "just give it the path" was ruled
   out earlier.
2. **A repair is only as good as what the error message carries.** "Not found" cost 4 steps and never
   converged; "expected this signature" cost 1. Feedback quality is not one number.
3. **Signatures are necessary, not sufficient.** `MovementSystem` re-checks the grid bounds before
   calling `IsWalkable`, which already returns false out of bounds. Harmless, but the digest carries
   signatures and not behaviour, so the caller defended against a guarantee it could not see.

The architecture already contained the fix: §2.1 defines the node as
`{ id, goal, owns[], reads[], dependsOn[], doneCriterion }` and the loop was passing only `goal`.
Slices 1 and 2 had `reads = ∅`, which is also true of all 30 benchmark goals and all 8 faults — that is
the precise reason those three instruments saturated, and why the first node with a non-empty `reads`
failed immediately. §2.2 has been corrected: its "a later node cannot find an earlier node's types" row
named only inverted dependency order, and now names this second cause too.

### The second finding, which is worse: an unguarded run destroyed the contract and passed

Repeating the control on the same build did **not** reproduce the honest 4-step failure. It reported
success instead:

```
step 4  compile passed ✅ · Test Runner passed ✅  19/19 tests passed
✅ SUCCESS — applied and runtime-verified in 4 step(s)
```

19, where the suite should hold 27. What the model had actually done, per `git`:

| | committed before | after |
|---|---|---|
| `Actor` | name, MaxHealth, CurrentHealth, IsAlive, TakeDamage, Heal, MoveTo, `Died`, death latch | X, Y, IsDead, SetPosition, Kill |
| `DungeonGrid` | `TileType`, `(w, h, seed)`, `TileAt`, `IsWalkable` | enum gone, **seed gone**, `TileAt` gone, `SetWalkable` added |
| ActorTests / DungeonGridTests | 10 / 8 | 3 / 6 |

Unable to resolve the types, it redefined them to match its own assumptions, and then rewrote the tests
that this broke. Losing `seed` deleted slice 1's determinism contract outright.

**"All tests pass" is measured against the tests that exist after the model's edits** — and the model
writes both sides, so shrinking the contract is always a way to reach green. The suite cannot catch it;
the suite is what got rewritten. The goal text said verbatim *"Do NOT modify DungeonGrid or Actor"*, so
prompt-level prohibition is not enforcement. Full write-up in ARCHITECTURE §8.3.

The four runs together:

| surface | run | steps | tests | contracts | verdict |
|---|---|---|---|---|---|
| off | 1 | 4 | — | preserved | ❌ honest failure |
| off | 2 | 4 | 19/19 | **destroyed** | ⚠️ **false pass** |
| on | 1 | 1 | 27/27 | preserved | ✅ |
| on | 2 | 1 | 24/24 | preserved | ✅ |

Delivering `reads` fixed the starvation, 2 for 2. It does **not** close the destruction hole — those two
runs left the contracts intact, which is an observation about two runs and not a guarantee. The damaged
files were restored with `git checkout`; the damaged versions are kept outside the repo as evidence.

**Not a fault-library candidate.** `VerifyCompile` recorded it as a model `Fail`, but the root cause is
the loop starving the model. Recording it as "the model makes this mistake" would bake in exactly the
misattribution that kept the three domain-reload failures out of the library — even though its step
count is the `originalSteps >= 3` profile the library says it needs.

### The two experiments this is for

- **§7** — build slice 5 (combat) and slice 6 (inventory) in **two concurrent sessions** against the
  one open Editor, and record what actually happens to each. The prediction is that verification
  serializes and one side stalls; `NodeOutcome.Blocked` exists for exactly this and has never fired
  in a real contention case.
- **§8** — after the equipment and loot slices, ask a session to extend inventory for combat's sake,
  then change combat's shape, and measure what is left behind. Whatever the loop cannot notice is
  the argument for a rot pass.
