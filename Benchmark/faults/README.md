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

## Current library

| id | caught by | original steps |
|---|---|---|
| `move-to-target` | runtime tests 4/5 | 4 |
| `object-pool` | compile error | 3 |
| `cooldown-timer` | runtime tests 6/7 | 2 |
| `damage-over-time` | runtime tests 4/5 | 2 |
| `grid-spawner` | runtime tests 6/7 | 2 |
| `wave-spawner` | runtime tests 5/6 | 2 |
| `buff-stack` | skill check, before apply | 2 |
| `inventory-stack` | skill check, before apply | 2 |

All three verification layers are represented: 2 static, 1 compile, 5 runtime.

## What this does not measure

Repair, not **prevention**. A skill's value is that the bad code never appears; injecting the fault
bypasses that. Prevention is measured by the violation rate on the goal benchmark instead.

## Adding faults

Do not write them by hand. Run the goal benchmark (or the sample game) and extract from the traces of
runs that needed a repair step. As the sample game grows, its real failures become this library —
the loop producing its own regression tests.
