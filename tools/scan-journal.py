import glob, os
base = os.path.join(os.environ["LOCALAPPDATA"], "Autodesk", "Revit", "Autodesk Revit 2020", "Journals")
f = sorted(glob.glob(os.path.join(base, "journal.*.txt")), key=os.path.getmtime, reverse=True)[0]
data = open(f, "rb").read()
try:
    text = data.decode("utf-16")
except Exception:
    text = data.decode("utf-8", "ignore")
lines = text.splitlines()
for i, l in enumerate(lines):
    if "RevitAI" in l:
        for j in range(max(0, i - 2), min(len(lines), i + 12)):
            print("%4d | %s" % (j, lines[j].strip()[:360]))
        print("-" * 40)
