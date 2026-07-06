# Brokkr & Eitri ⚒️

**Compile-time architecture enforcement for agent-ready .NET — forged by Eitri, guarded by Brokkr.**

In the myth, the dwarf brothers forged Mjolnir under a wager that the work be flawless — while Loki, as a biting fly, tried to sabotage them at the bellows. The work shipped flawless anyway.

That's the division of labor here:

- **Eitri** — the smith. Roslyn analyzers that enforce vertical-slice boundaries *inside the compiler*, plus a **context-budget fitness function**: every build, every slice, Eitri computes the worst-case working set an AI coding agent would need to load, and **fails the build if it exceeds your token budget**.
- **Brokkr** — the bellows. A mutation-test canary (`samples/brokkr.sh`) that injects one violation per rule and asserts the build *fails*. If a config change, tooling update, or helpful refactor quietly disarms a wall, Brokkr notices.
- **Heimdall** — the watchman. A hook-based harness that maps the repo for agents before they act, observes every file they touch, and warns them in-loop when they drift across a seam.

This isn't hypothetical caution: during development, **four of five test suites caught a real bug in the exact thing they guard** — a silently-inert `InternalsVisibleTo`, an `.editorconfig` bulk-severity override that downgraded the walls to warnings, a broken props auto-import, and drift-report dilution. Each was a wall that *looked* armed and wasn't.

```
CSC : error EIT100: Slice 'Retskilder' worst-case agent working set is ~16,204 tokens
      (own source 12,981 + dependency contracts 2,672 + kernel 551), over the budget
      of 15,000 — split the slice or slim its contracts
```

Your architecture's promise to coding agents — *"any feature fits in a small, bounded context"* — stops being a hope and becomes a build gate.

## Why

