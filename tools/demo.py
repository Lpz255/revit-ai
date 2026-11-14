# -*- coding: utf-8 -*-
"""Create a 4x3 m room offset by +6000 mm and print every element id."""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "client"))
from revit import call  # noqa: E402


def one(title, res):
    if res.get("ok"):
        print("%-10s ok   %s" % (title, res["data"]))
    else:
        print("%-10s FAIL %s" % (title, res.get("error")))
    return res.get("ok")


def main():
    hello = call("hello")
    if not hello.get("ok"):
        print(hello)
        return 1
    level = hello["data"]["levels"][0]["name"]
    x0, y0 = 6000, 0
    w, h = 4000, 3000

    print("level:", level)
    one("grid-1", call("grid.create", {"p1": [x0 - 1000, y0], "p2": [x0 + w + 1000, y0], "name": "3"}))
    one("grid-2", call("grid.create", {"p1": [x0 - 1000, y0 + h], "p2": [x0 + w + 1000, y0 + h], "name": "4"}))
    one("wall-S", call("wall.create", {"p1": [x0, y0], "p2": [x0 + w, y0], "level": level, "height_mm": 3000}))
    one("wall-E", call("wall.create", {"p1": [x0 + w, y0], "p2": [x0 + w, y0 + h], "level": level, "height_mm": 3000}))
    one("wall-N", call("wall.create", {"p1": [x0 + w, y0 + h], "p2": [x0, y0 + h], "level": level, "height_mm": 3000}))
    one("wall-W", call("wall.create", {"p1": [x0, y0 + h], "p2": [x0, y0], "level": level, "height_mm": 3000}))
    one("floor", call("floor.create", {"points": [[x0, y0], [x0 + w, y0], [x0 + w, y0 + h], [x0, y0 + h]], "level": level}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
