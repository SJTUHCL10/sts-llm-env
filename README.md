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

支持攻击、技能、能力、升级、触发效果及储君星资源。LLM 只能组合已实现的效果，不能返回可执行代码。新生成采用 v3 协议，校验结构与执行规则，但不设强度预算，卡牌平衡需要实际游玩评估。

生成牌定义随原生卡牌存档保存，读档不需要再次调用模型。本局候选池和已冻结奖励另存于 `data/runs`，需要恢复当前局奖励时请保留。已有生成牌的存档应继续启用此 Mod。

## 配置与隐私

可调整奖励张数、请求时机、风格提示词和日志开关，见 [配置与排错](docs/CONFIGURATION.md)。默认记录战斗、提示词与服务端返回的 reasoning；日志没有自动清理。

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

## 文档

- [配置与排错](docs/CONFIGURATION.md)：参数、数据目录和常见失败原因。
- [LLM 协议](docs/LLM_PROTOCOL.md)：请求上下文、输出和候选池规则。
- [卡牌协议](docs/CARD_PROGRAM.md)：v3 效果语义与 v1/v2 存档兼容。
- [架构](docs/ARCHITECTURE.md)：模块边界、线程、存档与游戏挂钩。
- [验证指南](docs/VALIDATION.md)：自动检查与实机验收步骤。

自动测试与程序集检查不代表已完成游戏 UI、效果执行或其他 Mod 共存验收。
