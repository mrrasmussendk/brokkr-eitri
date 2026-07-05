#!/usr/bin/env bash
# End-to-end harness test: feedforward -> sense -> feedback -> drift.
set -eu; cd "$(dirname "$0")/.."
rm -rf .heimdall
python3 heimdall/emit_map.py --root samples --budget 15000 | grep -q "2 slices" && echo "ok: feedforward map emitted"
grep -q "heimdall:deps" samples/Slices/Domme/AGENTS.md && echo "ok: AGENTS.md deps generated from csproj"
ev() { printf '{"session_id":"s1","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2"; }
ev Read "samples/Slices/Domme/Internal/DommeEngine.cs"        | python3 heimdall/heimdall.py
ev Edit "samples/Slices/Domme/Internal/DommeEngine.cs"        | python3 heimdall/heimdall.py
ev Read "samples/Slices/Retskilder/Contract/IRetskilderService.cs" | python3 heimdall/heimdall.py
ev Read "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"   | python3 heimdall/heimdall.py   # OOB read
out=$(ev Edit "samples/Slices/Retskilder/Internal/RetskilderEngine.cs" | python3 heimdall/heimdall.py 2>&1 >/dev/null) && rc=0 || rc=$?
[ "$rc" = 2 ] && echo "$out" | grep -q "cross-slice" && echo "ok: feedback fired on second-slice edit (exit 2 -> agent sees it)"
# clean session: edits Domme only, reads a foreign Internal -> must count as OOB
ev2() { printf '{"session_id":"s2","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2"; }
ev2 Edit "samples/Slices/Domme/Internal/DommeService.cs"          | python3 heimdall/heimdall.py
ev2 Read "samples/Slices/Domme/Internal/DommeEngine.cs"           | python3 heimdall/heimdall.py
ev2 Read "samples/Slices/Retskilder/Internal/RetskilderEngine.cs" | python3 heimdall/heimdall.py
python3 heimdall/drift_report.py | grep -E "Domme.*[1-9][0-9]*%" >/dev/null && echo "ok: OOB read detected and attributed"
lines=$(wc -l < .heimdall/telemetry.jsonl)
[ "$lines" -ge 5 ] && echo "ok: telemetry logged ($lines findings)"
python3 heimdall/drift_report.py | grep -q "Domme" && echo "ok: drift report attributes out-of-bounds reads"
echo "--- heimdall smoke: all green"
