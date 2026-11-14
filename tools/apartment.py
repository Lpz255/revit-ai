# -*- coding: utf-8 -*-
"""Build a real little apartment: 8x5 m, one partition, door, windows, rooms.

Phase 1  apply_plan  -> 5 walls + floor in ONE transaction (all-or-nothing)
Phase 2  door / windows hosted on those walls, then two Room elements
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "client"))
from revit import call  # noqa: E402


def ok(res):
    if not res.get("ok"):
        print("  FAIL:", res.get("error"))
        return None
    return res["data"]


def main():
    hello = call("hello")
    if not hello.get("ok"):
        print(hello)
        return 1
    level = hello["data"]["levels"][0]["name"]
    print("level:", level)

    W, H = 8000, 5000
    px = 5000  # partition x

    plan = {
        "ops": [
            {"cmd": "wall.create", "args": {"p1": [0, 0], "p2": [W, 0], "level": level, "height_mm": 3000, "type": "常规 - 200mm"}},
            {"cmd": "wall.create", "args": {"p1": [W, 0], "p2": [W, H], "level": level, "height_mm": 3000, "type": "常规 - 200mm"}},
            {"cmd": "wall.create", "args": {"p1": [W, H], "p2": [0, H], "level": level, "height_mm": 3000, "type": "常规 - 200mm"}},
            {"cmd": "wall.create", "args": {"p1": [0, H], "p2": [0, 0], "level": level, "height_mm": 3000, "type": "常规 - 200mm"}},
            {"cmd": "wall.create", "args": {"p1": [px, 0], "p2": [px, H], "level": level, "height_mm": 3000, "type": "内部 - 123mm 隔断(1 小時)"}},
            {"cmd": "floor.create", "args": {"points": [[0, 0], [W, 0], [W, H], [0, H]], "level": level}},
        ]
    }

    res = call("apply_plan", plan, timeout=180)
    if not res.get("ok"):
        print("apply_plan FAILED (whole plan rolled back):", res.get("error"))
        return 1

    ids = [r.get("id") for r in res["data"]["results"]]
    print("apply_plan ok, %d elements: %s" % (res["data"]["count"], ids))
    south, east, north, west, partition, floor = ids

    print("\ndoors / windows:")
    print("  window S1 :", ok(call("window.create", {"wall_id": south, "point": [2000, 0], "type": "M_固定:0915 x 1220mm"})))
    print("  window S2 :", ok(call("window.create", {"wall_id": south, "point": [6500, 0], "type": "M_固定:0915 x 1220mm"})))
    print("  window N  :", ok(call("window.create", {"wall_id": north, "point": [4000, H], "type": "M_固定:0610 x 1220mm"})))
    print("  door      :", ok(call("door.create", {"wall_id": partition, "point": [px, 2500], "type": "M_单扇 - 与墙齐:0864 x 2134mm"})))

    print("\nrooms:")
    print("  living    :", ok(call("room.create", {"point": [2500, 2500], "level": level, "name": "客厅", "number": "01"})))
    print("  bedroom   :", ok(call("room.create", {"point": [6500, 2500], "level": level, "name": "卧室", "number": "02"})))
    return 0


if __name__ == "__main__":
    sys.exit(main())
