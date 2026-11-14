# -*- coding: utf-8 -*-
"""Whole-campus demo: two teaching buildings + canteen + site slab + shaded 3D shot."""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "client"))
from revit import call  # noqa: E402

TEACHING = {
    "length_mm": 48000,
    "depth_mm": 18000,
    "corridor_width_mm": 2400,
    "bay_mm": 8000,
    "roof": True,
    "levels": [
        {"name": "F1", "elevation_mm": 0, "height_mm": 4500},
        {"name": "F2", "elevation_mm": 4500, "height_mm": 3600},
        {"name": "F3", "elevation_mm": 8100, "height_mm": 3600},
    ],
}

SCHOOL = {
    "site": True,
    "site_margin_mm": 15000,
    "site_floor_type": "常规楼板 - 400mm",
    "blocks": [
        dict(TEACHING, name="东教学楼", origin=[0, 0]),
        dict(TEACHING, name="西教学楼", origin=[60000, 0]),
        {
            "name": "食堂",
            "origin": [24000, 30000],
            "length_mm": 30000,
            "depth_mm": 18000,
            "corridor_width_mm": 2400,
            "bay_mm": 6000,
            "roof": True,
            "levels": [{"name": "C1", "elevation_mm": 0, "height_mm": 5000}],
        },
    ],
}


def main():
    hello = call("hello")
    if not hello.get("ok"):
        print(hello)
        return 1
    print("document:", hello["data"]["document"]["title"])

    res = call("build.school", SCHOOL, timeout=1500)
    if not res.get("ok"):
        print("build.school FAILED:", res.get("error"))
        return 1

    data = res["data"]
    print("blocks:", data["blocks"])
    print("counts:", json.dumps(data["counts"], ensure_ascii=False))
    print("room area: %.1f m2" % data["total_room_area_m2"])
    print("site:", json.dumps(data["site"], ensure_ascii=False))
    print("failures:", data["failure_count"])

    shot = call("view.export", {"path": r"E:\revit-ai\shots\campus-shaded", "view_type": "3d", "shaded": True}, timeout=180)
    print("export:", json.dumps(shot.get("data") or shot.get("error"), ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
