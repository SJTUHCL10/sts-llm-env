# LLM 请求与输出协议

当前新生成卡牌使用 schema v4；观测上下文使用 schema v2。此文档描述代码实际发送的字段，不是原始战斗日志的字段全集。

## HTTP 请求

向 `provider.base_url + /chat/completions` POST：

| 字段 | 内容 |
| --- | --- |
| `model` | 配置的模型名 |
| `messages` | 两条消息：`system`（固定效果协议＋风格提示词/指令＋角色扩展），`user`（数量＋观测 JSON） |
| `max_tokens` 或 `max_completion_tokens` | 按 `token_limit_parameter` 选择字段；值为配置 token 预算 |
| `temperature` | 仅 `include_temperature=true` 时发送 |
| `reasoning_effort` | 非 null 时原样发送；null/缺省时整个字段省略，使用服务端默认 |
| `response_format` | 仅 `json_mode=true` 时为 `{"type":"json_object"}` |

密钥只通过 Authorization header 发送，不进入 prompt。模型及网关是否支持 reasoning、实际默认强度，由服务端决定；客户端不会把 null 自动转换成 high，也不添加 thinking 参数。

用户消息结构：

```text
REQUESTED_COUNT=1
OBSERVATION_JSON:
{...以下上下文字段...}
```

## 上下文顶层

序列化顺序为 `schema_version/run/generation_history/first_round_summary/combat_key/state/combat_summary/total_events/omitted_events/recent_events`。固定协议最先发送，风格只发送一次，储君机制追加在共同 system 前缀后；同一场内稳定的牌组与历史放在变化频繁的战斗状态之前。地图路径历史已经不发送。

共同协议只列通用效果，不含中毒和星。`CharacterMechanics` 根据角色决定追加的可选扩展：静默开放中毒与 `target_poison`，储君开放星、星费用与 `self_stars`。持有原生 `PRISMATIC_GEM`（棱彩宝石）时开放所有已实现扩展；牌组中已有静默/储君原生卡池的卡牌时，开放相应扩展。牌组来源通过原生 `pool` ID 判断，不解析名称/说明，也不让已有生成牌反向解锁。旧日志缺少 `pool` 时仍可根据角色和遗物判断。

扩展只表示可用范围，不赋予权重或要求围绕角色机制设计。优势机制不作为 system 偏好列表，通用牌与配合牌同样可取；提示在选择和历史之间变化功能、费用和时机。原生观测里的机制不自动成为可生成效果。生成会话与 CLI `generate` 使用相同范围校验，LLM 返回未开放的中毒/星时整批拒绝。独立 `validate`、存档加载和旧候选保持原有结构校验，保留已有牌的兼容性。

| 字段 | 内容 |
| --- | --- |
| `schema_version` | 2 |
| `combat_key` | 战斗定位哈希，供请求归属和奖励恢复；不是玩家账号 ID |
| `run` | 当前爬塔、去重牌组、遗物和药水 |
| `state` | 当前战斗状态；战斗结束后的请求可能保留清理前的最后一份状态 |
| `recent_events` | 最近事件，默认最多 60 条；只带简短引用和实际事件数据 |
| `total_events` | 本场已捕获事件数 |
| `omitted_events` | 未进入 recent_events 的数量，包含事件窗口和 prompt 字符裁剪 |
| `combat_summary` | 从战斗开始累计的摘要；不会随 recent_events 裁剪而丢失 |
| `first_round_summary` | 第一轮敌方行动结束时固定的累计摘要；此前为 null。第一轮内胜利时为最终摘要 |
| `generation_history` | 最近 16 条生成记录＋当前池中候选，按 ID 去重；用于避免重复设计 |

### run

顺序为 `character/ascension/deck/relics/potions/act/floor/room_type/gold`。牌组按结构排序，避免原始列表重排破坏公共前缀。

`deck[]` 按下面字段相同的牌合并，增加 `count`：

