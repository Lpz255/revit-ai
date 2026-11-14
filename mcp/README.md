# RevitAI MCP server

把 Revit 插件（127.0.0.1:8787）暴露成 MCP 工具，任何支持 MCP 的客户端都能建模：
Codex CLI / Claude Desktop / Cursor / VS Code Copilot。

零依赖，纯标准库，JSON-RPC 2.0 over stdio。

## 工具（16 个）

| 工具 | 说明 |
| --- | --- |
| `revit_ping` | 探活 |
| `revit_hello` | 文档信息、标高、元素数 |
| `revit_levels` | 标高列表 |
| `revit_types` | 墙/板/柱/门/窗类型名 |
| `revit_wall_create` | 建墙 |
| `revit_grid_create` | 建轴网 |
| `revit_floor_create` | 建楼板 |
| `revit_door_create` / `revit_window_create` | 门窗宿主到墙 |
| `revit_room_create` | 建房间 |
| `revit_element_delete` | 删构件 |
| `revit_apply_plan` | 批量命令，单事务，失败全回滚 |
| `revit_build_block` | 整栋楼参数化生成 |
| `revit_build_school` | 整个校区生成 |
| `revit_view_export` | 导出 PNG，以 image content 返回 |
| `revit_commands` / `revit_call` | 命令表 / 万能 escape hatch |

## 配置

### Codex CLI（`~/.codex/config.toml`）

```toml
[mcp_servers.revit]
command = "python"
args = ["E:\\revit-ai\\mcp\\server.py"]
```

### Claude Desktop（`claude_desktop_config.json`）

```json
{
  "mcpServers": {
    "revit": {
      "command": "python",
      "args": ["E:\\revit-ai\\mcp\\server.py"]
    }
  }
}
```

## 自检

```bat
python mcp\server.py --check
python mcp\test_client.py
```

## 注意

- Revit 必须开着，插件必须在跑；Revit 弹模态对话框时调用会超时。
- 桥是单线程串行的，`build_block` 可能要几十秒，`build_school` 一两分钟，客户端要放宽超时。
- token 从 `%LOCALAPPDATA%\RevitAI\token.txt` 自动读取，不暴露给模型。
- 建模命令都带事务：批量失败整体回滚，不会留半截模型。
