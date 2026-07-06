# AI Agents Pay for Your Architecture in Tokens — So I Made the Compiler Send the Bill

*How I turned "our codebase is agent-friendly" from a hope into a build error, with Roslyn analyzers, a token budget, and a little Norse mythology.*

---

Here's an error message you've never seen before:

```
CSC : error EIT100: Slice 'Retskilder' worst-case agent working set is ~16,204 tokens
      (own source 12,981 + dependency contracts 2,672 + kernel 551), over the budget
      of 15,000 — split the slice or slim its contracts
```

The build just failed because a feature got too *expensive for an AI agent to understand*. Not too slow. Not too buggy. Too expensive — in tokens.

This is a post about why I think that error message should exist, and what I learned building the tool that produces it.

## Agents don't feel architectural pain

Every architecture decision you've ever made was ultimately about managing what a human has to hold in their head. Layers, modules, bounded contexts — all of it exists so that a person touching one feature doesn't need to understand fifty others.

AI coding agents changed the accounting. An agent doesn't "hold things in its head" — it loads files into a context window, and every file costs tokens. Tokens cost money, latency, and — past a point — accuracy. Your architecture now has a literal, measurable price per task.

And here's the uncomfortable part: research backs this up. SonarSource ran a controlled twin-repo study (arXiv 2605.20049) — the same features, built twice, in two different architectures. Structure didn't change whether agents *succeeded*. It substantially changed what they *cost* and where they wandered.

So the question stopped being "is my architecture clean?" and became: **what does one feature cost an agent to touch, and does that cost grow as the repo grows?**

For vertical slices with contract-only seams, the answer can be O(1) — flat, forever. For a classic layered architecture, it's O(n): every feature you add makes every other feature slightly more expensive to touch, because the central DI registration file, the shared folders, the ring listings all co-load everyone's context.

The trouble is that "can be O(1)" is a promise, and promises rot. One helpful refactor, one `InternalsVisibleTo`, one shared utilities grab-bag, and your bounded context quietly stops being bounded. Nobody notices, because nobody *feels* it. Agents don't complain. They just get slower, dumber, and more expensive.

That was the founding idea: **agents don't feel architectural pain, so convert it into a number that fails a check.**

## Forged by Eitri, guarded by Brokkr

In the Norse myth, the dwarf brothers Brokkr and Eitri forged Mjolnir under a wager that the work be flawless — while Loki, disguised as a biting fly, tried to sabotage them at the bellows. The work shipped flawless anyway.

That's the division of labor in the tool, too:

**Eitri is the smith.** A set of Roslyn analyzers that enforce vertical-slice boundaries inside the compiler. Three of the rules are walls: public types belong in `Contract/` and nothing else (EIT001), `InternalsVisibleTo` is banned outright (EIT002), and contract signatures may only expose kernel types, `System` types, and same-contract types (EIT003) — so consuming a contract never drags in transitive context.

The fourth rule is the interesting one. **EIT100 is a context-budget fitness function.** On every build, for every slice, it computes the worst-case working set an agent would need to load — the slice's own source, plus the contract surfaces of its dependencies, plus the shared kernel — and fails the build if the total exceeds your token budget.

Configuring it is three lines of MSBuild:

```xml
<PropertyGroup>
  <Eitri_SlicePrefix>Slices.</Eitri_SlicePrefix>
  <Eitri_KernelAssembly>SharedKernel</Eitri_KernelAssembly>
  <Eitri_TokenBudget>15000</Eitri_TokenBudget>  <!-- the promise, as a number -->
</PropertyGroup>
```

Two details took most of the engineering effort, and both matter more than they look.

First: EIT100 doesn't count your dependencies' *source*. It reconstructs their **contract API surfaces from assembly metadata** — exactly what an agent would need in context to consume them. That makes the number architecture-truthful: your dependencies' internals cost you zero tokens, precisely as the architecture promises. If the number is honest, people trust the gate.

Second: token counting inside a compiler can't take a dependency on a tokenizer package. So Eitri ships a dependency-free estimator calibrated against `o200k_base` — about +10% conservative on C# source. It deliberately errs toward stricter budgets. A fitness function that flatters you is worse than none.