- `id/pool/title/type/rarity`：原生模型 ID、原生卡池 ID、当前名称、类型、稀有度。
- `cost/star_cost/current_cost/current_star_cost`：原始能量费用、原始星费用、当前费用。`star_cost=-1` 表示没有星费用；0 表示存在零星费用。
- `x_cost/star_x_cost/upgraded/target/keywords`：X 费标记、升级状态、目标、关键词。
- `enchantment/affliction`：附魔/负面附着的 ID 和数值，可能为 null。
- `origin`：`native` 或 `generated`。
- `description`：原生牌当前说明；将星/能量图片资源标签替换为 ★/⚡，不重复发送 DynamicVar 内部数据。生成牌通过下面的结构表达效果，省略重复说明。
- `generated_definition`：生成牌的已验证结构，原生牌为 null。省略重复名称、风味和默认效果槽位（如 on_play、repeat=1、无缩放）；非默认的旧上限仍保留。这是观测压缩，不改写存档定义。
- `count`：相同条目的数量。

遗物/药水条目为 `id/title/description/counters`；仅保留实际存在的 `Amount/TimesUsed/Charges/Counter/Stacks` 计数器，不发送图标路径、内部标志和排序信息。

### state

- `round/side`：轮数和当前阵营。
- `player` 和 `allies[]`：`instance/id/side/hp/max_hp/block/alive/powers`；能力仅 `id/amount`。
- `player_combat`：`turn/phase/energy/max_energy/stars/orb_capacity/orbs/piles`。
- `orbs[]`：`id`。
- `piles[]`：`pile` 和 `cards[]`。卡牌仅 `instance/id/title/type/current_cost/current_star_cost/upgraded/origin`，不再复制整份说明和效果。
- `enemies[]`：`creature`（与上面的生物结构一致）、`move`、`intents[]`（`type/damage/hits`）。

无法取得的游戏字段可能为 null 或 `{"unavailable":true}`。不发送 seed、Steam ID、路径历史、反射得到的其他资源内部属性。

### recent_events

每条为 `sequence/round/side/detail`；`detail` 为原生日志条目的 `type` 和 `fields`。字段随事件类型不同：

- 出牌：`CardPlay.card` 简短引用、目标生物简短引用、自动出牌/重放信息、目标牌堆、实际能量/星资源消耗。
- 事件卡牌引用仅包含 `instance/id/title/type/origin`；当前费用留在 state 牌堆，避免每条事件重复发送。
- 伤害：来源、接收者、被格挡/未被格挡/过量伤害、死亡及破盾结果。
- 抽牌、生成、消耗、弃牌：卡牌引用和事件自身的标记。
- 能量/星变化、获得格挡、获得能力：数值和有关对象 ID。
- 怪物行动：怪物 ID、动作 ID、意图和目标。

引用不复制生物能力全集、卡牌完整定义或模型内部 state；后继怪物状态树也被省略。完整原始事件及每次状态快照继续写入 `data/combats`。

### combat_summary / first_round_summary

`complete`、`won`、`event_counts`、`cards_played[]`、`damage_dealt`、`damage_taken`、`damage_blocked`、`block_gained`、`stars_gained`、`stars_spent`、`energy_spent`。

`cards_played[]` 为 `id/title/type/origin/count`，统计本角色已完成的出牌。伤害字段根据 CreatureAttackedEntry 统计，不是所有 HP 变化的完整归因；资源消耗统计来自完成出牌的 resources。`won` 在最终摘要中有值，其余时点为 null。

### generation_history

每条为 `name/type/cost/star_cost/effects/keywords/status/combat_key`，效果默认槽位也被省略。状态包括 `candidate`、`shown`、`selected`、`skipped`、`expired`。历史不包含服务端 reasoning。

默认只要求一个核心想法，允许单效果，不要求组合多个效果。仅储君的 system 追加星效果、星费用、self_stars 的语义，明确这些是可选机制，不要求星主题或优先采用星机制。其他角色的生成协议没有这些字段。旧配置中完全匹配原厂 balanced 指令的文本自动采用新默认；自定义风格保持原样。模型仍只可从协议支持的效果组合中设计卡牌。

## LLM 最终输出

`choices[0].message.content` 必须为 JSON `{"cards":[...]}`，张数精确等于 REQUESTED_COUNT。没有额外设计说明、独立效果文本或代码。示例：

