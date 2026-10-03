# 涅奥的陪伴 · Neow's Company

`0.1.0`，针对本机《杀戮尖塔 2》`v0.111.0 (41cef1ea)` 编译。涅奥观察你的卡组与战斗选择，通过你配置的 LLM 设计新牌，并将它们追加到原版战斗卡牌奖励。首版只支持单人。

默认在战斗中提前请求，打开卡牌奖励时立即从本局候选池选取**已完成且通过校验**的结果。没有可用结果就正常显示原版牌；不等待网络，也不会在你选牌时突然增加选项。原版牌、奖励选项、基础机制、地图、怪物和胜利条件保持原样。

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
| `prefetch_initial_card_plays` | `2` | 兼容旧配置的字段，现已忽略；首次调用固定等敌方首回合结束 |
| `candidate_pool_capacity` | `24` | 本局未展示候选池容量，超出淘汰最旧候选 |
| `active_style` | `"balanced"` | 使用 `styles` 中的哪个配置 |
| `styles.<name>.system_prompt` | 见示例 | 风格系统提示词；Mod 会追加固定的效果协议 |
| `styles.<name>.instructions` | 见示例 | 风格、叙事、构筑偏好提示；可新增任意命名风格 |
| `prompt_event_limit` | `60` | 最多把最近多少条战斗事件传给 LLM；本地日志仍记录全部事件 |
| `max_prompt_characters` | `60000` | 提示词总字符上限；超限优先移除较早事件，并标注省略数量 |
| `record_combat` | `true` | 在 `data/combats` 记录完整战斗事件和状态快照 |
| `record_generation` | `true` | 在 `data/generation` 独立记录 LLM 调用和奖励审计，不依赖 `record_combat` |
| `record_generation_prompts` | `true` | 在调用日志中保存提示词；关闭后仍记录调用次数、耗时、结果和失败原因，需开启 `record_generation` |
| `record_generation_reasoning` | `true` | 保存服务端 reasoning_content；需开启 record_generation |
| `provider.reasoning_effort` | `null` | 不传参数，使用服务默认；可设置思考强度或关闭思考，见下方说明 |

默认等敌方第一回合行动完成后首次调用，完整格挡或敌方没有攻击也会触发。第一轮内胜利时用最终摘要启动请求。后续在回合边界和出牌完成时按间隔/预算刷新。成功结果追加到本局候选池，不再覆盖上一批；奖励展示固定本次选项，但不取消在途请求，晚到的结果可以在后续战斗出现。已展示候选退出池。死亡、放弃或重新加载当前 run 会关闭旧请求。`wait_on_reward` 保留打开奖励时等待请求的可选行为。

上下文精简为去重牌组、当前状态、近期事件简短引用、完整累计摘要、首轮摘要、生成历史。新 prompt 鼓励一个核心想法、通常1～2个关联效果、不同费用和机制，允许强力协同。完整字段与输出示例见 [LLM_PROTOCOL.md](docs/LLM_PROTOCOL.md)。

