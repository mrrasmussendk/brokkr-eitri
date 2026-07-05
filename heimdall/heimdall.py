#!/usr/bin/env python3
"""Heimdall sensor runner — a Claude Code PostToolUse hook.

Reads the hook event from stdin, runs every sensor in heimdall/sensors/,
appends findings to .heimdall/telemetry.jsonl, and feeds warnings back to
the agent (exit code 2 + stderr) when a sensor says so.

Adding a sensor = dropping a .py file into heimdall/sensors/ that defines:

    SENSOR = "my_sensor_name"
    def observe(event: dict, ctx: dict) -> list[dict]:
        # return findings; a finding with {"feedback": "..."} is spoken to the agent

ctx contains: map (parsed .heimdall/map.json or None), repo_root.
"""
import importlib.util, json, os, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

def load_sensors():
    sensors = []
    sdir = os.path.join(HERE, "sensors")
    for f in sorted(os.listdir(sdir)):
        if not f.endswith(".py") or f.startswith("_"): continue
        spec = importlib.util.spec_from_file_location(f[:-3], os.path.join(sdir, f))
        mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
        if hasattr(mod, "observe"): sensors.append(mod)
    return sensors

def main():
    try: event = json.load(sys.stdin)
    except Exception: return 0
    map_path = os.path.join(ROOT, ".heimdall", "map.json")
    ctx = {"repo_root": ROOT,
           "map": json.load(open(map_path)) if os.path.isfile(map_path) else None}

    findings, feedback = [], []
    for sensor in load_sensors():
        try:
            for f in (sensor.observe(event, ctx) or []):
                f.setdefault("sensor", getattr(sensor, "SENSOR", sensor.__name__))
                f["ts"] = time.time()
                f["session"] = event.get("session_id", "?")
                findings.append(f)
                if f.get("feedback"): feedback.append(f["feedback"])
        except Exception as e:
            findings.append({"sensor": "heimdall", "error": str(e), "ts": time.time()})

    if findings:
        os.makedirs(os.path.join(ROOT, ".heimdall"), exist_ok=True)
        with open(os.path.join(ROOT, ".heimdall", "telemetry.jsonl"), "a") as fp:
            for f in findings: fp.write(json.dumps(f) + "\n")

    if feedback:
        print("Heimdall: " + " | ".join(feedback), file=sys.stderr)
        return 2   # exit 2 => Claude Code surfaces stderr to the agent
    return 0

if __name__ == "__main__": sys.exit(main())
