# Contributing to Garmr

- `dotnet test tests/Garmr.Analyzers.Tests` must be green.
- `bash samples/canary.sh` must be green — every rule needs a mutation test that proves it bites.
- New rules: reserve the next GARM id, add a descriptor in `Core.cs`, a doc page in `docs/rules/`, a unit test, a canary check, and a line in `AnalyzerReleases.Unshipped.md`.
- GARM1xx ids are reserved for context-budget rules; GARM0xx for boundary walls.
- Token estimator changes must include a calibration run against a real tokenizer (see `docs/rules/GARM100.md`) and updated accuracy numbers in the README.
