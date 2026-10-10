# 涅奥的陪伴 · Neow's Company

为《杀戮尖塔 2》单人模式添加 LLM 生成的卡牌奖励。涅奥根据你的卡组与战斗过程设计新牌，追加到原版战斗卡牌奖励中。

当前 Mod 版本 `0.1.0`，目标游戏版本 `v0.111.0`。仅支持单人；游戏更新后需要重新检查兼容性。卡图使用游戏占位图，不需要额外 Mod 依赖。

## 安装

1. 退出游戏，将预编译包中的 `NeowsCompany` 文件夹放入游戏的 `mods` 目录；从源码安装请见下方构建说明。
2. 将该文件夹中的 `config.example.json` 复制为 `config.json`，填写 `provider.base_url`、`provider.model` 和凭据。首次启动也会创建默认配置。
3. 在游戏 Mod 菜单启用“涅奥的陪伴”，重启后开始单人游戏。

`base_url` 是 OpenAI 兼容 API 的根地址；客户端会追加 `/chat/completions`。默认地址 `http://127.0.0.1:8000/v1` 用于本地服务，不附带模型。远端服务应填写其 HTTPS API 根地址。

密钥可写入本地 `provider.api_key`，或通过 `NEOWS_COMPANY_API_KEY` 环境变量提供（非空环境变量优先）。环境变量应在启动 Steam/游戏之前设置。修改配置后需要重启游戏。**不要将真实密钥写入 `config.example.json` 或提交到 Git。**

## 游戏行为

默认在敌方首回合结束后预生成，每场最多请求 3 次、间隔至少 15 秒。通过校验的卡牌进入本局候选池；打开奖励时追加 1 张可用候选，原版选项保留。没有候选就正常显示原版奖励，不等待网络，也不会在选择过程中改变选项。晚到的结果可在后续战斗出现。

新生成采用 v5 协议：两个完整升级形态、通用选牌与生成/变化、规则组和动态表达式，并支持奥斯提/灵魂/灾厄、充能球/集中、铸造/星、小刀/奇巧。LLM 只能组合已实现的效果，不能返回可执行代码；不设强度预算，卡牌平衡需要实际游玩评估。旧协议不提供迁移，建议从新局开始测试。

生成牌定义随原生卡牌存档保存，读档不需要再次调用模型。本局候选池和已冻结奖励另存于 `data/runs`，需要恢复当前局奖励时请保留。已有生成牌的存档应继续启用此 Mod。

## 配置与隐私

可调整奖励张数、请求时机、风格提示词和日志开关，见 [配置与排错](docs/CONFIGURATION.md)。默认记录生成调用、提示词与服务端返回的 reasoning；日志没有自动清理。完整战斗日志已停止写入，旧 `data/combats` 文件可在退出游戏后删除。

你的游戏状态和自定义提示词会发送给所配置的服务。API key 通过请求头认证，不作为提示词字段；分享日志前仍应检查敏感内容。`config.json`、运行数据和本地输出已加入 `.gitignore`。

## 从源码构建

需要 .NET 9 SDK（或能够构建 `net9.0` 的兼容 SDK），以及本机 Windows 版游戏 `v0.111.0`。项目无第三方 NuGet 包依赖，游戏程序集不会打包发布。

```powershell
.\build.ps1 -GameDir 'C:\Games\Slay the Spire 2'
.\install.ps1 -GameDir 'C:\Games\Slay the Spire 2'
```

`build.ps1` 默认运行核心测试和游戏 API 检查，输出 `build/NeowsCompany/` 与 `build/NeowsCompany-0.1.0.zip`。SDK 不在 PATH 时可传 `-DotnetExe '<dotnet.exe 路径>'`。安装脚本要求退出游戏，并保留已有配置和运行数据。

无需游戏或真实 LLM 即可测试核心与生成链路：

```powershell
dotnet run --project tests/Forge.Tests -c Release
# 在单独终端启动测试接口，仅返回固定卡牌：
python tools/mock_provider.py --port 8000
# 以下命令使用仓库自带的本地服务配置：
dotnet run --project src/Forge.Tool -- generate config.example.json examples/context.json generated.local.json
dotnet run --project src/Forge.Tool -- validate generated.local.json
# 仅查看实际提示词，不调用服务：
dotnet run --project src/Forge.Tool -- prompt config.example.json examples/context.json prompt.local.json
```

## 查看生成记录

使用 Python 3.10+ 启动本地查看器，无需安装第三方包。Windows 启动脚本会查找 Python，也支持本机已有的 Codex Python 运行时。传入游戏目录，启动后自动打开浏览器：

```powershell
.\view-generation.ps1 -GameDir 'C:\Games\Slay the Spire 2'
# 也可以直接指定日志目录，或通过 Python 启动：
.\view-generation.ps1 -Directory 'C:\Games\Slay the Spire 2\mods\NeowsCompany\data\generation'
python tools/view_generation.py 'C:\Games\Slay the Spire 2\mods\NeowsCompany\data\generation'
```

不传路径时使用 `STS2_GAME_DIR`，未设置则读取仓库的 `data/generation`。页面按战斗和请求序号整理记录，可以搜索卡名、模型和失败原因，筛选成功/失败，查看基础与升级版卡牌效果文本、效果结构、提示词、reasoning、模型原始输出（content）、耗时、token 与缓存命中，以及奖励审计事件。新日志保存共享 CardText 渲染器生成的效果文本；旧日志由查看器从定义还原。JSON 块点开后，所有嵌套对象和列表默认展开，仍可手动折叠；标题旁的“复制”按钮复制完整内容，与折叠状态无关。`OBSERVATION_JSON` 单独展示，reasoning 和原始输出默认折叠。原始输出保留空白和代码围栏（已知凭据和服务地址会脱敏），旧日志未保存的 content 无法补回。时间按浏览器本地时区显示，缺少完成结果的请求标记为“待完成”。

页面默认每 5 秒刷新，支持边玩边看；无效或未写完的 JSONL 行会被跳过并提示。查看器只读取日志，服务仅监听 `127.0.0.1`，不调用模型或修改游戏文件。按 `Ctrl+C` 停止。启动脚本支持 `-Port 0` 自动选择端口、`-NoBrowser` 仅启动服务，以及 `-PythonExe '<python.exe 路径>'`；直接用 Python 时对应选项为 `--port 0`、`--no-browser`。

## 文档

- [配置与排错](docs/CONFIGURATION.md)：参数、数据目录和常见失败原因。
- [LLM 协议](docs/LLM_PROTOCOL.md)：请求上下文、输出和候选池规则。
- [卡牌协议](docs/CARD_PROGRAM.md)：v5 完整形态、动作、目标与规则语义。
- [架构](docs/ARCHITECTURE.md)：模块边界、线程、存档与游戏挂钩。
- [验证指南](docs/VALIDATION.md)：自动检查与实机验收步骤。

自动测试与程序集检查不代表已完成游戏 UI、效果执行或其他 Mod 共存验收。
