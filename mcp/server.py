#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""RevitAI MCP server (stdio, JSON-RPC 2.0, zero dependencies).

Exposes the local Revit bridge (127.0.0.1:8787) as MCP tools so Codex,
Claude Desktop, Cursor and friends can drive Revit modelling.

    python mcp/server.py

Config (Codex ~/.codex/config.toml):
    [mcp_servers.revit]
    command = "python"
    args = ["E:\\revit-ai\\mcp\\server.py"]
"""
import base64
import json
import os
import sys
import urllib.error
import urllib.request

PORT = 8787
VERSION = "0.6.0"
PROTOCOL_VERSION = "2024-11-05"


# ---------------------------------------------------------------- bridge

def token_path():
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return os.path.join(base, "RevitAI", "token.txt")


def read_token():
    try:
        with open(token_path(), "r", encoding="utf-8") as fh:
            return fh.read().strip()
    except OSError:
        return ""


def call_revit(cmd, args=None, timeout=900):
    body = json.dumps({"cmd": cmd, "args": args or {}}).encode("utf-8")
    req = urllib.request.Request(
        "http://127.0.0.1:%d/rpc" % PORT,
        data=body,
        headers={"Content-Type": "application/json", "X-Auth-Token": read_token()},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


# ---------------------------------------------------------------- tools

POINT = {"type": "array", "items": {"type": "number"}, "minItems": 2,
         "description": "[x, y] in millimetres"}

TOOLS = [
    {
        "name": "revit_ping",
        "description": "Check that the Revit bridge is alive (does not touch the Revit UI thread).",
        "inputSchema": {"type": "object", "properties": {}},
        "handler": lambda a: call_revit("ping", {}, 30),
    },
    {
        "name": "revit_hello",
        "description": "Document info: title, path, Revit version, levels, element count.",
        "inputSchema": {"type": "object", "properties": {}},
        "handler": lambda a: call_revit("hello", {}, 60),
    },
    {
        "name": "revit_levels",
        "description": "List levels with elevations in mm.",
        "inputSchema": {"type": "object", "properties": {}},
        "handler": lambda a: call_revit("levels.list", {}, 60),
    },
    {
        "name": "revit_types",
        "description": "List available type names. category: walls | floors | columns | doors | windows.",
        "inputSchema": {"type": "object", "properties": {"category": {"type": "string"}},
                        "required": ["category"]},
        "handler": lambda a: call_revit("types.list", a, 60),
    },
    {
        "name": "revit_wall_create",
        "description": "Create a straight wall between two points (mm).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "p1": POINT, "p2": POINT,
                "level": {"type": "string"},
                "height_mm": {"type": "number"},
                "type": {"type": "string"},
            },
            "required": ["p1", "p2"],
        },
        "handler": lambda a: call_revit("wall.create", a, 120),
    },
    {
        "name": "revit_grid_create",
        "description": "Create a grid line between two points (mm).",
        "inputSchema": {
            "type": "object",
            "properties": {"p1": POINT, "p2": POINT, "name": {"type": "string"}},
            "required": ["p1", "p2"],
        },
        "handler": lambda a: call_revit("grid.create", a, 120),
    },
    {
        "name": "revit_floor_create",
        "description": "Create a floor from a closed polygon of [x, y] points (mm).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "points": {"type": "array", "items": POINT},
                "level": {"type": "string"},
                "type": {"type": "string"},
            },
            "required": ["points"],
        },
        "handler": lambda a: call_revit("floor.create", a, 120),
    },
    {
        "name": "revit_door_create",
        "description": "Place a door hosted in an existing wall. point is projected onto the wall axis.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "wall_id": {"type": "integer"}, "point": POINT,
                "type": {"type": "string"}, "level": {"type": "string"},
            },
            "required": ["wall_id", "point"],
        },
        "handler": lambda a: call_revit("door.create", a, 120),
    },
    {
        "name": "revit_window_create",
        "description": "Place a window hosted in an existing wall.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "wall_id": {"type": "integer"}, "point": POINT,
                "type": {"type": "string"}, "level": {"type": "string"},
            },
            "required": ["wall_id", "point"],
        },
        "handler": lambda a: call_revit("window.create", a, 120),
    },
    {
        "name": "revit_room_create",
        "description": "Create a room at a point inside a closed wall loop.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "point": POINT, "level": {"type": "string"},
                "name": {"type": "string"}, "height_mm": {"type": "number"},
            },
            "required": ["point"],
        },
        "handler": lambda a: call_revit("room.create", a, 120),
    },
    {
        "name": "revit_element_delete",
        "description": "Delete elements by element id.",
        "inputSchema": {
            "type": "object",
            "properties": {"ids": {"type": "array", "items": {"type": "integer"}}},
            "required": ["ids"],
        },
        "handler": lambda a: call_revit("element.delete", a, 120),
    },
    {
        "name": "revit_apply_plan",
        "description": "Run a batch of commands in ONE transaction; any failure rolls the whole batch back.",
        "inputSchema": {
            "type": "object",
            "properties": {"ops": {"type": "array", "items": {"type": "object"}}},
            "required": ["ops"],
        },
        "handler": lambda a: call_revit("apply_plan", a, 600),
    },
    {
        "name": "revit_build_block",
        "description": (
            "Generate a whole building block in one transaction: levels, walls, corridor, "
            "partitions, slabs, structural columns, doors, windows, rooms, roof. "
            f"Pass spec={{name, origin:[x,y], length_mm, depth_mm, corridor_width_mm, bay_mm, "
            "levels:[{name,elevation_mm,height_mm}], wall_type, floor_type, door_type, "
            "window_type, column_type, roof, make_*}}."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {"spec": {"type": "object", "additionalProperties": True}},
            "required": ["spec"],
        },
        "handler": lambda a: call_revit("build.block", a.get("spec") or a, 1800),
    },
    {
        "name": "revit_build_school",
        "description": (
            "Generate a whole campus: several blocks + optional site slab. "
            "Pass spec={blocks:[<build_block spec>...], site:true, site_margin_mm}."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {"spec": {"type": "object", "additionalProperties": True}},
            "required": ["spec"],
        },
        "handler": lambda a: call_revit("build.school", a.get("spec") or a, 2400),
    },
    {
        "name": "revit_view_export",
        "description": "Export a view to PNG and return the image so you can LOOK at the model.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "path": {"type": "string"},
                "view": {"type": "string"},
                "view_type": {"type": "string", "enum": ["3d", "plan"]},
                "shaded": {"type": "boolean"},
            },
        },
        "handler": lambda a: call_revit("view.export", a, 300),
    },
    {
        "name": "revit_commands",
        "description": "List every command the Revit bridge understands (including ones not exposed as MCP tools).",
        "inputSchema": {"type": "object", "properties": {}},
        "handler": lambda a: call_revit("help", {}, 60),
    },
    {
        "name": "revit_call",
        "description": "Escape hatch: call any raw bridge command by name with an args object.",
        "inputSchema": {
            "type": "object",
            "properties": {"cmd": {"type": "string"}, "args": {"type": "object"}},
            "required": ["cmd"],
        },
        "handler": lambda a: call_revit(a.get("cmd"), a.get("args") or {}, 900),
    },
]


def content_for(name, result):
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if len(text) > 18000:
        text = text[:18000] + "\n... (truncated; narrow the query or use revit_call for details)"
    items = [{"type": "text", "text": text}]
    if name == "revit_view_export" and result.get("ok"):
        for path in (result.get("data") or {}).get("files", [])[:1]:
            try:
                with open(path, "rb") as fh:
                    items.append({"type": "image", "data": base64.b64encode(fh.read()).decode("ascii"),
                                  "mimeType": "image/png"})
            except OSError as exc:
                items.append({"type": "text", "text": "image read failed: %s" % exc})
    return items


# ---------------------------------------------------------------- mcp plumbing

def send(payload):
    sys.stdout.write(json.dumps(payload) + "\n")
    sys.stdout.flush()


def reply(req_id, result):
    send({"jsonrpc": "2.0", "id": req_id, "result": result})


def fail(req_id, code, message):
    send({"jsonrpc": "2.0", "id": req_id, "error": {"code": code, "message": message}})


def handle(msg):
    method = msg.get("method")
    req_id = msg.get("id")

    if method == "initialize":
        reply(req_id, {
            "protocolVersion": PROTOCOL_VERSION,
            "capabilities": {"tools": {"listChanged": False}},
            "serverInfo": {"name": "revit-ai", "version": VERSION},
        })
        return
    if method in ("notifications/initialized", "initialized", "notifications/cancelled"):
        return
    if method == "ping":
        reply(req_id, {})
        return
    if method == "tools/list":
        reply(req_id, {"tools": [
            {"name": t["name"], "description": t["description"], "inputSchema": t["inputSchema"]}
            for t in TOOLS
        ]})
        return
    if method == "tools/call":
        params = msg.get("params") or {}
        name = params.get("name")
        arguments = params.get("arguments") or {}
        tool = next((t for t in TOOLS if t["name"] == name), None)
        if tool is None:
            fail(req_id, -32602, "unknown tool: %s" % name)
            return
        try:
            result = tool["handler"](arguments)
        except urllib.error.URLError as exc:
            reply(req_id, {"content": [{"type": "text",
                          "text": "cannot reach Revit bridge on 127.0.0.1:%d (%s)" % (PORT, exc.reason)}],
                          "isError": True})
            return
        except Exception as exc:  # noqa: BLE001
            reply(req_id, {"content": [{"type": "text", "text": "%s: %s" % (type(exc).__name__, exc)}],
                          "isError": True})
            return
        reply(req_id, {"content": content_for(name, result), "isError": not result.get("ok", False)})
        return

    if req_id is not None:
        fail(req_id, -32601, "method not found: %s" % method)


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except ValueError:
            continue
        try:
            handle(msg)
        except Exception as exc:  # noqa: BLE001
            if msg.get("id") is not None:
                fail(msg.get("id"), -32603, str(exc))


def check():
    import platform
    report = {
        "python": platform.python_version(),
        "server_version": VERSION,
        "tools": [t["name"] for t in TOOLS],
        "token_file": token_path(),
        "token_present": bool(read_token()),
    }
    try:
        report["bridge"] = call_revit("ping", {}, 10)
    except Exception as exc:  # noqa: BLE001
        report["bridge"] = "unreachable: %s" % exc
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    if "--check" in sys.argv:
        check()
    else:
        main()