Coding agents pay for architecture in tokens. Research (SonarSource's controlled twin-repo study, arXiv 2605.20049) shows codebase structure doesn't change whether agents succeed — but it substantially changes what they cost and where they wander. Vertical slices with contract-only seams keep an agent's working set **O(1)** as your project grows; layered architectures grow it **O(n)**. Eitri enforces the slice discipline that makes this true, at the only enforcement level agents respect: **the compiler**.

Rules that merely warn get negotiated with. Rules that fail compilation get obeyed — by humans and agents alike.

## Does it actually work? (we measured)

We stress-tested the architecture empirically before committing to it — a synthetic **80-feature** repo built two ways: sliced (this design) and an identical **Clean Architecture twin** with the same features and the same logic. Three questions: does slicing actually make agents cheaper, does the build stay fast at scale, and does the harness catch what it claims to?

**Token cost per task — 3.3× cheaper, and it stays that way.** A static working-set comparison for a single-feature task:

| Layout | Tokens / single-feature task | Growth as features are added |
|---|---|---|
| **Sliced** (this design) | **~1.6k** | **O(1)** — flat |
| Clean Architecture twin | ~5.3k | **O(n)** — linear |

The layered twin pays 3.3× more mainly because its central DI-registration file and its ring-folder listings **co-load all 80 features' context** to touch one. Sliced seams keep the working set bounded no matter how big the repo gets — which is the whole promise EIT100 turns into a build gate. (Contract-change blast radius was a wash, +7% — slicing's win *there* is that migrations parallelize, not that they cost fewer tokens. We report the number that didn't move, too.)

**Build mechanics held up at 82 projects.** Cold build of all 82 projects: **20s on a single core** (est. 5–10s on real multi-core hardware). Touched-slice inner loop: **1.4s**. And reference assemblies do exactly what the architecture claims — an internal edit to a **fan-in-22** slice recompiled **zero** consumers, while a *contract* edit recompiled **exactly** its 24-project cone. Nothing over-rebuilds; nothing silently under-rebuilds.

**The harness was tested behaviorally, not just unit-tested.** We replayed recorded agent sessions against an 81-slice repo: **16/16 checks passed.** In-loop feedback fired precisely on scope creep and frozen-contract edits, stayed **silent on clean work** (no false positives), the drift report correctly flagged a session that read 100% out-of-bounds, and hook latency held at **26 ms/event** — cheap enough to run on every tool call.

The throughline: **the walls are only as real as the tests that try to breach them.**

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

## Adding a slice

A slice is one project. The shared conventions live in `Directory.Build.props` (target framework, the two boundary flags, and the `Eitri_*` settings), so an individual slice `.csproj` carries **nothing but its declared dependencies** — that's the point: what a slice can reach is exactly what you can see in one file.

**1. Scaffold the folder.** Copy `samples/Slices/Domme/` as your template. The layout is fixed and Eitri enforces it:

```
Slices/Betaling/
├─ Betaling.csproj          # dependencies only — declares the seam
├─ AGENTS.md                # one line; Heimdall fills in the deps block
├─ Contract/                # PUBLIC surface — the whole slice's API
│  └─ IBetalingService.cs
└─ Internal/                # everything else — internal by default
   ├─ BetalingService.cs    #   implements the contract
   ├─ BetalingEngine.cs     #   the actual logic
   └─ Module.cs             #   the composition-root wiring (see below)
```

**2. Write the `.csproj` — dependencies and nothing else.** The kernel plus any *sibling slices you consume, by their contract*:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Retskilder\Retskilder.csproj" />  <!-- delete for a leaf slice -->
  </ItemGroup>
</Project>
```

**3. Put your public API in `Contract/`.** Everything under `Contract/` is the slice's contract; expose only kernel types, `System` types, and same-contract types in those signatures (EIT003). Everything under `Internal/` stays `internal` — EIT001 fails the build on any `public` type outside `Contract/`.

```csharp
// Contract/IBetalingService.cs
using SharedKernel;
namespace Slices.Betaling.Contract;

public sealed record Receipt(CaseId CaseId, decimal Amount);

public interface IBetalingService
{
    Result<Receipt> Charge(CaseId caseId, decimal amount);
}
```

**4. Implement in `Internal/`, and wire it in `Module.cs`.** The implementation class is `internal sealed` and implements the contract interface. `Module` is the single sanctioned exception to EIT001 — it may be `public static` so the composition root can find it — and it resolves its slice dependencies **by their contract only**:

```csharp
// Internal/Module.cs
using SharedKernel;
using Slices.Betaling.Contract;
using Slices.Retskilder.Contract;   // a dependency's contract — never its Internal
namespace Slices.Betaling.Internal;

public static class Module
{
    public static void Register(IRegistry r) =>
        r.Register("Betaling", () => new BetalingService((IRetskilderService)r.Resolve("Retskilder")));
}
```

**5. Register the project and refresh the map.** Add it to your solution (`dotnet sln add Slices/Betaling/Betaling.csproj`), then regenerate the feedforward map so agents — and the harness — see the new seam:

```bash
heimdall map --root src --budget 15000   # rewrites .heimdall/map.json + AGENTS.md deps
```

That's the whole loop: build it (Eitri gates the walls and the token budget), `git add` it, and the next agent to touch the repo loads a map that already knows the slice exists. To scaffold a **new rule** rather than a slice, see `tools/new-rule.sh` below.

## Why agents navigate this layout faster

Agents don't navigate with an IDE's symbol resolution — they navigate with **grep**. That makes a repo's grep behavior a first-order performance property, and it's where this layout beats conventional ones twice over:

**1. Grep hits land where the work is.** In our measured comparison (identical 80-feature codebase, sliced vs. idiomatic Clean Architecture), grepping a feature name surfaced **~1,650 tokens of files in the sliced layout vs ~3,450 layered** — layered hits land inside feature-multiplexed shared files (central DI registration, shared contracts folders), so every hit drags 80 features of context along. Here, hits land in the owning slice. This matches the strongest finding in SonarSource's twin-repo study (arXiv 2605.20049): making code grep-targetable drove their largest single agent-cost reduction, while restructuring *without* a navigability gain cost tokens.

**2. Grep results are *complete* — the rules guarantee it.** In most repos, grep tells you where a symbol appears, but not whether you've found *all* the coupling: reflection-based DI, `InternalsVisibleTo`, and transitive references all create dependencies grep can't see. Under Eitri's walls, none of those can exist: EIT002 bans visibility escape hatches, `DisableTransitiveProjectReferences` bans undeclared coupling, wiring is explicit per-slice `Module.cs` (greppable, never reflection-scanned), and Heimdall generates each slice's `depends on:` line from the csproj graph itself. So `grep IRetskilderService` doesn't just find *some* consumers — **it provably finds all of them.** For an agent, that converts "search, then verify you didn't miss hidden coupling" into "search, done" — and re-verification loops are precisely where agents burn tokens (file re-reads dropped 34% on navigable code in the SonarSource data).

The conventions that keep this true are cheap and worth enforcing: file name = type name, one concept = one name everywhere (no `Sag`/`Case`/`Matter` synonyms), and message/handler pairs named so routing is a one-grep hop (`FindProvisions` → `FindProvisionsHandler`).

## Heimdall — the built-in harness 👁️

The dwarfs forge the walls; **Heimdall watches who walks near them.** Three components close the harness loop (sensors → feedforward → feedback). Heimdall ships as a NativeAOT .NET tool (`dotnet tool install heimdall`) so hook cold-start stays in the tens of milliseconds:

| | Component | What it does |
|---|---|---|
| **Feedforward** | `heimdall map` | Generates `.heimdall/map.json` (dependency graph + budgets + fan-in) from your csprojs and injects generated `depends on:` lines into each slice's AGENTS.md — the map agents load *before* acting, and it cannot drift because the csproj graph is its source. |
| **Sensors** | `heimdall hook` + `src/Heimdall.Sensors/` | A Claude Code `PostToolUse` hook (see `.claude/settings.json`). Every agent Read/Grep/Edit is classified against the map (in-slice / declared contract / kernel / out-of-bounds) and logged to `.heimdall/telemetry.jsonl`. |
| **Feedback** | compile errors + in-loop warnings + `heimdall drift` | Eitri's errors are the hard channel. Sensors add the soft channel: editing a second slice mid-session or touching a frozen high fan-in contract warns the agent *inside its loop* (hook exit 2). The drift report closes the loop: actual reads vs the architecture's promise, per session and per slice — sustained out-of-bounds >20% means the seam is cut wrong. |

**Adding a sensor is one class plus one registry line.** Implement `ISensor` in `src/Heimdall.Sensors/` and register it in `SensorRegistry.cs` (explicit registration, no reflection scanning — greppable discovery, consistent with the project's own rules):

```csharp
public sealed class MySensor : ISensor
{
    public string Name => "my_sensor";
    public IEnumerable<Finding> Observe(HookEvent e, HeimdallContext ctx)
    {
        // ctx: the parsed map + repo root
        // yield a Finding with Feedback set to speak to the agent in-loop
        yield break;
    }
}
```

**Install / build the CLI.** Either as a dotnet tool — `dotnet tool install --global Heimdall.Cli` — or from source: `dotnet publish src/Heimdall.Cli -c Release -r win-x64` (or `linux-x64`/`osx-arm64`) and point `.claude/settings.json`'s hook at `src/Heimdall.Cli/bin/Release/net10.0/<rid>/publish/heimdall(.exe) hook`. `heimdall/smoke_test.sh` runs the whole loop end-to-end against the published binary (in CI too, where it also prints measured hook latency).

**Adding an Eitri rule is a scaffold away:** `tools/new-rule.sh EIT004 "title"` creates the doc and release note and prints the four-step checklist (descriptor, implementation, unit test, Brokkr check).

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
bash samples/brokkr.sh         # every rule must bite; a green Brokkr means the walls are real
bash samples/test-package.sh   # hermetic nupkg consumption test: rules fire + properties flow
bash heimdall/smoke_test.sh    # the full harness loop: map -> sense -> feedback -> drift
```

Run all three in CI. The walls are only as real as the tests that try to breach them.

## Roadmap

- `Eitri.Tokenizers` — pluggable exact tokenizers (o200k, Claude)
- EIT004 — declared-dependency manifest sync (AGENTS.md <-> csproj, generated)
- EIT110 — event cascade depth budget for message-based slices
- SARIF + badge output: *"agent-ready: max working set 14.2k tokens"*

## License

MIT
