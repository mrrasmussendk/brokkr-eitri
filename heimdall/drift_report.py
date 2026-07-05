#!/usr/bin/env python3
import signal
try: signal.signal(signal.SIGPIPE, signal.SIG_DFL)
except Exception: pass
"""Heimdall drift report: actual agent behavior (telemetry) vs the architecture's
promise (map). Per session: edited slices E; legitimate working set = E + declared
contracts of E + kernel; everything else read = out-of-bounds. Rising OOB rate on a
slice = its boundary is cut wrong."""
import json, os, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
tele_p, map_p = os.path.join(ROOT, ".heimdall/telemetry.jsonl"), os.path.join(ROOT, ".heimdall/map.json")
if not (os.path.isfile(tele_p) and os.path.isfile(map_p)): sys.exit("need telemetry + map")
m = json.load(open(map_p))
sessions = collections.defaultdict(lambda: {"edits": set(), "reads": []})
for line in open(tele_p):
    try: f = json.loads(line)
    except Exception: continue
    s = sessions[f.get("session", "?")]
    if f.get("event") == "edit": s["edits"].add(f["slice"])
    elif f.get("event") == "read": s["reads"].append(f)

oob_by_slice = collections.Counter(); total_by_slice = collections.Counter()
for sid, s in sessions.items():
    allowed = set(s["edits"])
    for e in s["edits"]:
        allowed |= set(m["slices"].get(e, {}).get("depends_on", []))
    for r in s["reads"]:
        k = r["kind"]
        target = sorted(s["edits"])[0] if s["edits"] else "?"
        total_by_slice[target] += 1
        ok = (k == "kernel"
              or k.startswith("slice:") and k[6:] in s["edits"]
              or k.startswith("contract:") and k[9:] in allowed)
        if not ok: oob_by_slice[target] += 1

# per-session first (wandering is a session property; aggregates dilute it)
print(f"{'session':<10}{'slices edited':<26}{'reads':>7}{'oob':>6}{'oob %':>8}")
for sid, s in sorted(sessions.items()):
    allowed = set(s["edits"])
    for e in s["edits"]:
        allowed |= set(m["slices"].get(e, {}).get("depends_on", []))
    t = len(s["reads"]); o = 0
    for r in s["reads"]:
        k = r["kind"]
        ok = (k == "kernel"
              or k.startswith("slice:") and k[6:] in s["edits"]
              or k.startswith("contract:") and k[9:] in allowed)
        if not ok: o += 1
    if t:
        print(f"{sid:<10}{','.join(sorted(s['edits'])) or '-':<26}{t:>7}{o:>6}{100*o/t:>7.0f}%")
print()
print(f"{'slice':<22}{'reads':>7}{'out-of-bounds':>15}{'oob %':>8}")
for sl in sorted(total_by_slice, key=lambda x: -oob_by_slice[x]):
    t, o = total_by_slice[sl], oob_by_slice[sl]
    print(f"{sl:<22}{t:>7}{o:>15}{100*o/t:>7.0f}%")
print("\nrule of thumb: sustained oob% > 20 on a slice = re-cut that seam")
