# LLM 请求与输出协议 · 2026-10-04

当前新生成卡牌使用 schema v3；观测上下文使用 schema v2。此文档描述代码实际发送的字段，不是原始战斗日志的字段全集。

## HTTP 请求

向 `provider.base_url + /chat/completions` POST：

| 字段 | 内容 |
| --- | --- |
| `model` | 配置的模型名 |
| `messages` | 两条消息：`system`（风格系统提示词＋固定效果协议），`user`（数量＋风格指令＋观测 JSON） |
| `max_tokens` 或 `max_completion_tokens` | 按 `token_limit_parameter` 选择字段；值为配置 token 预算 |
| `temperature` | 仅 `include_temperature=true` 时发送 |
| `reasoning_effort` | 非 null 时原样发送；null/缺省时整个字段省略，使用服务端默认 |
| `response_format` | 仅 `json_mode=true` 时为 `{"type":"json_object"}` |

密钥只通过 Authorization header 发送，不进入 prompt。`deepseek-flash` 通过哪个网关提供、网关是否转发 reasoning、实际默认强度，由服务端决定；客户端不会把 null 自动转换成 high，也不添加 thinking 参数。

用户消息结构：

```text
REQUESTED_COUNT=1
STYLE: <当前风格指令>
OBSERVATION_JSON:
{...以下上下文字段...}
```

## 上下文顶层

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

`act`、`floor`、`ascension`、`character`、`room_type`、`gold`、`deck`、`relics`、`potions`。

`deck[]` 按下面字段相同的牌合并，增加 `count`：

- `id/title/type/rarity`：原生模型 ID、当前名称、类型、稀有度。
- `cost/star_cost/current_cost/current_star_cost`：原始能量费用、原始星费用、当前费用。`star_cost=-1` 表示没有星费用；0 表示存在零星费用。
- `x_cost/star_x_cost/upgraded/target/keywords`：X 费标记、升级状态、目标、关键词。
- `enchantment/affliction`：附魔/负面附着的 ID 和数值，可能为 null。
- `origin`：`native` 或 `generated`。
- `description`：当前实际卡牌说明；将星/能量图片资源标签替换为 ★/⚡，不重复发送 DynamicVar 内部数据。
- `generated_definition`：生成牌的完整已验证定义，原生牌为 null。可识别历史生成牌并理解其实际执行结构。
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
- 伤害：来源、接收者、被格挡/未被格挡/过量伤害、死亡及破盾结果。
- 抽牌、生成、消耗、弃牌：卡牌引用和事件自身的标记。
- 能量/星变化、获得格挡、获得能力：数值和有关对象 ID。
- 怪物行动：怪物 ID、动作 ID、意图和目标。

引用不复制生物能力全集、卡牌完整定义或模型内部 state；后继怪物状态树也被省略。完整原始事件及每次状态快照继续写入 `data/combats`。

### combat_summary / first_round_summary

`complete`、`won`、`event_counts`、`cards_played[]`、`damage_dealt`、`damage_taken`、`damage_blocked`、`block_gained`、`stars_gained`、`stars_spent`、`energy_spent`。

`cards_played[]` 为 `id/title/type/origin/count`，统计本角色已完成的出牌。伤害字段根据 CreatureAttackedEntry 统计，不是所有 HP 变化的完整归因；资源消耗统计来自完成出牌的 resources。`won` 在最终摘要中有值，其余时点为 null。

### generation_history

每条为 `combat_key/status/name/type/cost/star_cost/effects/keywords`。状态包括 `candidate`、`shown`、`selected`、`skipped`、`expired`。历史不包含服务端 reasoning。

默认强调一张牌一个核心想法、通常 1～2 个关联效果，考虑整副牌和完整战斗摘要，避免持续模仿近期防牌或历史生成牌。储君优先探索星收入、星费用和资源取舍；其他角色不使用星机制。模型仍只可从协议支持的效果组合中设计卡牌。

## LLM 最终输出

`choices[0].message.content` 必须为 JSON `{"cards":[...]}`，张数精确等于 REQUESTED_COUNT。没有额外设计说明、独立效果文本或代码。示例：

```json
{
  "cards": [
    {
      "schema_version": 3,
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
          "scaling_amount": 2,
          "scaling_cap": 0
        }
      ]
    }
  ]
}
```

卡牌字段完整说明见 [CARD_PROGRAM.md](CARD_PROGRAM.md)。数值说明由客户端生成，LLM 不能自行写卡牌效果文案。升级费用字段是费用减少量。

v3：`max_per_turn=0` 表示事件不限次数；`scaling_cap=0` 表示缩放不限计数。prompt 要求事件显式填写 0（该字段为兼容旧牌，反序列化缺省仍为 1）；非事件为 1。没有强度打分上限、按费用预算、零费强制消耗或旧数值/持续/重复上限。仍校验正数、整数范围、字段合法性、目标与触发语义；原生引擎限制及避免递归卡死的内部保护仍生效。已有 v1/v2 不迁移，也不改变原来的上限和行为。

## reasoning 日志

服务端 reasoning 来自 `choices[0].message.reasoning_content`，独立于最终 card JSON。即使最终内容无法解析或服务端报告 length，也先记录能够读取到的 reasoning。

`data/generation/<战斗>.jsonl` 新增 `generation_response`：

- `revision`：与 generation_request 相同的请求序号。
- `reasoning_content`：服务端字符串；服务未返回或 `record_generation_reasoning=false` 时为 null。
- `finish_reason`：已知的 stop/length/content_filter，否则 null。
- `prompt_tokens/completion_tokens/total_tokens`：服务端提供的整数 token 数，缺少时为 null。

需开启 `record_generation`；reasoning 开关默认 true。只保存这些显式字段，不保存 HTTP headers 和错误体；已知密钥和 base_url 如被 reasoning 回显则替换为 `[redacted]`。reasoning 是服务端给出的内容，不能保证它完整解释设计过程。

CLI generate 的响应诊断写入 `<输出路径>.response.local.json`；CLI prompt 命令可以离线输出实际 prompt，不调用服务端。

## 候选池和时机

首次预生成在第一轮敌方行动结束后，全部挡住或敌方没有攻击仍会触发；第一轮内胜利则提交最终摘要。后续仍受每场请求预算和最小间隔约束。

每批成功结果追加到本局池，默认容量 24；以效果、费用、升级和关键词的指纹去重（忽略名称/风味文本），超容量淘汰最旧候选。选奖励时优先最近未展示过的机制形状，平分时选较老候选。已展示牌从池移除，无论最后是否选择；未展示候选可跨战斗出现。

`data/runs/<run-key>.json` 在一个原子事务中保存候选、历史、已见指纹和已冻结奖励（包括空奖励）。run-key 包含存档原生开局时间、种子、角色、进阶，防止不同时间的同种子新局复用池。SL 重开已展示的战斗会恢复固定奖励，不再为该战斗启动请求。旧 data/rewards 缓存仅在时间属于当前 run 时导入。

战斗胜利/奖励展示只停止新的刷新，不取消在途请求；晚到的结果进入池，不改变已展示奖励。下一场战斗可以与上一场尚未返回的请求同时进行。死亡、放弃、退出当前 run、重新加载 run 时关闭池并取消旧请求。退出程序不能保留尚未完成的网络请求，已经落盘的内容可恢复。请求回调仅持有原来的池，不能写入后来的新局。
