# Contributing to Eitri

- `dotnet test tests/Eitri.Analyzers.Tests` must be green.
- `bash samples/brokkr.sh` must be green — every rule needs a mutation test that proves it bites.
- New rules: reserve the next EIT id, add a descriptor in `Core.cs`, a doc page in `docs/rules/`, a unit test, a canary check, and a line in `AnalyzerReleases.Unshipped.md`.
- EIT1xx ids are reserved for context-budget rules; EIT0xx for boundary walls.
- Token estimator changes must include a calibration run against a real tokenizer (see `docs/rules/EIT100.md`) and updated accuracy numbers in the README.

- New Heimdall sensors: drop a `.py` in `heimdall/sensors/` exposing `observe(event, ctx)`; extend `heimdall/smoke_test.sh` with an event that exercises it.
- `bash heimdall/smoke_test.sh` must be green.
