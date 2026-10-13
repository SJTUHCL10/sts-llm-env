# 卡牌协议 v7

卡牌是受校验的数据程序，由游戏侧通过原生命令执行。LLM 返回 `{"cards":[...]}`；文本由程序生成，不参与执行。v7 增加最大生命、金币和 damage.on_fatal，旧候选池不迁移。

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
| gain_max_hp / lose_max_hp | amount；生物目标默认 self |
| gain_gold / lose_gold | amount；仅玩家自身 |
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


`gain_max_hp/lose_max_hp` 使用原生最大生命命令，玩家的变化永久保留。增加上限后按实际增加量调用回血；降低上限时，当前生命超过新上限的部分走原生扣血流程（来源为卡牌）。`gain_gold/lose_gold` 永久改变玩家金币，获得金币沿用原生修正与钩子，失去金币使用 Lost 类型且最低为零。四种动作都接受非负数值或动态表达式，可用于普通 effects、rules 或 on_fatal；不限制职业。

### 斩杀

只有 `damage` 可以附带非空 `on_fatal:[动作]`：

```json
{"kind":"damage","target":"enemy","amount":10,"on_fatal":[{"kind":"gain_max_hp","amount":3},{"kind":"gain_gold","amount":20}]}
```

伤害前记录各目标的原生 `ShouldOwnerDeathTriggerFatal()`，伤害后使用 `DamageResult.WasTargetKilled` 判断本次伤害是否击杀符合斩杀资格的目标；随从和原生特殊敌人的限制沿用游戏。每次 damage 动作最多执行一次斩杀组，全体伤害击杀多名敌人也只执行一次，包括因条件或目标数值不同而拆开的原生命令。repeat 的每次伤害独立判定；跳过或未击杀不触发。

斩杀组在伤害动作内部执行，击杀最后一个敌人仍完成已触发的组，普通后续动作仍受战斗结束保护。组允许全部已支持动作，包含继续携带 on_fatal 的 damage；战斗操作能否在结束阶段生效由原生命令决定。玩家死亡则停止。组继承当前打出/规则的选定目标、事件数据和来源牌，不把死亡目标重绑为事件目标；enemy 仍指本次打出选定的敌人，死亡目标不会再被操作。

组可读取外层 select 绑定，内部新增绑定不会泄漏到外层。嵌套最多六层，嵌套动作计入每个形态 24 个动作、每条规则 8 个动作的总限制；运行中仍共享 4096 次操作预算。数值变量、文本、升级和保存使用包含嵌套动作的前序顺序。斩杀收益里的 damage_dealt 是刚完成的整个 damage 动作的总伤害；若组内又造成伤害，后续读取其最新结果。

### 奥斯提的生成限制

本机 v0.111.0 中，攻击先对原始目标计算伤害修正、荆棘和角色格挡，再由 `DieForYouPower` 将未格挡的攻击伤害转给奥斯提，最后对实际承伤者计算生命损失修正。因此奥斯提自己的格挡不会抵挡转移伤害；荆棘和易伤也不会在转移后重新计算。新生成卡禁止 `block(target:osty)`，`apply_power(target:osty)` 只允许下列别名，并继续受当前局角色机制开关限制：

| power | 奥斯提上的实际用途 |
| --- | --- |
| strength / vigor | 增加奥斯提的原生攻击伤害；技能伤害及规则触发伤害使用 Unpowered，不享受这些加成 |
| weak | 降低奥斯提的原生攻击伤害；属于负面效果 |
| buffer / intangible | 在转移后免除一次生命损失／将生命损失限制为 1 |
| artifact | 阻止直接施加给奥斯提的减益；不会代替角色阻止角色收到的减益 |
| poison / doom | 中毒扣血／灾厄达到生命值时死亡，属于负面效果；中毒仍需启用静默机制 |

禁止给奥斯提施加 dexterity/vulnerable/frail/focus/thorns/plating/retain_block：分别依赖自身格挡、原始受击目标、充能球或格挡保留，无法改善它的常规攻击与抵挡流程。该限制只约束新生成，不删除全局状态目录，也不自动重写已保存卡的无效效果。旧定义的读取与数值语义见下方 v6 说明；旧候选池不迁移。

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

普通数值允许整数和小数，动态数值使用小型表达式：

```json
{"add":[5,{"mul":[{"stat":"power","of":"target","id":"poison"},2]}]}
{"div":[{"stat":"max_hp","of":"osty"},2]}
{"op":"lt","left":{"stat":"hp"},"right":{"mul":[{"stat":"max_hp"},0.5]}}
```

表达式为 decimal 字面量、stat、add、sub、mul、div 之一；add/mul 有 2～4 个操作数，sub/div 恰好两个。sub 表示减法；div 使用 decimal 普通除法，中间运算不转成整数，嵌套最多六层。字面分母不能为零；运行时零分母和 decimal 溢出会中止效果。decimal 精度是有限的，不是任意精度有理数。上述半血条件在 39/79 时成立、40/80 时不成立；mul 0.5 与 div 2 等价。旧的 add 与负数乘积仍显示为减法。

表达式的整数化发生在最终使用处：repeat、生成/复制/选牌 count、channel、forge、set_cost 向零截断，非负数量负值按零处理；orb_slots 保留符号后向零截断。伤害、格挡、生命、最大生命、金币、能量、星、状态层数和召唤量保留 decimal 进入原生命令，由游戏在最终状态变更时处理整数；正数抽牌量沿用原版向上取整（1.5 抽 2 张），预览一致。固定费用、过滤费用、options、turns、occurrence 和 limit 仍为整数。离散效果优先设计成整数量。

v6 直接替换旧 div 语义，旧候选池不迁移；读取已有卡牌时也使用普通除法。新的小数语义不改为伤害倍率钩子：表达式先算出基础效果数值，再进入原生修正流程。

生物数值：hp/max_hp/block/power，of 默认 self，可为 target/osty；power 必须带 id。玩家数值：gold/energy/stars/hand_size/draw_size/discard_size/exhaust_size/orb_count/orb_capacity。上下文数值：paid_energy/paid_stars（本次打出实付资源快照）、event_amount、damage_dealt（最近一个伤害动作的 TotalDamage）。状态在动作执行时读取。

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
