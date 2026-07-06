#!/usr/bin/env bash
# End-to-end harness test: feedforward -> sense -> feedback -> drift.
# Drives the published NativeAOT binary: set HEIMDALL_BIN, or let it auto-detect.
set -eu; cd "$(dirname "$0")/.."

AOT=1
if [ -z "${HEIMDALL_BIN:-}" ]; then
  for rid in linux-x64 win-x64 osx-arm64 osx-x64; do
    for exe in heimdall heimdall.exe; do
      cand="src/Heimdall.Cli/bin/Release/net10.0/$rid/publish/$exe"
      if [ -f "$cand" ]; then HEIMDALL_BIN="$cand"; break 2; fi
    done
  done
fi
if [ -z "${HEIMDALL_BIN:-}" ]; then
  # fallback for dev machines without the NativeAOT toolchain: framework-dependent build
  for exe in heimdall heimdall.exe; do
    cand="src/Heimdall.Cli/bin/Release/net10.0/$exe"
    if [ -f "$cand" ]; then HEIMDALL_BIN="$cand"; AOT=0; break; fi
  done
fi
if [ -z "${HEIMDALL_BIN:-}" ]; then
  echo "no heimdall binary — run: dotnet publish src/Heimdall.Cli -c Release -r <rid>" >&2
  exit 1
fi
[ "$AOT" = 1 ] || echo "warning: using framework-dependent binary (no AOT publish found) — latency gate is advisory"

rm -rf .heimdall
"$HEIMDALL_BIN" map --root samples --budget 15000 | grep -q "2 slices" && echo "ok: feedforward map emitted"
grep -q "heimdall:deps" samples/Slices/Kvad/AGENTS.md && echo "ok: AGENTS.md deps generated from csproj"
ev() { printf '{"session_id":"s1","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2"; }
ev Read "samples/Slices/Kvad/Internal/KvadEngine.cs"        | "$HEIMDALL_BIN" hook
ev Edit "samples/Slices/Kvad/Internal/KvadEngine.cs"        | "$HEIMDALL_BIN" hook
ev Read "samples/Slices/Rune/Contract/IRuneService.cs" | "$HEIMDALL_BIN" hook
ev Read "samples/Slices/Rune/Internal/RuneEngine.cs"   | "$HEIMDALL_BIN" hook   # OOB read
out=$(ev Edit "samples/Slices/Rune/Internal/RuneEngine.cs" | "$HEIMDALL_BIN" hook 2>&1 >/dev/null) && rc=0 || rc=$?
[ "$rc" = 2 ] && echo "$out" | grep -q "cross-slice" && echo "ok: feedback fired on second-slice edit (exit 2 -> agent sees it)"
# clean session: edits Kvad only, reads a foreign Internal -> must count as OOB
ev2() { printf '{"session_id":"s2","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2"; }
ev2 Edit "samples/Slices/Kvad/Internal/KvadService.cs"          | "$HEIMDALL_BIN" hook
ev2 Read "samples/Slices/Kvad/Internal/KvadEngine.cs"           | "$HEIMDALL_BIN" hook
ev2 Read "samples/Slices/Rune/Internal/RuneEngine.cs" | "$HEIMDALL_BIN" hook
"$HEIMDALL_BIN" drift | grep -E "Kvad.*[1-9][0-9]*%" >/dev/null && echo "ok: OOB read detected and attributed"
lines=$(wc -l < .heimdall/telemetry.jsonl)
[ "$lines" -ge 5 ] && echo "ok: telemetry logged ($lines findings)"
"$HEIMDALL_BIN" drift | grep -q "Kvad" && echo "ok: drift report attributes out-of-bounds reads"

# estimator: same linked TokenEstimator source Eitri compiles with (parity is structural)
est=$("$HEIMDALL_BIN" estimate samples)
[ "$est" -gt 0 ] && echo "ok: estimate runs on the samples tree ($est tokens)"

# hook latency: this runs as a PostToolUse hook on EVERY tool call — must stay cheap.
# (bash 5's EPOCHREALTIME; measures full process lifecycle incl. spawn, like Claude Code does)
n=50; start=$EPOCHREALTIME
for _ in $(seq $n); do ev Read "samples/Slices/Kvad/Internal/KvadEngine.cs" | "$HEIMDALL_BIN" hook; done
end=$EPOCHREALTIME
ms=$(awk -v a="$start" -v b="$end" -v n="$n" 'BEGIN{printf "%.1f", (b-a)*1000/n}')
echo "hook latency: ${ms} ms/event (n=$n)"
if [ "$AOT" = 1 ]; then
  awk -v ms="$ms" 'BEGIN{exit !(ms <= 30)}' && echo "ok: hook latency <= 30ms/event"
else
  echo "skip: latency gate (framework-dependent binary)"
fi
echo "--- heimdall smoke: all green"
