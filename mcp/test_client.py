# -*- coding: utf-8 -*-
"""Spawn the MCP server and exercise it like Codex would."""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    proc = subprocess.Popen([sys.executable, os.path.join(HERE, "server.py")],
                            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            text=True, encoding="utf-8", bufsize=1)

    def send(obj):
        proc.stdin.write(json.dumps(obj) + "\n")
        proc.stdin.flush()

    def recv():
        while True:
            line = proc.stdout.readline()
            if not line:
                raise SystemExit("server closed")
            line = line.strip()
            if line:
                return json.loads(line)

    send({"jsonrpc": "2.0", "id": 1, "method": "initialize",
          "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                     "clientInfo": {"name": "selftest", "version": "1"}}})
    init = recv()
    print("initialize ->", json.dumps(init.get("result", {}).get("serverInfo", {}), ensure_ascii=False))
    send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    send({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
    tools = recv()["result"]["tools"]
    print("tools -> %d: %s ..." % (len(tools), ", ".join(t["name"] for t in tools[:6])))

    send({"jsonrpc": "2.0", "id": 3, "method": "tools/call",
          "params": {"name": "revit_ping", "arguments": {}}})
    res = recv()["result"]
    print("ping -> isError=%s %s" % (res["isError"], res["content"][0]["text"][:100].replace("\n", " ")))

    send({"jsonrpc": "2.0", "id": 4, "method": "tools/call",
          "params": {"name": "revit_view_export",
                     "arguments": {"path": "E:/revit-ai/shots/mcp-test", "view_type": "3d", "shaded": True}}})
    res = recv()["result"]
    kinds = [c["type"] for c in res["content"]]
    print("export -> isError=%s kinds=%s" % (res["isError"], kinds))

    proc.stdin.close()
    proc.wait(timeout=10)
    print("exit:", proc.returncode)


if __name__ == "__main__":
    main()
