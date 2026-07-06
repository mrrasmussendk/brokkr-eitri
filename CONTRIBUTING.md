# Contributing to Eitri

- `dotnet test tests/Eitri.Analyzers.Tests` must be green.
- `bash samples/brokkr.sh` must be green — every rule needs a mutation test that proves it bites.
- New rules: reserve the next EIT id, add a descriptor in `Core.cs`, a doc page in `docs/rules/`, a unit test, a canary check, and a line in `AnalyzerReleases.Unshipped.md`.
- EIT1xx ids are reserved for context-budget rules; EIT0xx for boundary walls.
- Token estimator changes must include a calibration run against a real tokenizer (see `docs/rules/EIT100.md`) and updated accuracy numbers in the README.

- New Heimdall sensors: implement `ISensor` in `src/Heimdall.Sensors/` and add one line to `SensorRegistry.cs` (explicit registration, no reflection scanning — that's what keeps the CLI NativeAOT-clean); cover it in `tests/Heimdall.Tests` and extend `heimdall/smoke_test.sh` with an event that exercises it.
- `dotnet test tests/Heimdall.Tests` and `bash heimdall/smoke_test.sh` (needs `dotnet publish src/Heimdall.Cli -c Release -r <rid>` first) must be green.
