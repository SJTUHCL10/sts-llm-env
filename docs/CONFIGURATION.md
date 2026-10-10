# 配置与排错

配置文件位于游戏的 `mods/NeowsCompany/config.json`，修改后重启游戏。完整默认值见 [config.example.json](../config.example.json)。不要把真实凭据填入示例文件。

## 生成与记录

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
| `prompt_event_limit` | `60` | 内存中保留的近期战斗事件数量，用于生成观测；累计战斗摘要独立保留 |
| `max_prompt_characters` | `60000` | 提示词总字符上限；超限优先移除较早事件，并标注省略数量 |
| `record_combat` | 已移除 | 仅兼容读取旧配置，值为 `true` 也不会写入战斗日志；新示例配置省略此字段 |
| `record_generation` | `true` | 在 `data/generation` 记录 LLM 调用和奖励审计 |
| `record_generation_prompts` | `true` | 在调用日志中保存提示词；关闭后仍记录调用次数、耗时、结果和失败原因，需开启 `record_generation` |
| `record_generation_reasoning` | `true` | 保存服务端 reasoning_content；需开启 record_generation |
| `provider.reasoning_effort` | `null` | 不传参数，使用服务默认；可设置思考强度或关闭思考，见下方说明 |

`prefetch_initial_card_plays` 仅用于读取旧配置，不控制当前调用时机。首次预生成在敌方首回合结束后；第一轮内胜利则使用最终摘要。候选池与奖励冻结规则见 [LLM_PROTOCOL.md](LLM_PROTOCOL.md#候选池和时机)。

最后一幕的 Boss 战斗没有原生卡牌奖励，因此不会创建生成会话或请求 LLM，包括进阶 10 的两场 Boss。判断与游戏奖励逻辑一致，使用当前幕索引、幕列表和遭遇的 Boss 类型，不依赖层数；前两幕 Boss 与第三幕普通/精英战斗照常生成。

## Provider

`base_url` 填 OpenAI 兼容 API 根地址，例如本地服务的 `http://127.0.0.1:8000/v1`。客户端追加 `/chat/completions`。远端服务应使用 HTTPS。

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `base_url` | `http://127.0.0.1:8000/v1` | API 根地址，不接受用户名、密码、查询串或片段 |
| `model` | `local-model` | 服务端提供的模型名称 |
| `api_key` | 空字符串 | 本地凭据；不要提交到 Git |
| `api_key_environment_variable` | `NEOWS_COMPANY_API_KEY` | 非空环境变量优先于 `api_key` |
| `timeout_seconds` | `30` | 单次请求超时，范围 1–120 秒 |
| `max_tokens` | `4096` | 输出预算，范围 128–16000；服务可能将推理 token 计入预算 |
| `token_limit_parameter` | `max_tokens` | 可改为 `max_completion_tokens` |
| `temperature` | `0.8` | 温度；是否支持由服务决定 |
| `include_temperature` | `true` | 不支持温度参数时设为 `false` |
| `json_mode` | `false` | 开启时发送 `response_format: {"type":"json_object"}` |
| `reasoning_effort` | `null` | 省略此请求字段，使用服务默认 |
| `max_response_bytes` | `131072` | 响应大小上限，范围 1024–1048576 字节 |

`reasoning_effort` 接受 `null`、`none`、`minimal`、`low`、`medium`、`high`、`xhigh`、`max`、`ultra`。非空值原样发送，客户端不转换别名，也不根据模型名称切换参数。配置校验通过不代表服务支持该值；请按所用服务的参数要求设置。

环境变量应在启动 Steam/游戏之前设置，已有进程不会自动继承新值。程序不会自动重试不兼容的参数。升级 Mod 保留已有配置，不会用新的默认值覆盖它。

## 数据与日志

所有路径相对于 Mod 安装目录：

| 路径 | 内容与清理方式 |
| --- | --- |
| `data/runs/*.json` | 本局候选池、历史与冻结奖励；需要恢复当前局奖励时必须保留 |
| `data/rewards/` | 旧版奖励缓存，仅作为当前局的兼容导入来源 |
| `data/combats/*.jsonl` | 旧版完整战斗日志，已停止写入；退出游戏后可删除，不参与生成或读档 |
| `data/generation/*.jsonl` | 请求、响应诊断与奖励审计；退出游戏后可删除 |

生成日志暂无自动清理，后台写入队列在异常退出时可能丢失末尾记录。可关闭 `record_generation_prompts` 和 `record_generation_reasoning`，保留 `record_generation` 以诊断调用结果。战斗事件和累计摘要仅保留在内存中，不再逐事件写入完整状态快照。

游戏信息与自定义提示词会发送至你配置的服务。API key 通过 Authorization 请求头发送，不作为提示词字段。日志不主动记录请求头、HTTP 错误体或任意异常消息；reasoning 中匹配到的已知密钥及服务地址会被替换。这不等于对任意模型文本的全面脱敏，分享日志前仍需检查内容。

## 没有出现生成牌

先检查 Mod 已启用、配置有效、服务可访问且模型名正确。默认模式只展示打开奖励时已经完成并通过校验的结果；慢请求可能到后续战斗才出现。奖励首次打开即冻结，空结果也会保存，读档不会重新生成。

在 `data/generation` 中查找 `generation_request`、`generation_ready`、`generation_failed`；失败条目包含 `stage`、`reason`、`elapsed_ms` 和可用的 `http_status`。

| 原因 | 检查方向 |
| --- | --- |
| `completion_token_limit` | 响应被截断；检查输出预算和推理消耗 |
| `invalid_response_json_or_schema` | 输出 JSON 或字段不符合协议 |
| 校验阶段失败 | 检查效果结构、目标、数值范围与协议版本 |
| `provider_timeout` | 检查响应耗时与超时设置 |
| `reward_frozen_or_session_ended` | 会话已结束或取消；当前默认模式冻结奖励本身不会取消在途请求 |

`wait_on_reward` 会在打开奖励时等待请求，但不能绕过服务错误、校验失败或已经冻结的空奖励。
