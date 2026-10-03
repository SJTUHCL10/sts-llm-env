# 涅奥的陪伴 · Neow's Company

`0.1.0`，针对本机《杀戮尖塔 2》`v0.111.0 (41cef1ea)` 编译。涅奥观察你的卡组与战斗选择，通过你配置的 LLM 设计新牌，并将它们追加到原版战斗卡牌奖励。首版只支持单人。

默认在战斗中提前请求，打开卡牌奖励时立即使用最近一批**已完成且通过校验**的结果。没有可用结果就正常显示原版牌；不等待网络，也不会在你选牌时突然增加选项。原版牌、奖励选项、基础机制、地图、怪物和胜利条件保持原样。

## 安装与开始

1. 退出游戏，把预编译压缩包中的 `NeowsCompany` 文件夹放入游戏的 `mods` 目录。
2. 将 `config.example.json` 复制成同目录下的 `config.json`，填写 `provider.base_url`、`provider.model`，以及密钥。首次启动也会自动创建默认配置。
3. 在游戏 Mod 菜单启用“涅奥的陪伴”，重新启动，开始单人爬塔。
4. 有效配置示例：`base_url = "http://127.0.0.1:8000/v1"` 用于本地 OpenAI 兼容服务；远端填服务商提供的 API 根地址。Mod 会追加 `/chat/completions`，不要把完整端点填入 `base_url`。

不需要 BaseLib、AutoAnthony、Bridge 或 Advisor，也不包含原游戏程序集。卡图暂用游戏的占位图。需要本地服务运行或有效的远端接口才会出现 LLM 卡牌；默认配置没有内置模型。

密钥既可写在 `provider.api_key`，也可用 `NEOWS_COMPANY_API_KEY` 环境变量；非空环境变量优先。给游戏使用的环境变量应在**启动 Steam/游戏前**设置，已有进程不会自动继承新值。配置在 Mod 初始化时读取，修改后重启游戏。

## 主要配置

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `enabled` | `true` | 是否生成和追加新牌；已有生成牌仍可正常使用 |
| `generated_cards_per_reward` | `1` | 每份战斗卡牌奖励追加 0–3 张牌，原版选项不减少 |
| `generation_timing` | `"prefetch"` | 战斗中提前请求，只展示已完成结果；可选 `"wait_on_reward"`，打开奖励时请求并最多等待配置的超时 |
| `max_requests_per_combat` | `3` | 每场最多请求次数，失败也占一次；没有自动网络重试 |
| `prefetch_minimum_interval_seconds` | `15` | 两次请求启动的最小间隔；同一场最多一个请求在途 |
| `active_style` | `"balanced"` | 使用 `styles` 中的哪个配置 |
| `styles.<name>.system_prompt` | 见示例 | 风格系统提示词；Mod 会追加固定的效果协议 |
| `styles.<name>.instructions` | 见示例 | 风格、叙事、构筑偏好提示；可新增任意命名风格 |
| `prompt_event_limit` | `60` | 最多把最近多少条战斗事件传给 LLM；本地日志仍记录全部事件 |
| `max_prompt_characters` | `60000` | 提示词总字符上限；超限优先移除较早事件，并标注省略数量 |
| `record_combat` | `true` | 记录完整事件、状态和生成审计；关闭后不创建日志 |
| `record_generation_prompts` | `true` | 在战斗日志中保存发出的提示词；需同时开启 `record_combat` |

默认在首次玩家回合开始后请求，后续在回合边界和打牌完成时尝试刷新；间隔与预算决定是否实际请求。请求期间发生的新动作会进入之后一次请求。短战斗或慢模型可能没有生成牌，符合“奖励不等待”的默认策略。战斗结束后尚在途的请求仍可完成，但第一次打开奖励会冻结当前结果并取消在途请求；冻结后的响应不再影响奖励。

