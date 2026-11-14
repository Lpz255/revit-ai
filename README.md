# revit-ai

让 AI 直接驱动 Revit 建模的本地桥。Revit 侧是一个 C# 插件，对外暴露一个只监听 `127.0.0.1` 的极简 HTTP 服务；AI（Claude / Cursor / DeepSeek Harness 等）用 JSON 命令建模，不必实时编译代码进 Revit。

## 为什么是这个架构

- **Revit API 只能进程内调用**，所以必须有插件；
- 插件用 `ExternalEvent` 把 HTTP 线程的请求编组到 Revit 主线程，**每条命令一个 Transaction**，失败自动回滚；
- AI 只发结构化 JSON（`wall.create` / `hello` …），不做"生成 C# 再编译"，稳定且可校验；
- 命令返回元素 ID 与错误信息，AI 才能形成"做—看—改"的闭环。

```
AI  --HTTP/JSON-->  127.0.0.1:8787  -->  Revit 插件  --ExternalEvent-->  Revit 主线程
      (X-Auth-Token)                                      └─ Transaction（可回滚）
```

## 进度

- [x] 插件骨架（IExternalApplication）+ HTTP 桥 + Token 鉴权
- [x] `ping` / `help` / `hello`（文档信息、版本、标高）
- [x] 建模命令：标高、轴网、墙、楼板、结构柱、族实例、删除
- [x] 单位统一 mm（内部换算英尺），警告自动吞掉（否则弹窗卡死桥）
- [ ] Revit 侧联调（首次加载已定位为中文路径问题，改到 ASCII 路径后待验证）
- [ ] `apply_plan` 批量事务 + MCP 封装

## 环境

| 依赖 | 版本 |
| --- | --- |
| Revit | 2020（API 仅 .NET Framework 4.7.2） |
| 编译 | .NET SDK 8+ 或 VS 生成工具，需 net472 目标包 |
| 客户端 | Python 3.8+ |

引用 `RevitAPI.dll` / `RevitAPIUI.dll` 的路径在 `src/RevitAI.csproj` 里，按自己的安装位置改。

> **重要：插件 DLL 不要放在含中文的路径下。** 实测 Revit 2020 在 `E:\0个人文件夹\...` 这类路径上会报
> 「无法运行外部应用程序」，journal 里是 `API_ERROR { Starting External Application ... }`，
> 而同一 DLL 换到 `E:\revit-ai\...` 就能正常加载。`install-addin.bat` 会把 DLL 复制到
> `%APPDATA%\Autodesk\Revit\Addins\2020\RevitAI\`（纯 ASCII），避免踩这个坑。

## 编译与安装

```bat
dotnet build src\RevitAI.csproj -c Release
install-addin.bat
```

`install-addin.bat` 把 `dist\RevitAI.dll` 复制到 `%APPDATA%\Autodesk\Revit\Addins\2020\RevitAI\`，
并按 `RevitAI.addin.template` 生成 `RevitAI.addin`。

启动 Revit，首次会弹 **“未签名加载项”** 提示，点 **Always Load**。加载成功的话
`%LOCALAPPDATA%\RevitAI\` 下会出现 `token.txt` 和 `bridge.log`。

## 使用

```bat
python client\revit.py ping
python client\revit.py hello
python tools\smoke.py          :: 只读联调
python tools\smoke.py --build  :: 画一个 4x3 m 的房间（两面轴网 + 四面墙 + 楼板）
```

## 命令

所有长度单位都是 **mm**。

| cmd | 参数 | 说明 |
| --- | --- | --- |
| `ping` | – | 探活（不经过 Revit 主线程） |
| `help` | – | 命令表 |
| `hello` | – | 文档标题/路径、Revit 版本、标高、元素数 |
| `levels.list` | – | 标高列表 |
| `types.list` | `category`: walls/floors/columns/doors/windows | 可用类型名 |
| `level.create` | `name`, `elevation_mm` | 建标高 |
| `grid.create` | `p1`, `p2`, `name` | 建轴网 |
| `wall.create` | `p1`, `p2`, `level`, `height_mm`, `type` | 建墙 |
| `floor.create` | `points`, `level`, `type` | 建楼板（闭合多边形） |
| `column.create` | `point`, `level`, `type` | 建结构柱 |
| `family.place` | `type`, `point`, `level`, `rotation_deg` | 放族实例 |
| `element.delete` | `ids` | 删除 |

返回统一是 `{ ok, cmd, data \| error, ms }`。

## 安全

- 只绑定 `127.0.0.1`，不监听外网；
- 每次启动随机生成 48 位 token，写入 `%LOCALAPPDATA%\RevitAI\token.txt`，请求需带 `X-Auth-Token`；
- 日志：`%LOCALAPPDATA%\RevitAI\bridge.log`；
- 所有建模命令在 Transaction 内执行，异常回滚；警告自动吞掉避免弹窗阻塞。

## License

MIT
