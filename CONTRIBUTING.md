# Contributing

[한국어](CONTRIBUTING.ko.md)

Thanks for looking. Issues and pull requests are welcome.

## The one rule that matters

**This project does not claim what it has not measured.**

It exists because AI-generated code that *looks* right is worthless, so the same standard applies to
the loop itself. Concretely:

- If you add a verification layer or a budget, include the run that shows it catching a real failure.
- If something is designed but unverified, label it — the docs use `[현재]` / `[계획]` / `[미실측]`
  (current / planned / unmeasured) for exactly this.
- A plausible causal explanation is not evidence. Flip it and re-measure. (A comment in this repo
  once claimed `queueEventOnly: false` was required for touch input; testing the opposite showed the
  real requirement was advancing a frame.)

## Development setup

```bash
git clone https://github.com/SeoBYP/GameDev-AgentLoop.git
cd GameDev-AgentLoop
dotnet build Orchestrator
```

You need **Unity 6** (6000.x) with `com.unity.pipeline` installed, the **Unity CLI**, and the
**.NET 10 SDK**. Open this repo as a Unity project so the pipeline server is running —
without it, every verification step times out.

```bash
unity pipeline list      # a reachable server must be listed
```

## Before you open a PR

```bash
dotnet build Orchestrator          # must be 0 warnings, 0 errors
agentloop --demo                   # compile self-repair
agentloop --demo-play              # wrong behavior caught at runtime
agentloop --demo-skills            # domain rule rejection
agentloop --demo-perf              # time budget
agentloop --demo-draw              # render budget
```

The five demos are the regression suite for the loop: they are deterministic (scripted backend, no
API key) and each one must reach the **same verdict** as before your change. If you touch the
PlayMode tests, also run the Unity test suite.

**CI cannot run the demos** — they need a live Unity Editor with `com.unity.pipeline`. What CI does
check is everything that works without the editor: the build (warnings are errors), that skills load,
that the system prompt assembles, that the brief degrades correctly on a project with no assembly
definitions, and that the package bundles the skills. Every one of those exists because that exact
bug happened here at least once.

## Adding a domain skill

Skills live in `Skills/*.md` as portable markdown with two halves: `GUIDANCE` (injected into the
prompt) and `CHECKS` (static checks run before code is applied).

Two things to get right:

1. **Target mistakes models actually make**, not things that are bad in theory. The first five
   checks written for this repo caught nothing — modern models handle the basics.
2. **Measure the false-positive rate first.** Run your check against the existing generated code in
   `Assets/Scripts/` before proposing it.

## Style

- Comments and commit messages may be written in Korean or English.
- The CLI surface (`--help`) is English so the tool reads widely.
- `docs/DESIGN.md` and `docs/WORKLOG.md` are development logs kept in Korean.
- Keep the orchestrator **dependency-free** (BCL only). It is a deliberate constraint: it keeps the
  wire format transparent and makes it obvious that a bug is in the loop, not in a library.

## Reporting a problem

Useful reports include:

- The exact command you ran, and `agentloop --print-prompt` output if generation looked wrong
- Unity version and whether `unity pipeline list` showed a reachable server
- Which verification layer failed, and whether it was reproducible

## Security

Never commit secrets. `ANTHROPIC_API_KEY` and UGS service-account credentials belong in environment
variables or a gitignored `.env`. `.env.example` is tracked and must contain placeholders only.

Note that `eval`-based verification is **not sandboxed** — it runs inside your editor process.
`SnippetGuard` is a mitigation, not a boundary. Use `--tests-only` when auditability matters.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
