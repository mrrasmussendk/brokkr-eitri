#!/usr/bin/env bash
# Scaffolds a new Eitri rule: descriptor stub, test stub, doc, Brokkr check, release note.
# Usage: tools/new-rule.sh EIT004 "Short rule title"
set -eu; ID=$1; TITLE=$2; cd "$(dirname "$0")/.."
grep -q "$ID" src/Eitri.Analyzers/Core.cs && { echo "$ID exists"; exit 1; }
cat >> docs/rules/$ID.md << DOC
# $ID — $TITLE
TODO: rationale + fix guidance.
DOC
echo "$ID | Eitri.Architecture | Error | $TITLE" >> src/Eitri.Analyzers/AnalyzerReleases.Unshipped.md
cat << NEXT
scaffolded docs/rules/$ID.md and release note. Now:
  1. add descriptor '$ID' in src/Eitri.Analyzers/Core.cs
  2. implement + register in an analyzer (or a new file)
  3. add a unit test in tests/Eitri.Analyzers.Tests/RuleTests.cs
  4. add a Brokkr check in samples/brokkr.sh — the rule must provably bite
NEXT