Why the compiler, though? Because of a rule I now believe applies to agents and humans equally:

> Rules that merely warn get negotiated with. Rules that fail compilation get obeyed.

Agents are *spectacularly* good at negotiating with warnings. They will read your lint output, decide it's not relevant to the task, and move on. They cannot negotiate with `error EIT100`.

## The walls are only as real as the tests that try to breach them

Here's the part of the story I didn't plan.

**Brokkr is the bellows guard** — a mutation-test canary. It's a script that injects exactly one violation per rule into a sample repo and asserts that the build *fails*. If a config change, a tooling update, or a helpful refactor quietly disarms a wall, Brokkr notices, because a wall that doesn't bite is indistinguishable from no wall at all.

I added Brokkr on principle. Then, during development, **four of my five test suites caught a real bug in the exact thing they guard.** A silently-inert `InternalsVisibleTo`. An `.editorconfig` bulk-severity override that downgraded all the walls from errors to warnings. A broken props auto-import. A diluted drift report.

Four walls that *looked* armed and weren't — in a codebase whose entire purpose is keeping walls armed, written by someone actively thinking about walls. That's the most honest argument for mutation canaries I can offer: if it happened here, it's happening in your repo right now.

(The `.editorconfig` one is a genuine footgun worth knowing about even if you never use this tool: a blanket `dotnet_analyzer_diagnostic.severity = warning` silently downgrades analyzer errors across the board. Pin your severities explicitly.)

## Heimdall watches who walks near the walls

Compile errors are the hard enforcement channel, but they arrive late — after the agent has already wandered. So there's a third component: **Heimdall, the watchman**, a NativeAOT .NET tool that closes the loop around the agent itself.

Before an agent acts, `heimdall map` generates a dependency map (graph, budgets, fan-in) straight from the csproj files and injects a generated `depends on:` block into each slice's AGENTS.md — feedforward that can't drift, because the build graph is its source of truth.

While the agent works, a `PostToolUse` hook classifies every Read, Grep, and Edit against that map: in-slice, declared contract, kernel, or out-of-bounds. Cross into a second slice mid-session, or touch a frozen high-fan-in contract, and the agent gets warned *inside its own loop* — not in a report a human reads next week.

Afterwards, `heimdall drift` compares what the agent actually read against what the architecture promised it would need. Sustained out-of-bounds reads above 20% doesn't mean the agent misbehaved — it means the seam is cut wrong, and the architecture just told you so with data.

## Did it work? Numbers, including the one that didn't move

I stress-tested the design before committing to it: a synthetic 80-feature repo built two ways — sliced, and an identical Clean Architecture twin with the same features and the same logic.

**Token cost per single-feature task: 3.3× cheaper, and it stays that way.** ~1.6k tokens in the sliced layout versus ~5.3k in the layered twin — and the sliced number is O(1) flat as features are added, while the layered one grows linearly.

**Build mechanics held at 82 projects.** Cold build in 20 seconds on a single core; touched-slice inner loop at 1.4s. Reference assemblies did exactly what the architecture claims: an internal edit to a fan-in-22 slice recompiled *zero* consumers, while a contract edit recompiled exactly its 24-project cone.

**The harness passed a behavioral replay.** 16/16 checks against recorded agent sessions on an 81-slice repo — feedback fired precisely on scope creep, stayed silent on clean work, and hook latency held at 26 ms per event.

And one result that *didn't* flatter the design: contract-change blast radius was a wash, +7%. Slicing's win on cross-cutting changes is that migrations parallelize — not that they cost fewer tokens. I'm reporting that number too, because a fitness function you only trust when it agrees with you isn't a fitness function.

## The one-line takeaway

Your architecture makes a promise to coding agents: *any feature fits in a small, bounded context.* For most codebases, that promise is a hope. It can be a build gate instead.

Agents don't feel architectural pain. Convert it into a number that fails a check.

---

*Brokkr & Eitri is open source (MIT) — Roslyn analyzers, the Brokkr canary, and the Heimdall harness, with samples and the full measurement methodology in the repo. If you try the context-budget gate on your own codebase, I'd love to hear what number your biggest slice comes in at.*
