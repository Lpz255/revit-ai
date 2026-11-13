# revit-ai

让 AI 直接驱动 Revit 建模的本地桥。Revit 侧是一个 C# 插件，对外暴露一个只监听 `127.0.0.1` 的极简 HTTP 服务；AI（Claude / Cursor / DeepSeek Harness 等）用 JSON 命令建模，不必实时编译代码进 Revit。

## 为什么是这个架构

- **Revit API 只能进程内调用**，所以必须有插件；
- 插件用 `ExternalEvent` 把 HTTP 线程的请求编组到 Revit 主线程，**每条命令一个 Transaction**，失败自动回滚；
- AI 只发结构化 JSON（`create_wall` / `hello` …），不做"生成 C# 再编译"，稳定且可校验；
- 命令返回元素 ID 与错误信息，AI 才能形成"做—看—改"的闭环。

```
AI  --HTTP/JSON-->  127.0.0.1:8787  -->  Revit 插件  --ExternalEvent-->  Revit 主线程
      (X-Auth-Token)                                      └─ Transaction（可回滚）
```

## 进度

- [x] 插件骨架（IExternalApplication）+ HTTP 桥 + Token 鉴权
- [x] `ping`（不经过 Revit 主线程，用于探活）
- [x] `hello`（文档信息 / Revit 版本 / 标高列表）
- [x] 本地自测 8/8（200/401/400、坏 JSON、未知命令）
- [ ] 建模命令：标高、轴网、墙、楼板、柱、族实例
- [ ] `apply_plan` 批量事务 + MCP 封装

## 环境

| 依赖 | 版本 |
| --- | --- |
| Revit | 2020（API 仅 .NET Framework 4.7.2） |
| 编译 | .NET SDK 8+ 或 VS 生成工具，需 net472 目标包 |
| 客户端 | Python 3.8+ |

引用 `RevitAPI.dll` / `RevitAPIUI.dll` 的路径在 `src/RevitAI.csproj` 里，按自己的安装位置改。

## 编译与安装

```bat
dotnet build src\RevitAI.csproj -c Release
install-addin.bat
```

`install-addin.bat` 会用 `RevitAI.addin.template` 生成 `%APPDATA%\Autodesk\Revit\Addins\2020\RevitAI.addin`，`<Assembly>` 指向本机 `dist\RevitAI.dll` 的绝对路径。

启动 Revit，首次会弹 **“未签名加载项”** 提示，点 **Always Load**。

## 使用

```bat
python client\revit.py ping
python client\revit.py hello
```

返回示例：

```json
{"ok": true, "cmd": "ping", "data": {"pong": true, "in_revit": true}, "ms": 7}
```

## 命令

| cmd | 需要 Revit 主线程 | 说明 |
| --- | --- | --- |
| `ping` | 否 | 探活 |
| `hello` | 是 | 文档标题/路径、Revit 版本、标高列表、元素数 |

## 安全

- 只绑定 `127.0.0.1`，不监听外网；
- 每次启动随机生成 48 位 token，写入 `%LOCALAPPDATA%\RevitAI\token.txt`，请求需带 `X-Auth-Token`；
- 日志：`%LOCALAPPDATA%\RevitAI\bridge.log`；
- 所有建模命令在 Transaction 内执行，异常回滚。

## License

MIT