```json
{
  "cards": [
    {
      "schema_version": 4,
      "name": "星潮斩",
      "type": "attack",
      "rarity": "uncommon",
      "cost": 0,
      "star_cost": 2,
      "upgrade_cost": 0,
      "upgrade_star_cost": 1,
      "keywords": [],
      "flavor": "涅奥借来一线星光，让余辉也成为锋刃。",
      "effects": [
        {
          "kind": "damage",
          "target": "enemy",
          "amount": 8,
          "upgrade_amount": 3,
          "trigger": "on_play",
          "duration": 1,
          "max_per_turn": 1,
          "repeat": 1,
          "condition": "none",
          "scaling": "self_stars",
          "scaling_amount": 2
        }
      ]
    }
  ]
}
```

卡牌字段完整说明见 [CARD_PROGRAM.md](CARD_PROGRAM.md)。数值说明由客户端生成，LLM 不能自行写卡牌效果文案。升级费用字段是费用减少量。

v4：删除 `scaling_cap`，缩放不限计数，返回该字段（包括 0）会被拒绝。`max_per_turn=0` 表示事件不限次数，prompt 要求事件显式填写 0（兼容缺省仍为 1）；非事件为 1。没有强度打分上限、按费用预算、零费强制消耗或旧数值/持续/重复上限。仍校验正数、整数范围、字段合法性、目标与触发语义；原生引擎限制及避免递归卡死的内部保护仍生效。已有 v1/v2/v3 不迁移，原上限及候选指纹继续兼容。

## reasoning 日志

服务端 reasoning 来自 `choices[0].message.reasoning_content`，独立于最终 card JSON。即使最终内容无法解析或服务端报告 length，也先记录能够读取到的 reasoning。

`data/generation/<战斗>.jsonl` 中的 `generation_response`：

- `revision`：与 generation_request 相同的请求序号。
- `reasoning_content`：服务端字符串；服务未返回或 `record_generation_reasoning=false` 时为 null。
- `finish_reason`：已知的 stop/length/content_filter，否则 null。
- `prompt_tokens/completion_tokens/total_tokens`：服务端提供的整数 token 数，缺少时为 null。
- `prompt_cache_hit_tokens/prompt_cache_miss_tokens`：DeepSeek 返回的命中/未命中 token 数。命中字段也支持标准 `usage.prompt_tokens_details.cached_tokens`；缺失字段为 null，不推算未命中量。

[DeepSeek 缓存文档](https://api-docs.deepseek.com/guides/kv_cache/)说明缓存依赖重复前缀。离线字符数与公共前缀对比仅用于衡量请求结构；实际命中还受服务端缓存状态影响，以这些 usage 字段为准。

需开启 `record_generation`；reasoning 开关默认 true。只保存这些显式字段，不保存 HTTP headers 和错误体；已知密钥和 base_url 如被 reasoning 回显则替换为 `[redacted]`。reasoning 是服务端给出的内容，不能保证它完整解释设计过程。

CLI generate 的响应诊断写入 `<输出路径>.response.local.json`；CLI prompt 命令可以离线输出实际 prompt，不调用服务端。

## 候选池和时机

首次预生成在第一轮敌方行动结束后，全部挡住或敌方没有攻击仍会触发；第一轮内胜利则提交最终摘要。后续仍受每场请求预算和最小间隔约束。

每批成功结果追加到本局池，默认容量 24；以效果、费用、升级和关键词的指纹去重（忽略名称/风味文本），超容量淘汰最旧候选。选奖励时优先最近未展示过的机制形状，平分时选较老候选。已展示牌从池移除，无论最后是否选择；未展示候选可跨战斗出现。

`data/runs/<run-key>.json` 在一个原子事务中保存候选、历史、已见指纹和已冻结奖励（包括空奖励）。run-key 包含存档原生开局时间、种子、角色、进阶，防止不同时间的同种子新局复用池。SL 重开已展示的战斗会恢复固定奖励，不再为该战斗启动请求。旧 data/rewards 缓存仅在时间属于当前 run 时导入。

战斗胜利/奖励展示只停止新的刷新，不取消在途请求；晚到的结果进入池，不改变已展示奖励。下一场战斗可以与上一场尚未返回的请求同时进行。死亡、放弃、退出当前 run、重新加载 run 时关闭池并取消旧请求。退出程序不能保留尚未完成的网络请求，已经落盘的内容可恢复。请求回调仅持有原来的池，不能写入后来的新局。
