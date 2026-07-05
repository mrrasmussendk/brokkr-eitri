#!/usr/bin/env bash
# Mutation tests for Garmr's rules: each injected violation must FAIL the build.
set -u; cd "$(dirname "$0")"
dotnet build ../src/Garmr.Analyzers -c Release -v:q --nologo >/dev/null
pass=0; fail=0
check() {
  echo "$3" > "$2"
  if dotnet build "$4" -v:q --nologo 2>&1 | grep -q "error $1"; then
    echo "canary ok:   $1 bit"; pass=$((pass+1))
  else
    echo "CANARY FAIL: $1 did not bite"; fail=$((fail+1))
  fi
  rm -f "$2"
}
check GARM001 Slices/Domme/Internal/_c.cs 'namespace Slices.Domme.Internal; public sealed class Leak { }' Slices/Domme
check GARM002 Slices/Retskilder/Internal/_c.cs '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Domme")]' Slices/Retskilder
check GARM003 Slices/Domme/Contract/_c.cs 'namespace Slices.Domme.Contract; public interface IL { Slices.Retskilder.Contract.RetskilderAssessment G(); }' Slices/Domme
GARMR_BUDGET_TEST=$(dotnet build Slices/Retskilder -v:q --nologo -p:Garmr_TokenBudget=100 2>&1 | grep -c "error GARM100")
[ "$GARMR_BUDGET_TEST" -ge 1 ] && { echo "canary ok:   GARM100 bit"; pass=$((pass+1)); } || { echo "CANARY FAIL: GARM100"; fail=$((fail+1)); }
echo "---"; echo "canary: $pass passed, $fail failed"; exit $fail
