"""Feedback sensor: warns the agent in-loop when it edits a second slice in one
session (the classic scope-creep smell) or edits a frozen high fan-in contract."""
import json, os
SENSOR = "boundary_edits"
FAN_IN_FREEZE = 10

def _slice_of(path, m):
    sd = m["slices_dir"]
    if sd not in path: return None
    rest = path.split(sd, 1)[1].lstrip("/\\")
    return rest.split("/")[0].split("\\")[0] or None

def _session_slices(ctx, session):
    tele = os.path.join(ctx["repo_root"], ".heimdall", "telemetry.jsonl")
    seen = set()
    if os.path.isfile(tele):
        for line in open(tele):
            try: f = json.loads(line)
            except Exception: continue
            if f.get("session") == session and f.get("event") == "edit":
                seen.add(f.get("slice"))
    return seen - {None}

def observe(event, ctx):
    if event.get("tool_name") not in ("Edit", "Write", "MultiEdit"): return []
    m = ctx.get("map")
    if not m: return []
    path = (event.get("tool_input") or {}).get("file_path") or ""
    s = _slice_of(path, m)
    if not s: return []
    finding = {"event": "edit", "path": path, "slice": s}
    prior = _session_slices(ctx, event.get("session_id", "?"))
    if prior and s not in prior:
        finding["feedback"] = (f"you are now editing slice '{s}' after editing {sorted(prior)} — "
                               f"cross-slice changes should go through contracts; confirm this is intentional")
    info = m["slices"].get(s, {})
    if "/Contract/" in path.replace("\\", "/") and info.get("fan_in", 0) >= FAN_IN_FREEZE:
        finding["feedback"] = (f"'{s}' Contract has fan-in {info['fan_in']} — treat as frozen: "
                               f"additive changes only, breaking changes need an expand-contract fan-out")
    return [finding]
