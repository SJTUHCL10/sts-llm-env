# 卡牌协议 v5

卡牌是受校验的数据程序，由游戏侧通过原生命令执行。LLM 返回 `{"cards":[...]}`；文本由程序生成，不参与执行。此轮直接替换旧协议，不提供旧定义迁移。

## 两个完整形态

```json
{
  "name": "涅奥的低语",
  "type": "attack",
  "rarity": "common",
  "forms": [
    {"cost":{"energy":1},"effects":[{"kind":"damage","target":"enemy","amount":7}]},
    {"cost":{"energy":1},"effects":[{"kind":"damage","target":"all_enemies","amount":9},{"kind":"draw","amount":1}]}
  ]
}
```

`forms[0]` 为基础形态，`forms[1]` 为升级形态，必须恰好两个，彼此不继承。每个形态独立声明费用、关键词、即时效果与规则；升级可以改变目标、动作顺序、效果数量和机制。名称、类型、稀有度及可选 `flavor` 属于卡牌家族。没有 `schema_version`、`upgrade_*` 或 LLM 生成的实例 ID。

费用为 `cost:{energy?,stars?,energy_x?,stars_x?}`，固定费用数值非负。省略 `energy` 默认为 0，省略 `stars` 表示不使用星费用；X 费用只需 `energy_x:true` 或 `stars_x:true`，不需要额外填写对应的固定费用。例如 `cost:{energy:1,stars_x:true}` 消耗 1 能量和全部星。X 标记优先于同种资源的固定费用，兼容已有显式填写 0 的定义。关键词：`exhaust/ethereal/retain/innate/sly`；能力牌允许 `retain` 和已启用职业的 `sly`，但不能带 `exhaust`。`effects`、`rules`、`keywords` 可省略。每个形态共有 1～24 个动作、最多 8 条规则；每条规则 1～8 个动作。

## 动作与目标

动作公共字段：`kind,target?,amount?,repeat?,condition?`。默认目标为自己、重复一次；动作按顺序执行，随机目标每次重新抽取。无关字段会被拒绝。

| kind | 必要参数 / 附加参数 |
| --- | --- |
| damage | amount、敌人目标；actor 可为 osty |
| block / draw / gain_energy / gain_stars / heal / lose_hp | amount |
| apply_power | amount、power；力量/集中可用 until:turn |
| discard / exhaust / upgrade / play | 卡牌目标 |
| move | 卡牌目标、to |
| select | 卡牌目标、as；不得附带条件或重复 |
| copy | 卡牌目标、to；count 默认 1 |
| transform | 卡牌目标、card |
| create_card | card、to；count 默认 1 |
| add_keyword / remove_keyword | 卡牌目标、keyword |
| set_cost | 卡牌目标、amount、until:turn 或 combat |
| summon / forge / channel / orb_slots | amount；channel 还需 orb |
| evoke / orb_passive | 球目标；evoke 可用 remove:false |

生物目标：`self/enemy/all_enemies/random_enemy/osty/event.target`。规则没有玩家当前选定的 `enemy`，应使用随机、全体或事件目标。即时攻击使用原生攻击修正；规则和技能中的直接伤害使用 Unpowered。`lose_hp` 绕过格挡并触发原生伤害流程。

卡牌目标：`this_card`、`event.card`、`selected:名称`，或统一选择器：

```json
{"pile":"hand","pick":"choose","count":2,"filter":{"type":"attack"},"up_to":true}
```

- `pile`: hand/draw/discard/exhaust。
- `pick`: random/choose/all/first/last；数量默认 1，all 不带 count。随机选择不重复；选择数量会缩至可用张数；up_to 仅用于 choose，允许选零张。
- `filter`: id/type/rarity/cost/upgraded/keyword，可组合，匹配同一张牌。费用筛选使用当前能量费用，X 不视为 0 费。
- `select` 绑定一次选择，后续多个动作使用同一批实例；绑定仅在当前动作组内有效。
- 牌堆选择排除正在执行的来源牌。动作只操作战斗中的牌；不修改永久牌组。`this_card` 在持久规则中通过内部实例标识解析，复制品拥有新标识。

目的地 `to` 使用同一组牌堆名称；抽牌堆位置 `position` 为 top（默认）/bottom/random。多张弃牌使用原生批量命令，保证奇巧的结算顺序。自动打出通过原生 AutoPlay 选择目标与处理费用。

## 生成、变化与复制

固定来源与卡池来源共用 `card`：

```json
{"kind":"create_card","card":{"id":"soul"},"to":"hand","count":2}
{"kind":"transform","target":{"pile":"hand","pick":"choose","filter":{"type":"status"}},"card":{"pool":"character","pick":"random"}}
{"kind":"create_card","card":{"pool":"colorless","pick":"choose","options":3,"filter":{"type":"skill"},"upgraded":true},"to":"hand"}
```

