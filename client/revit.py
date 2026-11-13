#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Revit AI bridge client.

usage:
    python revit.py ping
    python revit.py hello
    python revit.py <cmd> '{"k": "v"}'
"""
import json
import os
import sys
import urllib.error
import urllib.request

PORT = 8787
BASE = "http://127.0.0.1:%d" % PORT


def token_path():
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return os.path.join(base, "RevitAI", "token.txt")


def read_token():
    path = token_path()
    if not os.path.exists(path):
        raise SystemExit("token not found: %s (is Revit running with RevitAI loaded?)" % path)
    with open(path, "r", encoding="utf-8") as fh:
        return fh.read().strip()


def call(cmd, args=None, timeout=45):
    payload = json.dumps({"cmd": cmd, "args": args or {}, "id": 1}).encode("utf-8")
    req = urllib.request.Request(
        BASE + "/rpc",
        data=payload,
        headers={"Content-Type": "application/json", "X-Auth-Token": read_token()},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", "replace")
        return {"ok": False, "http_status": exc.code, "error": raw}
    except urllib.error.URLError as exc:
        raise SystemExit("cannot reach Revit bridge on 127.0.0.1:%d -> %s" % (PORT, exc.reason))


def main(argv):
    cmd = argv[1] if len(argv) > 1 else "ping"
    args = json.loads(argv[2]) if len(argv) > 2 else {}
    out = call(cmd, args)
    print(json.dumps(out, ensure_ascii=False, indent=2))
    return 0 if out.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