`provider` 配置包括 `base_url`、`api_key`、`api_key_environment_variable`、`model`、`timeout_seconds`、`max_tokens`、`reasoning_effort`、`temperature`、`include_temperature`、`token_limit_parameter`、`json_mode`、`max_response_bytes`。对于要求新参数的模型，可将 `token_limit_parameter` 改为 `"max_completion_tokens"`；不接受温度参数时将 `include_temperature` 设为 `false`。`json_mode` 默认关闭，兼容更多本地服务；支持 JSON 模式的服务可以开启。协议依据 [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat) 与 [JSON mode 文档](https://developers.openai.com/api/docs/guides/structured-outputs)，具体模型/服务的参数支持仍需自行匹配。

`provider.reasoning_effort` 默认 `null`（或省略），请求中不发送该字段，兼容未支持此参数的服务。配置非空值时，Mod 将其原样作为请求顶层 `reasoning_effort` 发送，并在 `generation_request` 日志中记录；不会依据模型名自动切换行为。

DeepSeek 的 `deepseek-flash` 可使用以下值；按[官方思考模式文档](https://api-docs.deepseek.com/guides/thinking_mode/)和 [Chat Completions 参数文档](https://api-docs.deepseek.com/zh-cn/api/create-chat-completion/)，默认开启思考且强度为 `high`：

| 配置值 | DeepSeek 行为 |
| --- | --- |
| `null` | 使用服务默认，不发送该参数 |
| `"none"` | 关闭思考 |
| `"low"` | 低强度思考 |
| `"high"` | 高强度思考 |
| `"max"` | 最高强度思考 |

兼容别名也可配置：`"minimal"` 映射到 `low`，`"medium"`/`"xhigh"` 映射到 `high`，`"ultra"` 映射到 `max`；它们不代表额外的实际档位。其他服务是否接受这些值由该服务决定。无效配置值会在启动时被拒绝。

对于战斗中预生成卡牌，可先尝试在现有 `config.json` 的 `provider` 对象中添加 `"reasoning_effort": "low"`，或使用 `"none"` 关闭思考。这些设置的实际速度和生成质量仍需用目标服务验证；思考模式的推理 token 也会占用输出预算，降低强度不会保证所有请求成功。DeepSeek 思考模式下 `temperature` 不生效。配置修改后重启游戏。

## 生成卡牌与存档

LLM 返回版本化 JSON，新生成使用 v3，支持攻击/技能/能力、原生能量与储君星费用、获得星、剩余星数成长，以及原有触发/条件/牌堆效果。v3 取消强度预算和数值、scaling计数、事件次数、持续期、重复次数的固定上限；默认事件不限次数，缩放不限计数。仍校验合法结构，保留原生游戏规则和内部递归保护。旧 v1/v2 不改变语义。详见 [CARD_PROGRAM.md](docs/CARD_PROGRAM.md)。

详见 [复杂卡牌协议](docs/CARD_PROGRAM.md) 和 [可校验示例](examples/complex-cards.json)。新增 [高费数值示例](examples/high-cost-cards.json)，包含 4 费 52→63 伤害与 5 费攻击。旧 v1 卡牌仍按原即时效果执行。LLM 只能组合已实现的组件，不能返回代码或任意新规则。

卡牌描述由实际效果生成，使用原版动态变量、伤害/格挡修正、状态应用和关键词流程。升级与战斗复制保留定义。定义作为原生 `SavedProperty` 随牌保存，读档不依赖再次调用模型。延迟效果实例保存完整定义、施放数值、剩余回合和响应次数；恢复检查点仍由游戏控制。强度预算计入最大升级/缩放、重复次数和持续期；这不是完整平衡模拟，真实组合仍需要试玩调参。

白银熔炉升级这份原版奖励时，追加的生成牌也会升级，包括第三次奖励。此判断读取原版候选的遗物处理记录，不额外消耗熔炉次数；原版牌仅因随机概率升级时，不会据此强制升级生成牌。

`data/runs/<run-key>.json` 原子保存本局候选池、生成/展示/选择历史、去重指纹和冻结奖励（空结果也保存）。键使用原生存档开局时间、种子、角色及进阶。SL 恢复相同奖励，已展示的战斗不再发请求；新的同种子局不复用较早开局的池。原有 `data/rewards` 仅作为属于当前run的旧缓存导入来源。

游戏的运行存档中已有生成牌时，应保留此 Mod；移除提供卡牌类型的 Mod 后，游戏可能将它们作为废弃内容处理。损坏的奖励缓存会忽略；损坏的牌定义不会被静默改成另一张牌。

## 战斗记录

安装目录下的 `data/combats/*.jsonl` 按场记录：

- 开始/结束、回合边界、种子、角色、难度、楼层、房间、已走路径、金币、完整卡组、遗物和药水。
- 每条原生历史事件的顺序、回合、阵营、具体字段，以及事件发生时的状态快照。
- 牌实例编号、卡名、类型、费用、升级、关键词、动态数值、附魔/侵蚀与实际描述，各牌堆的完整顺序。
- 玩家 HP、格挡、能量、星星、充能球顺序/容量/可读取数值、奥斯提等盟友状态，敌人 HP、格挡、状态、下一招与全部意图。
- 打牌开始/完成、目标、资源消耗、抽弃消耗牌、伤害、格挡、药水、状态、怪物行动等游戏历史条目。
- LLM 调用与奖励审计保存到同名的 `data/generation/*.jsonl`，可按文件名关联战斗。

字段读取失败会标记 `unavailable`；额外状态只读取公开标量属性，不能保证每种角色专属的隐藏状态都被提取。牌实例编号只在本场采集器内有效。日志用后台单写入队列落盘，正常每行刷新；异常退出时尚未写入的队列尾部可能丢失。日志暂无自动清理。

每个历史事件都附带完整状态快照，单场战斗达到数 MB 是可能的。日常游玩可将 `record_combat` 和 `record_generation_prompts` 设为 `false`，保留 `record_generation: true`，这样仅保留体积较小的调用诊断和奖励审计。退出游戏后可以手动删除不需要的 `data/combats` 和 `data/generation` 历史日志；需要恢复尚未领取奖励的存档时，保留 `data/rewards`。

调用日志中 `generation_request` 表示请求开始，`generation_ready` 表示整批卡牌通过验证，`generation_failed` 含 `stage`、`reason`、`elapsed_ms` 及可用的 `http_status`。例如 `completion_token_limit` 表示服务返回截断标记，可检查 `provider.max_tokens`；`invalid_response_json_or_schema` 表示 JSON/字段不符合协议；校验阶段会给出非法字段、结构、目标或旧协议限制；`provider_timeout` 表示超时；`reward_frozen_or_session_ended` 表示结束/取消了会话（新奖励冻结不会取消请求）。错误体、密钥和任意外部异常消息不会写入日志。旧版本的生成审计仍在 `data/combats` 内，只有异常类型，不能追溯完整原因。

新配置的 `provider.max_tokens` 默认值为 `4096`，为模型完成结构化输出留出更多空间；使用推理模型时，服务的 token 上限可能还包含推理过程。升级 Mod 会保留旧配置，因此已有 `max_tokens: 1800` 需要自行修改。增大上限不能保证所有响应通过验证；如果出现 `provider_timeout`，再检查响应耗时和 `provider.timeout_seconds`。

run快照奖励表中的 `[]` 表示第一次打开奖励时没有已通过校验的结果，并非损坏。`wait_on_reward` 能避免等待不足造成的空奖励，但不会绕过服务错误或卡牌校验；它也不会重新生成已经冻结的空缓存。

发送给你配置的 LLM 的是精简的卡组/运行快照、当前状态、有限近期事件、累计/首轮摘要和生成历史，不会发送 API 密钥或 Steam 玩家 ID。远端服务会收到这些游戏信息。本地日志也会保存游戏过程及（可关闭的）提示词，密钥和服务端原始错误体不会写入。

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
# 测试复杂能力牌：添加 --fixture .\examples\complex-cards.json --card-index 1
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

服务端 reasoning_content 另记为 generation_response，带请求序号、finish_reason和token数量；没有返回则为null。CLI prompt 命令可以离线查看请求，无网络调用。
