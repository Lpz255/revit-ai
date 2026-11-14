# -*- coding: utf-8 -*-
"""End-to-end smoke test for the RevitAI bridge.

    python tools/smoke.py            # read-only checks
    python tools/smoke.py --build    # also draws a 4x3 m room with a floor

All coordinates are millimetres.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "client"))
from revit import call  # noqa: E402


def show(title, payload):
    print("\n[%s]" % title)
    print(json.dumps(payload, ensure_ascii=False, indent=2)[:1200])


def main():
    build = "--build" in sys.argv

    ping = call("ping")
    show("ping", ping)
    if not ping.get("ok"):
        return 1

    hello = call("hello")
    show("hello", hello)
    if not hello.get("ok"):
        return 1

    data = hello["data"]
    levels = data.get("levels") or []
    print("\nDOC: %s | levels: %d | elements: %s"
          % (data["document"]["title"], len(levels), data.get("element_count")))
    for lv in levels[:10]:
        print("   %-20s %8.1f mm" % (lv["name"], lv["elevation_mm"]))

    show("types.list walls", call("types.list", {"category": "walls"}))
    if not build:
        print("\nread-only checks passed (use --build to draw geometry)")
        return 0

    level = levels[0]["name"] if levels else None
    if not level:
        print("no levels in document")
        return 1

    print("\nbuilding 4x3 m room on level %r ..." % level)
    steps = []
    steps.append(("grid 1", call("grid.create", {"p1": [-1000, 0], "p2": [5000, 0], "name": "1"})))
    steps.append(("grid 2", call("grid.create", {"p1": [-1000, 3000], "p2": [5000, 3000], "name": "2"})))
    steps.append(("wall S", call("wall.create", {"p1": [0, 0], "p2": [4000, 0], "level": level, "height_mm": 3000})))
    steps.append(("wall E", call("wall.create", {"p1": [4000, 0], "p2": [4000, 3000], "level": level, "height_mm": 3000})))
    steps.append(("wall N", call("wall.create", {"p1": [4000, 3000], "p2": [0, 3000], "level": level, "height_mm": 3000})))
    steps.append(("wall W", call("wall.create", {"p1": [0, 3000], "p2": [0, 0], "level": level, "height_mm": 3000})))
    steps.append(("floor", call("floor.create", {"points": [[0, 0], [4000, 0], [4000, 3000], [0, 3000]], "level": level})))

    failed = 0
    for name, res in steps:
        ok = res.get("ok")
        if not ok:
            failed += 1
        print("  %-8s %s  %s" % (name, "OK " if ok else "ERR",
                                 json.dumps(res.get("data") or res.get("error"), ensure_ascii=False)[:160]))
    print("\n%s" % ("all geometry created" if failed == 0 else "%d step(s) failed" % failed))
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
