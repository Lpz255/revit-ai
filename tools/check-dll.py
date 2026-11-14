data = open(r"E:\revit-ai\dist\RevitAI.dll", "rb").read()
for s in ("apply_plan", "door.create", "room.create", "0.3.0"):
    print("%-14s ascii=%d utf16=%d" % (s, data.count(s.encode()), data.count(s.encode("utf-16-le"))))
