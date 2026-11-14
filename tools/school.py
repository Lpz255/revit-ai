# -*- coding: utf-8 -*-
"""Build a 3-storey teaching building with build.block.

    python tools/school.py

Layout: 48 m x 18 m, central 2.4 m corridor, 6 bays of 8 m,
classrooms on both sides (7.8 m deep), one door + one window per classroom.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "client"))
from revit import call  # noqa: E402

SPEC = {
    "origin": [0, 0],
    "length_mm": 48000,
    "depth_mm": 18000,
    "corridor_width_mm": 2400,
    "bay_mm": 8000,
    "levels": [
        {"name": "F1", "elevation_mm": 0, "height_mm": 4500},
        {"name": "F2", "elevation_mm": 4500, "height_mm": 3600},
        {"name": "F3", "elevation_mm": 8100, "height_mm": 3600},
    ],
    "wall_type": "常规 - 200mm",
    "floor_type": "常规楼板 - 400mm",
    "column_type": "M_W 形状-柱:W250X73",
    "door_type": "M_单扇 - 与墙齐:0915 x 2134mm",
    "window_type": "M_固定:0915 x 1220mm",
    "roof": True,
    "make_rooms": True,
    "make_doors": True,
    "make_windows": True,
    "make_columns": True,
}


def main():
    hello = call("hello")
    if not hello.get("ok"):
        print(hello)
        return 1
    print("document:", hello["data"]["document"]["title"])

    res = call("build.block", SPEC, timeout=900)
    if not res.get("ok"):
        print("build.block FAILED:", res.get("error"))
        return 1

    data = res["data"]
    print("\nblock:", json.dumps(data["block"], ensure_ascii=False))
    print("counts:", json.dumps(data["counts"], ensure_ascii=False))
    print("total room area: %.1f m2" % data["total_room_area_m2"])
    print("\nsample rooms:")
    for room in data["sample_rooms"]:
        print("  %-12s %-8s %6.2f m2" % (room["name"], room["number"], room["area_m2"]))

    shot = call("view.export", {"path": r"E:\revit-ai\shots\teaching-building"}, timeout=120)
    print("\nview.export:", json.dumps(shot.get("data") or shot.get("error"), ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
