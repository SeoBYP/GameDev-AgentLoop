# GameDev-AgentLoop

[한국어](README_ko.md)

[![CI](https://github.com/SeoBYP/GameDev-AgentLoop/actions/workflows/ci.yml/badge.svg)](https://github.com/SeoBYP/GameDev-AgentLoop/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-6000.x-black.svg?logo=unity)](https://unity.com/)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg?logo=dotnet)](https://dotnet.microsoft.com/)
![Claude Code](https://img.shields.io/badge/Claude_Code-555?logo=claude)
![Codex](https://img.shields.io/badge/Codex-111?logo=openai)

> AI writes Unity code. This loop **runs it, measures it, and makes the AI fix it** — until it
> compiles, behaves correctly, and fits a frame budget.

Most AI coding demos stop at *"it generated code."* In game development the real questions start
right after: **Does it compile? Does it actually behave? Is it fast enough?**
This orchestrator answers those by *measuring*, and feeds every failure back until it passes.

![Claude Code as the backend — generate, apply, and pass compile verification in one step](docs/images/claude-backend-run.png)

---

## What it actually verifies

Each layer enforces a stricter definition of "done". Every one of these is exercised by a
deterministic demo you can run yourself, without an API key.

| Layer | What passing the previous layer still misses | How this catches it |
|---|---|---|
| ③-a **Compile** | *plausible code that doesn't build* | real compiler errors via `recompile_status` |
| ③-b **Runtime behavior** | *compiles, but the logic is wrong* | **Unity Test Runner** (or a PlayMode `eval` assert) |
| ③-b′ **Scenario & input** | *looks right if you only check one frame* | `[UnityTest]` coroutines + **virtual input injection** |
| ③-c **Time budget** | *correct, but allocates every frame* | run the hot path 50k times and **measure elapsed time** |
| ③-c′ **Render budget** | *fast, but explodes draw calls* | measure the **draw-call / triangle increase** it leaves in the scene |
| ①-b **Domain quality** | *runs, but is built badly* | static skill checks **reject it before it is applied** |

Verification **persists as an asset** — the AI writes PlayMode tests alongside the implementation,
and those tests stay in your repo to catch regressions on every later run.

### A real run — "correct" is not the same as "fast enough"

```
step 1  ③ compile passed          ✅
        ③ PlayMode assert passed  ✅     <- the behavior is correct
        ③ perf budget exceeded    ❌  ScoreTracker: 50000 calls in 41.03ms (0.82µs/call)
                                       [profile: drawCalls=24 mono=943MB cpuFrame=2.55ms]
step 2  ③ perf budget passed      ✅  ScoreTracker: 50000 calls in 11.82ms (0.24µs/call)

✅ success — behavior AND performance verified in 2 steps
```

An implementation that allocated a new `List` on every call **passed behavior verification**, got
caught by measurement, and was repaired into something **3.5× faster**. That is a measurement,
not a static analyzer's guess.

---

## Quickstart

### Prerequisites

| | Why |
|---|---|
| **Unity 6** (6000.x) + [`com.unity.pipeline`](https://docs.unity3d.com/) | lets the loop drive the *running* editor — recompile, PlayMode, `eval`, profiler stats |
| **Unity CLI** (`unity`) | the bridge to that in-editor server |
| **.NET 10 SDK** | builds and runs the orchestrator |
| **A brain** — one of: `claude` CLI · `codex` CLI · `ANTHROPIC_API_KEY` | the CLIs need no API key, just a normal login |

> The target Unity project **must be open in the Editor** — the pipeline server runs inside it.
> Verify with `unity pipeline list` (look for a reachable server).

### Install

The NuGet package is not published yet, so build the tool from source:

```bash
git clone https://github.com/SeoBYP/GameDev-AgentLoop.git
cd GameDev-AgentLoop
dotnet pack Orchestrator -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg GameDev.AgentLoop
```

### Use it on your own Unity project

```bash
cd /path/to/your/unity/project
agentloop --init
```

`--init` creates a runtime and a test assembly definition. This matters: a Unity test `.asmdef`
**cannot reference `Assembly-CSharp`**, so in a project without assembly definitions PlayMode tests
can never compile. Without this step the loop silently falls back to one-shot `eval` asserts.
Existing files are never overwritten.

```bash
agentloop --claude "A health component with current/max HP that clamps at both ends"
```

### See the loop work without any API key

Each demo reproduces one class of failure and repairs it, deterministically:

```bash
agentloop --demo         # compile error        -> self-repair
agentloop --demo-play    # compiles, behaves wrong
agentloop --demo-skills  # violates a domain rule -> rejected before apply
agentloop --demo-perf    # correct, but allocates on the hot path
agentloop --demo-draw    # fast, but floods draw calls
```

Run `agentloop --help` for the full option list.

### Every run is recorded

Runs are written to `<project>/.agentloop/runs/<runId>/` — a span trace, the model's raw replies,
compiler output, and a manifest of the settings that were in effect. That record is the substrate
everything else is built on: replaying what happened, and later learning from it.

```bash
agentloop --demo-perf --trace   # print the span tree when the run finishes
agentloop --show-trace          # rebuild and print the most recent run's tree
```

```
run 20260817-004257  "A ScoreTracker component; Record(int) may be called every fr…"  ✅ 2 step(s), 66.7s
  backend scripted:demo · target unity:6000.5.4f1 · skills client-architecture,unity-performance,unity-pitfalls
├─ phase step 1                                      ✅   34.1s
│  ├─ Generate                                       ✅     9ms  1 file edit(s)  → spans/s003/reply.txt
│  ├─ SkillCheck                                     ✅     7ms  9 checks
│  ├─ Apply                                          ✅   561ms  applied 1 file(s), recompile triggered
│  ├─ VerifyCompile                                  ✅    1.9s
│  ├─ VerifyAssert                                   ✅   19.4s  AI-written
│  └─ VerifyPerf                                     ❌   12.0s  50000 calls in 50.27ms  [1 error(s)]
└─ phase step 2
   └─ VerifyPerf                                     ✅   12.2s  50000 calls in 14.31ms
```

Each span carries **whose fault** an outcome was — the model's, the infrastructure's, or nobody's —
so aggregating a run tells you where the time and the mistakes actually went. See
[ARCHITECTURE §6](docs/ARCHITECTURE.md).

---

## How it works

```
goal (natural language)
 → ① generate    backend emits full files (text out, no tools)
 → ①-b check     static domain rules — reject before touching the project
 → ② apply       write files + trigger recompile
 → ③ verify      compile → tests/assert → time budget → render budget
 → ④ feedback    failures go back into the next prompt
 → ⑤ judge       pass → done / fail → ① (bounded by --max-steps)
```

**The loop is ours.** AI backends are used as pure `context → text` generators with **no tools** —
applying, verifying, retrying, and judging all belong to the orchestrator. That is what makes the
backend genuinely swappable.

```
        ┌──────────── Orchestrator (C#) — owns the loop ────────────┐
        │  ① generate → ①-b check → ② apply → ③ verify → ④ feedback │
        └──┬─────────────────────────────────────┬──────────────────┘
     IAgentBackend (brain)                 IExecTarget (hands)
   ├ ClaudeCodeBackend   no key          ├ UnityEditorTarget   client (unity CLI)
   ├ CodexBackend        no key          └ UgsTarget           backend (ugs CLI + REST)
   ├ ApiBackend          API key
   └ ScriptedBackend     demos
```

- **Agent-agnostic, demonstrated.** Claude Code and Codex — two different CLI agents — drove the
  same loop with **zero loop-code changes**.
- **Different hands need different work and different proof.** The Unity target writes C# and
  verifies by compiling and entering PlayMode; the UGS target writes Cloud Code JS and verifies by
  deploying and calling it over REST. So `IExecTarget` **declares its own** generation brief and
  supported verifications (inspect it with `--print-prompt`).

| | `--target unity` | `--target ugs` |
|---|---|---|
| Output | C# components | Cloud Code JS |
| First check | compile | **deploy** (`ugs deploy`) |
| Runtime check | PlayMode tests / assert | **script invocation** (Cloud Code REST) |
| Performance | measured hot path | — |

---

## Domain skills

`Skills/*.md` are portable markdown files with two halves:

- **`GUIDANCE`** — injected into the system prompt (prevention)
- **`CHECKS`** — static checks run **before** the code is applied (enforcement)

They ship with the tool, so they apply to any project you point it at. A project-local `Skills/`
folder takes precedence if you want your own rules.

```bash
agentloop --list-skills      # see which skills and checks would apply
agentloop --skills off       # disable them
```

> When adding a check, target **mistakes models actually make**, not things that are bad in theory —
> and measure the false-positive rate against existing output first. Our first five checks caught
> nothing at all; modern models are good at the basics.

---

## Honest limitations

- **`eval` is not sandboxed.** Verification snippets run inside *your* editor process, which the
  orchestrator does not own. `SnippetGuard` statically blocks file/process/network/registry access,
  but string tricks can bypass it. Use `--tests-only` to eliminate temporary snippets entirely and
  verify only through compiled, reviewable test files.
- **Absolute millisecond budgets are machine-dependent.** They are useful as *relative* signals; the
  same code measured 13.9ms and 11.9ms across runs, so budgets need generous margins.
- **Memory is diagnostic, not a budget.** Unity's Mono uses the Boehm GC, so `monoUsedBytes` can go
  *down* under load — unusable as a pass/fail criterion. Time and render metrics are used instead.
- **UGS invocation is verified; nothing else about your cloud project is.**
- **Not published to NuGet yet** — install from source (above).
- Runtime console output is currently Korean; `--help` is English.

---

## Documentation

| Path | Contents | Language |
|---|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Execution graph, trace tree, and self-improvement design (Mermaid diagrams) | EN · [KO](docs/ARCHITECTURE.ko.md) |
| [Orchestrator/](Orchestrator/README.md) | The loop itself — contracts, backends, targets, verification | 🇰🇷 |
| [Skills/](Skills/README.md) | Domain knowledge layer — guidance and static checks | 🇰🇷 |
| [docs/DESIGN.md](docs/DESIGN.md) | Decision log — *why* each choice was made | 🇰🇷 |
| [docs/WORKLOG.md](docs/WORKLOG.md) | Build log — what broke and how it was fixed | 🇰🇷 |

> `DESIGN` and `WORKLOG` are development logs kept in Korean.
> Translations are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

One rule matters more than the rest: **this project does not claim what it has not measured.**
If you add a verification layer or a budget, include the measurement that shows it works, and label
anything unverified as such.

## License

[MIT](LICENSE)
