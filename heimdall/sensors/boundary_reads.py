"""Classifies every file the agent reads: in-slice / contract / kernel / out-of-bounds.
Pure observation — logs, never interrupts. Drift analysis happens in drift_report.py."""
import os
SENSOR = "boundary_reads"

def _slice_of(path, m):
    sd = m["slices_dir"]
    if sd not in path: return None
    rest = path.split(sd, 1)[1].lstrip("/\\")
    return rest.split("/")[0].split("\\")[0] or None

def observe(event, ctx):
    if event.get("tool_name") not in ("Read", "Grep", "Glob"): return []
    m = ctx.get("map")
    if not m: return []
    path = (event.get("tool_input") or {}).get("file_path") or (event.get("tool_input") or {}).get("path") or ""
    if not path.endswith((".cs", ".csproj", ".md")): return []
    s = _slice_of(path, m)
    kind = ("kernel" if m["kernel"] in path else
            "slice:" + s if s else "outside")
    if s and "/Contract/" in path.replace("\\", "/"):
        kind = "contract:" + s
    return [{"event": "read", "path": path, "kind": kind}]
