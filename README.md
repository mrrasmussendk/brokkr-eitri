# Garmr 🐺

**The boundary hound for agent-ready .NET architectures. Barks at compile time.**

Garmr is a Roslyn analyzer that makes vertical-slice architecture *physically unbreakable* — and adds something no other tool has: a **context-budget fitness function that runs inside the compiler**. Every build, every slice, Garmr computes the worst-case working set an AI coding agent would need to load to work on that slice, and **fails the build if it exceeds your token budget**.

```
CSC : error GARM100: Slice 'Retskilder' worst-case agent working set is ≈16,204 tokens
      (own source 12,981 + dependency contracts 2,672 + kernel 551), over the budget
      of 15,000 — split the slice or slim its contracts
```

Your architecture's promise to coding agents — *"any feature fits in a small, bounded context"* — stops being a hope and becomes a build gate.

## Why

Coding agents pay for architecture in tokens. Research (SonarSource's controlled twin-repo study, arXiv 2605.20049) shows codebase structure doesn't change whether agents succeed — but it substantially changes what they cost and where they wander. Vertical slices with contract-only seams keep an agent's working set **O(1)** as your project grows; layered architectures grow it **O(n)**. Garmr enforces the slice discipline that makes this true, at the only enforcement level agents respect: **the compiler**.

Rules that would merely warn get negotiated with. Rules that fail compilation get obeyed — by humans and agents alike.

## The rules

| Rule | What it guards | Severity |
|---|---|---|
| **GARM001** | Public types belong in `Contract/` — everything else is `internal`. A slice's public surface *is* its contract. | error |
| **GARM002** | `InternalsVisibleTo` is banned. No visibility escape hatches through the slice wall. | error |
| **GARM003** | Contract purity: contract signatures may expose only kernel types, `System` types, and same-contract types. Consuming a contract never drags in transitive context. | error |
| **GARM100** | **Context budget**: own source + dependency contract surfaces + kernel > budget → build fails. | error |
| **GARM101** | Context budget report (the same number, always visible). | info |

GARM100 is the novel one. It reconstructs your dependencies' contract API surfaces **from assembly metadata** — exactly what an agent would need in context to consume them — so the number is architecture-truthful: your dependencies' internals cost you zero tokens, precisely as the architecture promises. Token counts use a built-in dependency-free estimator calibrated against `o200k_base` (≈ +10% conservative on C# source — it errs toward stricter budgets).

## Install

```xml
<!-- Directory.Build.props -->
<ItemGroup>
  <PackageReference Include="Garmr.Analyzers" PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <Garmr_SlicePrefix>Slices.</Garmr_SlicePrefix>       <!-- namespaces Garmr polices -->
  <Garmr_KernelAssembly>SharedKernel</Garmr_KernelAssembly>
  <Garmr_TokenBudget>15000</Garmr_TokenBudget>          <!-- the promise, as a number -->
</PropertyGroup>
```

Garmr assumes the **slice-per-assembly** layout (see `samples/`): each feature is one project, `Contract/` is public, `Internal/` is internal, and two MSBuild flags do the heavy lifting alongside Garmr:

```xml
<DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>  <!-- you see only what you declare -->
<ProduceReferenceAssembly>true</ProduceReferenceAssembly>                      <!-- consumers rebuild only on contract change -->
```

With those flags + Garmr, all four boundary properties are compile-time enforced: internals unreachable, transitive references unconstructable, no visibility escape hatches, and bounded context cost.

## The philosophy in one line

> Agents don't feel architectural pain. Convert it into a number that fails a check.

## Test your guardrails

`samples/canary.sh` injects one violation per rule and asserts the build **fails** — mutation tests for the walls themselves. Run it in CI; a green canary means the hound still bites. (It caught a real hole during Garmr's own development: `InternalsVisibleTo` compiled silently until GARM002 existed.)

## Roadmap

- `Garmr.Tokenizers` — pluggable exact tokenizers (o200k, Claude)
- GARM004 — declared-dependency manifest sync (AGENTS.md ⇄ csproj, generated)
- GARM110 — event cascade depth budget for message-based slices
- SARIF + badge output: *"agent-ready: max working set 14.2k tokens"*

## License

MIT
