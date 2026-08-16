<!-- Thanks for contributing. Korean or English is fine throughout. -->

## What this changes

<!-- One or two sentences. What does the loop do now that it did not before? -->

## Evidence

<!--
  The project rule: it does not claim what it has not measured.
  If this adds or changes a verification layer, a check, or a budget, paste the run that shows it
  working — the failure it catches, and the repair. If you cannot measure it yet, say so here and
  label it accordingly; an honest unmeasured change is fine, a silently unmeasured one is not.
-->

```
paste the relevant run output
```

## Demo regression

The five demos are the regression suite for the loop. They are deterministic (scripted backend, no
API key) and must reach the **same verdict** as before this change. They need a live Unity Editor,
so CI cannot run them — please run them locally and tick what you checked.

- [ ] `agentloop --demo` — compile self-repair
- [ ] `agentloop --demo-play` — wrong behavior caught at runtime
- [ ] `agentloop --demo-skills` — domain rule rejection
- [ ] `agentloop --demo-perf` — time budget
- [ ] `agentloop --demo-draw` — render budget
- [ ] `dotnet build Orchestrator` — 0 warnings, 0 errors
- [ ] Unity PlayMode test suite (only if you touched `Assets/`)

## Checklist

- [ ] No secrets committed (`.env` stays gitignored; `.env.example` holds placeholders only)
- [ ] Anything designed but unverified is labelled `[planned]` / `[unmeasured]`
- [ ] Docs updated if behavior changed (`README.md` and `README_ko.md` are kept in sync)
