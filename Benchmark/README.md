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

## Running it

The goals need a live Unity Editor and a real backend, and each one takes minutes — a full sweep is
roughly an hour and a half. Between goals the runner deletes the files that goal generated (never
pre-existing ones) and recompiles, so each goal starts from a clean project.

## Adding a goal

Keep them the size of a single component, and phrase them the way a task would actually be handed
over — the loop's job is to turn that into verified code, not to parse a spec.

```json
{"id":"kebab-case","set":"train","tags":["logic","frames"],"goal":"One or two sentences."}
```

Tags are for slicing the results: `logic`, `frames`, `perf`, `render`, `input`, `collections`, `bounds`.
Add to `holdout` sparingly — every goal moved there is one you can never tune against.
