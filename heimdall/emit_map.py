#!/usr/bin/env python3
"""Heimdall feedforward: emits .heimdall/map.json (the dependency + budget map)
and injects generated 'depends on:' lines into each slice's AGENTS.md.
The csproj graph is the ground truth — the map cannot drift from reality.

Usage: python3 heimdall/emit_map.py --root <dir-containing-Slices> [--budget 15000]
"""
import argparse, json, os, re, sys

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--budget", type=int, default=15000)
    ap.add_argument("--kernel", default="SharedKernel")
    a = ap.parse_args()

    slices_dir = None
    for cand in (os.path.join(a.root, "Slices"), os.path.join(a.root, "src", "Slices")):
        if os.path.isdir(cand): slices_dir = cand; break
    if not slices_dir: sys.exit(f"no Slices/ under {a.root}")

    slices = {}
    for n in sorted(os.listdir(slices_dir)):
        csproj = os.path.join(slices_dir, n, f"{n}.csproj")
        if not os.path.isfile(csproj): continue
        refs = sorted(set(re.findall(r'\.\.\\(\w+)\\\w+\.csproj', open(csproj).read())) - {a.kernel})
        slices[n] = {"path": os.path.relpath(os.path.join(slices_dir, n)),
                     "depends_on": refs, "budget": a.budget}
        # generated dependency line in AGENTS.md, between markers
        agents = os.path.join(slices_dir, n, "AGENTS.md")
        dep_line = f"<!--heimdall:deps-->depends on: {', '.join(refs) if refs else '(none)'} + {a.kernel}<!--/heimdall:deps-->"
        if os.path.isfile(agents):
            txt = open(agents).read()
            if "<!--heimdall:deps-->" in txt:
                txt = re.sub(r"<!--heimdall:deps-->.*?<!--/heimdall:deps-->", dep_line, txt, flags=re.S)
            else:
                txt = txt.rstrip() + "\n" + dep_line + "\n"
        else:
            txt = f"# {n}\n{dep_line}\n"
        open(agents, "w").write(txt)

    fan_in = {n: 0 for n in slices}
    for n, s in slices.items():
        for d in s["depends_on"]:
            if d in fan_in: fan_in[d] += 1
    for n in slices: slices[n]["fan_in"] = fan_in[n]

    os.makedirs(".heimdall", exist_ok=True)
    json.dump({"kernel": a.kernel, "slices_dir": os.path.relpath(slices_dir), "slices": slices},
              open(".heimdall/map.json", "w"), indent=2)
    print(f"heimdall map: {len(slices)} slices -> .heimdall/map.json; AGENTS.md deps regenerated")

if __name__ == "__main__": main()
