# Fault library

Recorded first responses that **really failed verification**, replayed to measure repair.

## Why this exists

The goal benchmark measures *"does the model get it right first try"* — a property of the model, not
of the loop. Measured: the `hard` tier was written specifically to be difficult and came back
**12/12 one-shot**, leaving zero headroom. Making goals harder did not help, because the loop only
does work when something is wrong.

**The loop's product is repair.** So start from broken:

```bash
agentloop --claude --bench-faults
```

Each run replays a stored faulty response as step 1, then the real model takes over. The step count
is then a measure of **the loop**, and the starting point is fixed, so run-to-run model variance is
much smaller than in the goal benchmark.

## Faults are recorded, never invented

Every file here was extracted from a real run trace: the model wrote it, and a verification layer
caught it. Nothing is hand-authored.

That rule exists because hand-picked difficulty kept being wrong — three times in one day: the first
five skill checks caught nothing, the 12ms budget was flaky, and the `hard` tier one-shot completely.

**Infrastructure failures are excluded.** Three candidates were dropped because the failure was a
domain-reload race (`'Health' could not be found`), not the model's mistake. Including them would
have taught the library a misattribution — and they would not reproduce anyway, since the cause was
environmental.

| field | meaning |
|---|---|
| `reply` | the faulty first response, verbatim |
| `caughtBy` | which node caught it — check this still reproduces |
| `failure` / `errors` | what verification reported |
| `originalSteps` | how many steps the original run needed (reference line) |
| `source` | the benchmark run it came from |

## Current library and the recorded baseline

`20260817-152343`, backend `claude-code:sonnet`, `--max-steps 6`.

| id | caught by | original | replayed |
|---|---|---|---|
| `move-to-target` | runtime tests 4/5 | 4 | **2** |
| `object-pool` | compile error | 3 | 3 |
| `cooldown-timer` | runtime tests 6/7 | 2 | 2 |
| `damage-over-time` | runtime tests 4/5 | 2 | 2 |
| `grid-spawner` | runtime tests 6/7 | 2 | 2 |
| `wave-spawner` | runtime tests 5/6 | 2 | 2 |
| `buff-stack` | skill check, before apply | 2 | 2 |
| `inventory-stack` | skill check, before apply | 2 | 2 |
| | | **2.375** | **2.125** |

All three verification layers are represented: 2 static, 1 compile, 5 runtime.

**Reproduction is exact.** All 8 faults failed at step 1 in precisely the way they originally did —
same node, same message, down to `4/5 tests passed`. Compare that with the goal benchmark, where
two runs of behavior-equivalent code moved 9 of 18 goals in both directions. Fixing the starting
point removed almost all of the variance.

### The floor is 2, not 1

Step 1 is the injected fault, so it **always** fails. The best possible score is therefore **2.00**
(fault, then one successful repair) — not 1.00.

At 2.125 observed, the headroom is **0.125 steps**, and only one fault (`object-pool`) needs more
than a single repair attempt. So this instrument is **also close to saturated**, for a reason worth
stating plainly: given a specific error message, the loop's feedback already gets a single defect
fixed on the first try, almost every time.

That makes this a strong **regression guard** — degrade the feedback text and steps rise or repairs
fail — but a weak progress meter, the same conclusion the goal benchmark reached by a different road.

### What would make it a progress meter

Faults that need **three or more repair attempts**. Only 2 of the 8 recorded ones ever did
(`object-pool` 3, `move-to-target` 4, and the latter repaired in 2 on replay). So when extracting
new faults, prefer `originalSteps >= 3`: those are the ones where feedback quality actually decides
the outcome.

## What this does not measure

Repair, not **prevention**. A skill's value is that the bad code never appears; injecting the fault
bypasses that. Prevention is measured by the violation rate on the goal benchmark instead.

## Adding faults

Do not write them by hand. Run the goal benchmark (or the sample game) and extract from the traces of
runs that needed a repair step. As the sample game grows, its real failures become this library —
the loop producing its own regression tests.