`provider` 配置包括 `base_url`、`api_key`、`api_key_environment_variable`、`model`、`timeout_seconds`、`max_tokens`、`temperature`、`include_temperature`、`token_limit_parameter`、`json_mode`、`max_response_bytes`。对于要求新参数的模型，可将 `token_limit_parameter` 改为 `"max_completion_tokens"`；不接受温度参数时将 `include_temperature` 设为 `false`。`json_mode` 默认关闭，兼容更多本地服务；支持 JSON 模式的服务可以开启。协议依据 [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat) 与 [JSON mode 文档](https://developers.openai.com/api/docs/guides/structured-outputs)，具体模型/服务的参数支持仍需自行匹配。

## 生成卡牌与存档

LLM 返回版本化 JSON，不能返回代码或新游戏规则。首版支持攻击/技能、0–3 费、普通/罕见/稀有、最多 4 个顺序效果，以及消耗、虚无、保留、固有。效果是伤害、格挡、抽牌、能量、力量、敏捷、虚弱、易伤、中毒；可指定自己、一个选定敌人或全部敌人，组合必须符合类型规则。每个效果可配置升级增量。

卡牌描述由实际效果生成，使用原版动态变量、伤害/格挡修正、状态应用和关键词流程。升级与战斗复制保留定义。定义作为原生 `SavedProperty` 随牌保存，读档不依赖再次调用模型。基础数值上限和粗略强度预算会拒绝明显不合理的卡牌；这不是完整平衡模拟，真实组合仍需要试玩调参。

`data/rewards/<key>.json` 保存第一次打开奖励时冻结的候选定义（空结果也保存）。奖励读档重新填充时附加相同候选。键包含种子、角色、难度、章节、楼层和房间 ID，因此同种子同位置的重跑可能复用上次已经展示的结果。刷新奖励复用冻结牌，不追加新的网络请求。多份同房间卡牌奖励也共享该批生成牌。暂不支持按存档槽隔离此缓存，保留此目录可保证已展示奖励的读档稳定。

游戏的运行存档中已有生成牌时，应保留此 Mod；移除提供卡牌类型的 Mod 后，游戏可能将它们作为废弃内容处理。损坏的奖励缓存会忽略；损坏的牌定义不会被静默改成另一张牌。

## 战斗记录

安装目录下的 `data/combats/*.jsonl` 按场记录：

- 开始/结束、回合边界、种子、角色、难度、楼层、房间、已走路径、金币、完整卡组、遗物和药水。
- 每条原生历史事件的顺序、回合、阵营、具体字段，以及事件发生时的状态快照。
- 牌实例编号、卡名、类型、费用、升级、关键词、动态数值、附魔/侵蚀与实际描述，各牌堆的完整顺序。
- 玩家 HP、格挡、能量、星星、充能球顺序/容量/可读取数值、奥斯提等盟友状态，敌人 HP、格挡、状态、下一招与全部意图。
- 打牌开始/完成、目标、资源消耗、抽弃消耗牌、伤害、格挡、药水、状态、怪物行动等游戏历史条目。
- 生成请求/失败类别/通过验证的卡牌，展示的奖励及关闭奖励时的卡组。

字段读取失败会标记 `unavailable`；额外状态只读取公开标量属性，不能保证每种角色专属的隐藏状态都被提取。牌实例编号只在本场采集器内有效。日志用后台单写入队列落盘，正常每行刷新；异常退出时尚未写入的队列尾部可能丢失。日志暂无自动清理。

发送给你配置的 LLM 的是卡组/运行快照、当前状态和有限的最近事件，不会发送 API 密钥或 Steam 玩家 ID。远端服务会收到这些游戏信息。本地日志也会保存游戏过程及（可关闭的）提示词，密钥和服务端原始错误体不会写入。

## 构建与验证

需要 .NET 9 SDK（较新 SDK 能编译 net9.0 时亦可）及合法本机游戏。没有 NuGet 第三方依赖。

```powershell
.\build.ps1 -GameDir 'F:\SteamLibrary\steamapps\common\Slay the Spire 2' `
  -DotnetExe 'C:\Users\Administrator\codes\sts\.tools\dotnet-sdk-9\dotnet.exe'
```

输出 `build/NeowsCompany/` 和 `build/NeowsCompany-0.1.0.zip`。构建默认运行核心测试与游戏 API 检查。源码目录中的 `install.ps1 -GameDir '...'` 会安装已构建包，保留已有配置和 `data`，并要求游戏已退出。

无需启动游戏即可调试生成链路：

```powershell
# 可选：只返回测试卡的本地接口，验证集成，不能当作真实 LLM。
python .\tools\mock_provider.py --port 8000
# 另一个终端（dotnet 不在 PATH 时用完整可执行文件路径）：
dotnet run --project .\src\Forge.Tool -- config .\config.example.json
dotnet run --project .\src\Forge.Tool -- generate .\config.example.json .\examples\context.json .\generated.local.json
dotnet run --project .\src\Forge.Tool -- validate .\generated.local.json
```

验证状态见 `docs/VALIDATION.md`。核心测试和真实程序集检查不等同于完整游戏 UI/战斗验收；首版尚需进游戏验证奖励布局、出牌视觉反馈、读档奖励与其他 Mod 共存。游戏版本更新后需重新编译并复核 Harmony 挂钩。

## 后续扩展

游戏外的 `Forge.Core` 包含配置、版本化定义、提示词、Provider 抽象、校验、异步会话和日志存储；`Forge.Mod` 负责状态适配、效果执行、显示和奖励挂钩；`Forge.Tool` 可离线复现生成。拓展设计与挂钩清单见 `docs/ARCHITECTURE.md`。

后续“建筑师的注视（Architect's Gaze）”可复用记录、Provider 与请求预算，独立新增怪物定义/校验/生成器/游戏注入模块。首版不注册、不替换任何怪物。

参考了本机 AutoAnthony 的结构化卡牌及保存方式、sts 的游戏反编译与 Advisor 状态采集接口；未复制其源码或打包其资源。社区接口参考：[BaseLib](https://github.com/Alchyr/BaseLib-StS2)。此 Mod 本身使用游戏原生 Mod 初始化与 Harmony。
