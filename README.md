# revit-ai

让 AI 直接驱动 Revit 建模的本地桥。Revit 侧是一个 C# 插件，对外暴露只监听 `127.0.0.1` 的 HTTP 服务；
再包一层 **MCP server**，Codex / Claude Desktop / Cursor 这类客户端可以直接把它当工具用。

## 架构

```
Codex / Claude / Cursor            AI agent
        │  MCP (stdio, JSON-RPC)
        ▼
   mcp/server.py                  17 个工具，零依赖
        │  HTTP + token
        ▼
   Revit 插件 (C#)                ExternalEvent → Revit 主线程
        └─ 每条命令一个 Transaction（失败回滚）
```

为什么必须这样：**Revit API 只能进程内调用**，所以插件是绕不开的；而 AI 不能"生成 C# 再编译进 Revit"，
所以命令要结构化、可校验、可回滚。

## 实测结果（Revit 2020，中文建筑样板）

一条 `build.school` 生成一所学校：

| 项 | 数量 |
| --- | --- |
| 建筑 | 3 栋（2 栋 3 层教学楼 + 1 栋食堂） |
| 墙 / 柱 | 110 / 192 |
| 门 / 窗 | 82 / 82 |
| 房间 | 82 间，合计 4771 m² |
| 楼板 | 11（含 3 个屋顶 + 场地） |
| 耗时 | 约 60–90 秒，0 失败 |

## 进度

- [x] 插件骨架 + HTTP 桥 + Token 鉴权
- [x] 原子命令：标高 / 轴网 / 墙 / 板 / 柱 / 门 / 窗 / 房间 / 删除
- [x] `apply_plan` 批量单事务（失败整体回滚）
- [x] `build.block` 整栋楼参数化生成（轴网、走廊、隔墙、门窗、房间、屋顶）
- [x] `build.school` 多栋 + 场地
- [x] `view.export` 导出 PNG（AI 可以用它"看"模型）
- [x] **MCP server**（17 个工具，含 image 返回）
- [ ] 材质 / 配色（现在 Shading 是默认深灰）
- [ ] `view.create3d`（自动建三维视图）
- [ ] 楼梯、卫生间、门厅、场地道路

## 环境

| 依赖 | 版本 |
| --- | --- |
| Revit | 2020（API 仅 .NET Framework 4.7.2） |
| 编译 | .NET SDK 8+ 或 VS 生成工具，需 net472 目标包 |
| 客户端 | Python 3.8+ |

> **插件 DLL 不要放在含中文的路径下。** 实测 Revit 2020 在 `E:\0个人文件夹\...` 上会报
> 「无法运行外部应用程序」，journal 里是 `API_ERROR { Starting External Application ... }`，
> 同一 DLL 换到 `E:\revit-ai\...` 就能加载。`install-addin.bat` 会把 DLL 复制到
> `%APPDATA%\Autodesk\Revit\Addins\2020\RevitAI\`（纯 ASCII）。

## 编译与安装

```bat
dotnet build src\RevitAI.csproj -c Release
install-addin.bat
```

启动 Revit，首次弹 **「未签名加载项」** → **Always Load**。
加载成功后 `%LOCALAPPDATA%\RevitAI\` 下会出现 `token.txt` 和 `bridge.log`。

## 用 MCP 驱动（推荐）

见 [mcp/README.md](mcp/README.md)。

```toml
# ~/.codex/config.toml
[mcp_servers.revit]
command = "python"
args = ["E:\\revit-ai\\mcp\\server.py"]
```

## 直接用 HTTP / Python 客户端

```bat
python client\revit.py ping
python client\revit.py hello
python tools\school.py      :: 一栋 3 层教学楼
python tools\campus.py      :: 整个校区
```

## 命令

所有长度单位 **mm**，返回统一 `{ ok, cmd, data | error, ms }`。

| cmd | 说明 |
| --- | --- |
| `ping` / `help` / `hello` | 探活 / 命令表 / 文档信息 |
| `levels.list` / `types.list` / `views.list` | 查标高 / 类型 / 视图 |
| `level.create` / `grid.create` | 建标高 / 轴网 |
| `wall.create` / `floor.create` / `column.create` | 建墙 / 板 / 柱 |
| `door.create` / `window.create` | 门窗，宿主到墙并开洞 |
| `room.create` | 房间（可设层高） |
| `family.place` / `element.delete` | 放族 / 删构件 |
| `apply_plan` | 批量，单事务 |
| `build.block` / `build.school` | 整栋 / 整校区 |
| `view.export` | 导出 PNG |

## 安全

- 只绑定 `127.0.0.1`；
- 每次启动随机 48 位 token，写入 `%LOCALAPPDATA%\RevitAI\token.txt`，请求带 `X-Auth-Token`；
- 所有建模命令在 Transaction 内，异常回滚；警告自动吞掉，避免弹窗卡死桥。

## License

MIT
