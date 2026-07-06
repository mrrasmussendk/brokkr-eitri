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
check EIT001 Slices/Kvad/Internal/_c.cs 'namespace Slices.Kvad.Internal; public sealed class Leak { }' Slices/Kvad
check EIT002 Slices/Rune/Internal/_c.cs '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Kvad")]' Slices/Rune
check EIT003 Slices/Kvad/Contract/_c.cs 'namespace Slices.Kvad.Contract; public interface IL { Slices.Rune.Contract.RuneReading G(); }' Slices/Kvad
EITR_BUDGET_TEST=$(dotnet build Slices/Rune -v:q --nologo -p:Eitri_TokenBudget=100 2>&1 | grep -c "error EIT100")
[ "$EITR_BUDGET_TEST" -ge 1 ] && { echo "canary ok:   EIT100 bit"; pass=$((pass+1)); } || { echo "CANARY FAIL: EIT100"; fail=$((fail+1)); }
echo "---"; echo "canary: $pass passed, $fail failed"; exit $fail
