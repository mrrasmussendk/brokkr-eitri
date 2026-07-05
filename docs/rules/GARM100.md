# GARM100 / GARM101 — Agent context budget
Computes each slice's worst-case agent working set every build:
`own source tokens + Σ dependency Contract API surfaces (from metadata) + kernel public surface`.
GARM101 always reports the number (info). GARM100 fails the build when it exceeds `Garmr_TokenBudget` (default 15000).
Dependency surfaces are reconstructed from assembly metadata via SymbolDisplay — dependencies' internals cost zero, exactly as the architecture promises.
Token counts use a dependency-free estimator: +10.5% vs `o200k_base` on C# source in calibration (conservative — errs toward stricter budgets). Recalibrate with `tools/calibrate.py` after estimator changes.
**Fix when over budget:** split the slice by sub-capability, split god-contracts into role interfaces, or slim the kernel.