固定来源使用受支持的别名：soul/shiv/wound/dazed/burn/void/slimed/fuel/debris/minion_strike/minion_sacrifice/minion_dive/sovereign_blade。原生卡池来源为 character/colorless；random 随机生成，choose 展示备选并选择一张，options 默认 3、上限 10。使用原生解锁与可生成性筛选，不接受任意模型路径。固定来源也支持 upgraded:true。

生成与变化都使用同一种来源；变化保留原生变化流程与牌堆位置。复制保留来源牌的当前原生状态。所有这些操作均局限于本场战斗。

## 数值与条件

普通数值直接写整数，动态数值使用小型表达式：

```json
{"add":[5,{"mul":[{"stat":"power","of":"target","id":"poison"},2]}]}
{"div":[{"stat":"max_hp","of":"osty"},2]}
{"op":"lt","left":{"mul":[{"stat":"hp"},2]},"right":{"stat":"max_hp"}}
```

表达式为整数、stat、add、sub、mul、div 之一；add/mul 有 2～4 个操作数，sub/div 恰好两个。`{"sub":[a,b]}` 表示 a 减 b；div 向下取整，嵌套最多六层。字面分母不能为零；运行时零分母、溢出会中止效果并报告执行失败。普通数量小于零按零执行；apply_power 与 orb_slots 支持有符号数。没有强度预算或缩放上限。旧定义中的 add 与负数乘积仍可执行，描述会将两项相加中的第二项乘 -1 显示为减法；此扩展不改变 v5 存档版本。

生物数值：hp/max_hp/block/power，of 默认 self，可为 target/osty；power 必须带 id。玩家数值：energy/stars/hand_size/draw_size/discard_size/exhaust_size/orb_count/orb_capacity。上下文数值：paid_energy/paid_stars（本次打出实付资源快照）、event_amount、damage_dealt（最近一个伤害动作的 TotalDamage）。状态在动作执行时读取。

条件为比较 `eq/ne/gt/ge/lt/le`，或 all/any/not。条件满足后执行动作；数值不存在的事件载荷为零，不存在的卡牌/目标不产生操作。目标相关条件按每个目标评估。

## 规则、事件与计数

```json
{
  "trigger":{"event":"card_played","filter":{"type":"attack"},"occurrence":{"first":1}},
  "lifetime":"combat",
  "effects":[{"kind":"draw","amount":1},{"kind":"block","amount":3}]
}
```

规则把触发器和一组效果分开，多个收益只占一次额度。事件：turn_start/turn_end/card_played/card_drawn/card_discarded/card_exhausted/card_generated/damage_received/attack_completed/summoned/orb_channeled/orb_evoked。卡牌事件支持 filter；其他事件不能使用卡牌筛选。

- `occurrence:{first:N|nth:N|every:N,within:turn|combat}` 表示原生事件的序号，默认按回合。包括规则建立以前的匹配事件；历史筛选属性在事件发生时快照，之后升级牌不会改写历史。每回合第一张攻击使用 first:1。
- `limit:{count:N,within:turn|combat}` 表示规则满足自身条件后最多生效次数，默认按回合。失败条件不消耗额度；没有 limit 就不限次。它替代旧 max_per_turn，和“第几张牌”语义不同。
- `lifetime` 默认为 combat。turn 包含建立当回合，turns 默认 1；turn_start 的有限时长计数未来回合开始。next_turn 仅允许 turn_start，下一回合检查一次后到期，条件失败也到期。
- 规则在即时动作之后建立，忽略建立它的那一次打出。每次打出创建独立实例并捕获当时形态、变量与实付资源，之后升级来源牌不会改写已建立规则。
- event.card 来自卡牌事件；card_played 还提供目标。damage_received 提供攻击者与未格挡伤害；attack_completed 提供来源卡、首个命中目标与总伤害；summoned 提供数量；orb_evoked 提供首个目标。

防止自递归重入，并限制触发深度和单次解析操作数量。它们是防卡死保护，不是卡牌强度限制。

## 原生状态与角色范围

状态别名：strength/dexterity/weak/vulnerable/frail/poison/doom/focus/vigor/thorns/plating/intangible/artifact/buffer/retain_block。状态本身使用原生持续与叠加规则。until:turn 的力量/集中增减交给原生临时能力，在目标方回合结束时恢复。

奥斯提、灵魂、灾厄仅在亡灵范围开放；充能球、集中、燃料在机器人范围开放；星、铸造与储君衍生牌在储君范围开放；毒、小刀、奇巧在静默范围开放。棱彩宝石或牌组中已存在对应原生卡池可开放扩展，生成牌不作为解锁证据。扩展是可选能力，没有频率或必选主题。

具体覆盖和限制见 [MECHANIC_COVERAGE.md](MECHANIC_COVERAGE.md)，可验证示例见 examples/mechanic-cards.json。
