data = open(r"D:\software\revit\Revit 2020\RevitAPI.dll", "rb").read()
for s in (b"Shaded", b"Shading", b"FlatColors", b"Wireframe", b"DisplayStyle", b"Realistic"):
    print("%-14s %d" % (s.decode(), data.count(s)))
