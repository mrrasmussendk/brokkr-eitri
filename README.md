# Brokkr & Eitri ⚒️

**Compile-time architecture enforcement for agent-ready .NET — forged by Eitri, guarded by Brokkr.**

In the myth, the dwarf brothers forged Mjolnir under a wager that the work be flawless — while Loki, as a biting fly, tried to sabotage them at the bellows. The work shipped flawless anyway.

That's the division of labor here:

- **Eitri** — the smith. Roslyn analyzers that enforce vertical-slice boundaries *inside the compiler*, plus a **context-budget fitness function**: every build, every slice, Eitri computes the worst-case working set an AI coding agent would need to load, and **fails the build if it exceeds your token budget**.
- **Brokkr** — the bellows. A mutation-test canary (`samples/brokkr.sh`) that injects one violation per rule and asserts the build *fails*. If a config change, tooling update, or helpful refactor quietly disarms a wall, Brokkr notices. (It caught two real saboteur-flies during development: a silently-inert `InternalsVisibleTo`, and an `.editorconfig` bulk-severity override that downgraded the walls to warnings.)

```
CSC : error EIT100: Slice 'Retskilder' worst-case agent working set is ~16,204 tokens
      (own source 12,981 + dependency contracts 2,672 + kernel 551), over the budget
      of 15,000 — split the slice or slim its contracts
```

Your architecture's promise to coding agents — *"any feature fits in a small, bounded context"* — stops being a hope and becomes a build gate.

## Why

Coding agents pay for architecture in tokens. Research (SonarSource's controlled twin-repo study, arXiv 2605.20049) shows codebase structure doesn't change whether agents succeed — but it substantially changes what they cost and where they wander. Vertical slices with contract-only seams keep an agent's working set **O(1)** as your project grows; layered architectures grow it **O(n)**. Eitri enforces the slice discipline that makes this true, at the only enforcement level agents respect: **the compiler**.

Rules that merely warn get negotiated with. Rules that fail compilation get obeyed — by humans and agents alike.

## The rules

| Rule | What it guards | Severity |
|---|---|---|
| **EIT001** | Public types belong in `Contract/` — everything else is `internal`. A slice's public surface *is* its contract. | error |
| **EIT002** | `InternalsVisibleTo` is banned. No visibility escape hatches through the slice wall. | error |
| **EIT003** | Contract purity: contract signatures may expose only kernel types, `System` types, and same-contract types. Consuming a contract never drags in transitive context. | error |
| **EIT100** | **Context budget**: own source + dependency contract surfaces + kernel > budget → build fails. | error |
| **EIT101** | Context budget report (the same number, always visible). | info |

EIT100 is the novel one. It reconstructs your dependencies' contract API surfaces **from assembly metadata** — exactly what an agent would need in context to consume them — so the number is architecture-truthful: your dependencies' internals cost you zero tokens, precisely as the architecture promises. Token counts use a built-in dependency-free estimator calibrated against `o200k_base` (~ +10% conservative on C# source — it errs toward stricter budgets).

## Install

```xml
<!-- Directory.Build.props -->
<ItemGroup>
  <PackageReference Include="Eitri.Analyzers" PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <Eitri_SlicePrefix>Slices.</Eitri_SlicePrefix>       <!-- namespaces Eitri polices -->
  <Eitri_KernelAssembly>SharedKernel</Eitri_KernelAssembly>
  <Eitri_TokenBudget>15000</Eitri_TokenBudget>          <!-- the promise, as a number -->
</PropertyGroup>
```

Eitri assumes the **slice-per-assembly** layout (see `samples/`): each feature is one project, `Contract/` is public, `Internal/` is internal, and two MSBuild flags do the heavy lifting alongside the analyzers:

```xml
<DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>  <!-- you see only what you declare -->
<ProduceReferenceAssembly>true</ProduceReferenceAssembly>                      <!-- consumers rebuild only on contract change -->
```

With those flags + Eitri, all four boundary properties are compile-time enforced: internals unreachable, transitive references unconstructable, no visibility escape hatches, and bounded context cost.

## The philosophy in one line

> Agents don't feel architectural pain. Convert it into a number that fails a check.

## Known footgun

A blanket `dotnet_analyzer_diagnostic.severity = warning` in .editorconfig downgrades EIT001–003 from error to warning (EIT100 is immune — no-location diagnostics can't be reconfigured per-tree). Brokkr catches exactly this: if your walls stop biting, run it. Pin severities explicitly if you use bulk overrides:

```ini
dotnet_diagnostic.EIT001.severity = error
dotnet_diagnostic.EIT002.severity = error
dotnet_diagnostic.EIT003.severity = error
```

## Run Brokkr

```bash
bash samples/brokkr.sh        # every rule must bite; a green Brokkr means the walls are real
bash samples/test-package.sh  # hermetic nupkg consumption test: rules fire + properties flow
```

Run both in CI. The walls are only as real as the test that tries to breach them.

## Heimdall — the built-in harness 👁️

The dwarfs forge the walls; **Heimdall watches who walks near them.** Three components close the harness loop (sensors → feedforward → feedback):

| | Component | What it does |
|---|---|---|
| **Feedforward** | `heimdall/emit_map.py` | Generates `.heimdall/map.json` (dependency graph + budgets + fan-in) from your csprojs and injects generated `depends on:` lines into each slice's AGENTS.md — the map agents load *before* acting, and it cannot drift because the csproj graph is its source. |
| **Sensors** | `heimdall/heimdall.py` + `heimdall/sensors/` | A Claude Code `PostToolUse` hook (see `.claude/settings.json`). Every agent Read/Grep/Edit is classified against the map (in-slice / declared contract / kernel / out-of-bounds) and logged to `.heimdall/telemetry.jsonl`. |
| **Feedback** | compile errors + in-loop warnings + `heimdall/drift_report.py` | Eitri's errors are the hard channel. Sensors add the soft channel: editing a second slice mid-session or touching a frozen high fan-in contract warns the agent *inside its loop* (hook exit 2). The drift report closes the loop: actual reads vs the architecture's promise, per slice — sustained out-of-bounds >20% means the seam is cut wrong. |

**Adding a sensor is a file drop.** Create `heimdall/sensors/my_sensor.py`:

```python
SENSOR = "my_sensor"
def observe(event: dict, ctx: dict) -> list[dict]:
    # ctx: {"map": parsed map.json, "repo_root": ...}
    # return findings; add {"feedback": "..."} to speak to the agent in-loop
    return []
```

Auto-discovered on the next tool call. `heimdall/smoke_test.sh` runs the whole loop end-to-end (in CI too).

**Adding an Eitri rule is a scaffold away:** `tools/new-rule.sh EIT004 "title"` creates the doc and release note and prints the four-step checklist (descriptor, implementation, unit test, Brokkr check).

## Roadmap

- `Eitri.Tokenizers` — pluggable exact tokenizers (o200k, Claude)
- EIT004 — declared-dependency manifest sync (AGENTS.md <-> csproj, generated)
- EIT110 — event cascade depth budget for message-based slices
- SARIF + badge output: *"agent-ready: max working set 14.2k tokens"*

## License

MIT
