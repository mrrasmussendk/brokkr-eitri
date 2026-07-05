#!/usr/bin/env bash
# Mutation tests for Eitri's rules: each injected violation must FAIL the build.
set -u; cd "$(dirname "$0")"
dotnet build ../src/Eitri.Analyzers -c Release -v:q --nologo >/dev/null
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
check EIT001 Slices/Domme/Internal/_c.cs 'namespace Slices.Domme.Internal; public sealed class Leak { }' Slices/Domme
check EIT002 Slices/Retskilder/Internal/_c.cs '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Domme")]' Slices/Retskilder
check EIT003 Slices/Domme/Contract/_c.cs 'namespace Slices.Domme.Contract; public interface IL { Slices.Retskilder.Contract.RetskilderAssessment G(); }' Slices/Domme
EITR_BUDGET_TEST=$(dotnet build Slices/Retskilder -v:q --nologo -p:Eitri_TokenBudget=100 2>&1 | grep -c "error EIT100")
[ "$EITR_BUDGET_TEST" -ge 1 ] && { echo "canary ok:   EIT100 bit"; pass=$((pass+1)); } || { echo "CANARY FAIL: EIT100"; fail=$((fail+1)); }
echo "---"; echo "canary: $pass passed, $fail failed"; exit $fail
