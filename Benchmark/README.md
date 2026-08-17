# Benchmark

A fixed set of goals the loop is measured against, so improvements can be **proven rather than felt**.

```bash
agentloop --claude --bench                     # every goal
agentloop --claude --bench --bench-set holdout # held-out split only
agentloop --claude --bench --bench-filter cooldown
```

## Why a held-out split

Self-improvement is unusually easy to fool yourself about: skills distilled from failures, and
budgets calibrated from measurements, are tuned on the goals you ran. Getting better on *those*
proves nothing.

So `goals.jsonl` splits into:

| `set` | Count | Used for |
|---|---|---|
| `train` | 12 | tuning skills, budgets, and routing |
| `holdout` | 6 | **the only numbers an improvement claim may cite** |

## Metrics

Three, deliberately:

- **success rate** — did the loop reach a verified pass within `--max-steps`
- **mean steps** — over *passing* goals only, since failures are truncated at the cap and would
  otherwise drag the mean in the flattering direction
- **wall clock** — mean per goal

The shape of a claim worth making:

> Held-out, 6 goals: 3.4 steps on average → **1.9 steps** after skill distillation;
> success rate 67% → 100%

## Results

`--bench` writes `results/<benchId>/summary.json` (tracked — these are the numbers) and the full
span traces to `.agentloop/bench/<benchId>/` (ignored — working data).

A baseline must be recorded **before** the loop changes, otherwise later comparison is meaningless.

### Baseline — `20260817-121624`

Backend `claude-code:sonnet`, `--max-steps 6`, all three skills, correctness verification only
(no performance budget — see [ARCHITECTURE §9.4](../docs/ARCHITECTURE.md)).

| Split | Passed | Mean steps | Mean wall clock |
|---|---|---|---|
| **holdout** | **6/6 (100%)** | **1.33** | 141.4s |
| train | 12/12 (100%) | 1.17 | 80.0s |
| all | 18/18 (100%) | 1.22 | 100.5s |

Four goals needed a repair step, and **every one of them was a real defect the loop caught** —
no infrastructure noise in the whole sweep:

| Goal | What failed on step 1 | Caught by |
|---|---|---|
| `damage-over-time` | 4/5 tests passed | Test Runner |
| `grid-spawner` | 6/7 tests passed | Test Runner |
| `wave-spawner` | 5/6 tests passed | Test Runner |
| `inventory-stack` | domain rule violation | static skill check, **before applying** |

### What this baseline can and cannot show

**It cannot show a success-rate improvement.** At 100% there is no headroom; only a *regression*
would move that number. That is useful as a guard, but it means the goal set is not currently hard
enough to prove the loop got better at succeeding.

**The signal that remains is mean steps**, and it is thin — 14 of 18 goals were one-shot, so the
whole measurable range sits between 1.00 and 1.22. A change would have to be substantial to clear
the noise.

If future work needs a sharper instrument, the honest fix is **harder goals**, not a reinterpretation
of these numbers. Until then, treat this baseline as a regression guard rather than a progress meter.

## Run it against the sandbox, not your project

`Sandbox/` is a separate, empty Unity project that exists only for benchmarking. **Do not run the
benchmark against a project that already has code in it.**

The reason is contamination, and it is not hypothetical. In this repo, 8 of the 18 goals collide by
name with existing components, and two of them (`MoveToTarget`, `Jumper`) are referenced by existing
tests. The Test Runner runs the *whole* suite, so a goal that overwrites one of those files breaks
unrelated tests and gets scored as a failure it did not cause — producing a baseline that is simply wrong.

```bash
# once: open Benchmark/Sandbox in the Unity Editor and let it import,
#       then confirm the pipeline server is up
unity pipeline list

agentloop --claude --bench --project Benchmark/Sandbox
```

The sandbox tracks only its skeleton — `ProjectSettings/`, `Packages/manifest.json`, and the two
assembly definitions — so anyone can reproduce the same environment. It deliberately copies this
project's package versions and `activeInputHandler` setting, because render budgets and virtual
input depend on both.

Between goals the runner deletes the files that goal generated (never pre-existing ones) and
recompiles, so each goal starts clean. Each goal takes minutes; a full sweep is roughly an hour
and a half.

## Adding a goal

Keep them the size of a single component, and phrase them the way a task would actually be handed
over — the loop's job is to turn that into verified code, not to parse a spec.

```json
{"id":"kebab-case","set":"train","tags":["logic","frames"],"goal":"One or two sentences."}
```

Tags are for slicing the results: `logic`, `frames`, `perf`, `render`, `input`, `collections`, `bounds`.
Add to `holdout` sparingly — every goal moved there is one you can never tune against.
