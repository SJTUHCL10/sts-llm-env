# 卡牌协议 v9

卡牌是受校验的数据程序，由游戏侧通过原生命令执行。LLM 返回 `{"cards":[...]}`；文本由程序生成，不参与执行。v8 增加格挡、逐段伤害、能力层数、铸造和星事件，以及事件来源/对象/筛选。v9 移除整次攻击事件，伤害规则按每段、每目标结算；按出牌触发使用 card_played。旧候选池和含已移除事件的旧卡不迁移。

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

禁止给奥斯提施加 dexterity/vulnerable/frail/focus/thorns/plating：分别依赖自身格挡、原始受击目标或充能球，无法改善它的常规攻击与抵挡流程。该限制只约束新生成，不删除全局状态目录，也不自动重写已保存卡的无效效果。旧定义的读取与数值语义见下方 v6 说明；旧候选池不迁移。

卡牌目标：`this_card`、`event.card`、`selected:名称`，或统一选择器：

```json
{"pile":"hand","pick":"choose","count":2,"filter":{"type":"attack"},"up_to":true}
```

- `pile`: hand/draw/discard/exhaust。
- `pick`: random/choose/any/all/first/last。`any` 由玩家选择零张至全部符合条件的牌（如储君“护驾！！！”）；`all` 自动选中全部。两者均不带 `count` 或 `up_to`。其余模式数量默认 1，数量会缩至可用张数；随机选择不重复。`up_to: true` 仅用于 `choose`，允许选零张；例如 `pick:choose,count:10,up_to:true` 表示至多选 10 张，`up_to` 本身不是数量。
- `filter`: id/type/rarity/cost/min_cost/max_cost/tag/upgraded/keyword，可组合，匹配同一张牌。cost 为等值；min_cost/max_cost 为包含边界的区间，不能与 cost 混用。费用筛选使用当前能量费用并排除 X。tag 为 strike/defend/shiv/minion/osty_attack，匹配原生元数据，不给生成牌添加该标签。
- `select` 绑定一次选择，后续多个动作使用同一批实例；绑定仅在当前动作组内有效。
- 牌堆选择排除正在执行的来源牌。动作只操作战斗中的牌；不修改永久牌组。`this_card` 在持久规则中通过内部实例标识解析，复制品拥有新标识。

例如任意选择手牌中的状态牌并变化为小刀（选择一次，后续动作复用）：

```json
[
  {"kind":"select","target":{"pile":"hand","pick":"any","filter":{"type":"status"}},"as":"statuses"},
  {"kind":"transform","target":"selected:statuses","card":{"id":"shiv"}}
]
```

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

生物数值：hp/max_hp/block/power，of 默认 self，可为 target/osty；规则中还可用 event.actor/event.source/event.subject/event.target，power 必须带 id。玩家数值：gold/energy/stars/hand_size/draw_size/discard_size/exhaust_size/orb_count/orb_capacity。上下文数值：paid_energy/paid_stars（安装规则那次打出的实付资源快照）、event_amount、damage_dealt（最近一个伤害动作的 TotalDamage）。v8 新增 event_total_damage/event_unblocked_damage（逐段伤害事件）、event_paid_energy/event_paid_stars（card_played 的触发出牌实付资源）、event_target_count（本次事件去重后的目标数，包括随后已死亡的目标）。状态在动作执行时读取。

条件为比较 `eq/ne/gt/ge/lt/le`，或 all/any/not。条件满足后执行动作；数值不存在的事件载荷为零，不存在的卡牌/目标不产生操作。目标相关条件按每个目标评估。

## 规则、事件与计数

```json
{
  "trigger":{"event":"card_played","filter":{"type":"attack"},"occurrence":{"first":1}},
  "lifetime":"combat",
  "effects":[{"kind":"draw","amount":1},{"kind":"block","amount":3}]
}
```

规则把触发器和一组效果分开，多个收益只占一次额度。基础事件：turn_start/turn_end/card_played/card_drawn/card_discarded/card_exhausted/card_generated/damage_received/summoned/orb_channeled/orb_evoked。其余事件如下：

| event | 触发点与 event_amount | 默认范围 |
| --- | --- | --- |
| block_gained | AfterBlockGained，原生正数格挡增益量 | 自己获得格挡 |
| damage_given | AfterDamageGiven，每段每目标，TotalDamage（含格挡） | 自己或奥斯提造成伤害 |
| power_amount_changed | AfterPowerAmountChanged，非零带符号变化量 | 自己施加的能力变化 |
| forged | AfterForge，正数原生铸造量 | 自己铸造 |
| stars_gained | AfterStarsGained，正数原生获得量 | 自己获得星 |
| stars_spent | AfterStarsSpent，正数原生支出量，付款期间、出牌前 | 自己花费星 |

`filter` 在五个卡牌事件中匹配事件卡；在 damage_received/damage_given/block_gained/power_amount_changed/forged 中匹配来源卡。来源卡不存在时不匹配，不用安装规则的卡补充。伤害动作现在向原生命令保留来源卡；原生没有提供来源卡的其他回调仍可能为空。

抽到／丢弃／消耗／生成一张牌时的 event.card 是被操作的牌，不能据此推断“引起该动作的模型是卡牌”；这些回调没有暴露因果模型时 source_kind 保持 unknown。card_played 的来源模型则是正在执行的卡牌。生成牌回调可以知道 creator 生物，但仍不猜测引起生成的卡牌或能力。

`where` 是与卡牌 filter 分开的事件筛选，各字段为 AND：

| 字段 | 值与限制 |
| --- | --- |
| actor / target / source | self/osty/self_or_osty/enemy/any，分别为执行者、受影响者和来源生物；any 仍要求有已知生物 |
| source_kind | card/power/relic/orb/other/unknown，原生暴露的来源模型类别；未知不表示没有原因 |
| own_turn | true 仅己方回合，false 仅非己方回合；不等同于 lifetime:turn |
| attack | 原生 powered attack 性质，只用于 damage_given/damage_received；不是按来源卡的 type 猜测 |
| power / power_type / temporary | 仅 power_amount_changed；power 为能力别名，power_type 为 buff/debuff，temporary 筛选 ITemporaryPower；按本次带符号变化判断正负面，减力量可以是 debuff |
| orb | 仅两个球事件；lightning/frost/dark/plasma/glass |
| from_hand_draw | 仅 card_drawn；true 为回合初始抽牌，false 排除初始抽牌 |
| caused_by_ethereal | 仅 card_exhausted，是否由虚无造成 |
| auto_play / last_in_series | 仅 card_played，是否自动打出／连打系列最后一次 |

显式 actor 可扩大默认执行者范围。damage_received/block_gained 的 target 可扩大默认受影响者范围；power_amount_changed 的 target 可观察指定对象接收的变化，即使 applier 未知。其余情况沿用默认范围。例如只写 own_turn 并不会将“自己造成伤害”扩大为“所有生物造成伤害”。多目标事件的 target 筛选表示任一目标匹配，event.targets 仍保留完整目标集合，不将其裁剪。turn_start/turn_end 的对象和阶段固定，不使用 where。原生星回调不提供来源卡、模型或来源生物，不能使用来源筛选；其他回调的未知来源也不会猜成卡牌、能力或遗物。

```json
{
  "trigger":{"event":"power_amount_changed","where":{"actor":"self","target":"enemy","power_type":"debuff","temporary":false}},
  "effects":[{"kind":"damage","target":"event.subject","amount":4}]
}
```

事件生物目标新增 `event.actor`、`event.source`、`event.subject`、`event.targets`。actor 为执行者（伤害施伤者／能力施加者；格挡为获得者），source 为已知来源生物，subject 为受影响生物，targets 为全部受影响生物，逐段伤害事件只有单次命中目标，激发球时保留所有目标；多目标动作仅对去重后的存活目标执行。subject 在多目标事件中为首个目标，按目标读取数值可用 `of:target`。缺失引用不操作，缺失生物数值读 0。card_played 的 target 来自该次出牌，非单目标牌可以为空。

damage_received 为保存兼容继续将 event.target 设为攻击者、event_amount 设为未格挡伤害；event.subject/event.targets 才是实际受伤者。其余新事件的 event.target 为受影响对象。damage_given 按每段、每个目标分别触发，event_amount 为 TotalDamage，完全格挡仍可为正数；要求实际掉血时使用 event_unblocked_damage > 0。`where.target` 始终筛选真实受影响对象。

“黑洞”式支出后收益应使用 `card_played + where.last_in_series:true + condition event_paid_stars > 0`，避免连打重复结算实付星；不要同时给同一个支出收益再加 stars_spent 监听。stars_spent 适合“群星之子”式付款时立即获得格挡。可执行例子见 [event-cards.json](../examples/event-cards.json)。

- `occurrence:{first:N|nth:N|every:N,within:turn|combat}` 仅用于五个卡牌事件，表示原生事件的序号，默认按回合。包括规则建立以前的匹配事件；历史筛选属性（包括费用区间与标签）在事件发生时快照，之后升级牌不会改写历史。每回合第一张攻击使用 first:1。原生历史不保留完整 where 上下文，因此 occurrence 不能与 where 混用；事件筛选后的成功生效额度使用 limit。
- `limit:{count:N,within:turn|combat}` 表示规则满足自身条件后最多生效次数，默认按回合。失败条件不消耗额度；没有 limit 就不限次。它替代旧 max_per_turn，和“第几张牌”语义不同。
- `lifetime` 默认为 combat。turn 包含建立当回合，turns 默认 1；turn_start 的有限时长计数未来回合开始。next_turn 仅允许 turn_start，下一回合检查一次后到期，条件失败也到期。
- 规则在即时动作之后建立，忽略建立它的那一次打出。每次打出创建独立实例并捕获当时形态、变量与实付资源，之后升级来源牌不会改写已建立规则。
- event.card 来自卡牌事件；card_played 还提供目标。damage_received 提供攻击者与未格挡伤害；damage_given 提供单次命中的来源卡、目标与伤害；summoned 提供数量；orb_evoked 提供首个目标。

防止自递归重入，并限制触发深度和单次解析操作数量。它们是防卡死保护，不是卡牌强度限制。

## 原生状态与角色范围

状态别名：strength/dexterity/weak/vulnerable/frail/poison/doom/focus/vigor/thorns/plating/intangible/artifact/buffer。状态本身使用原生持续与叠加规则。until:turn 的力量/集中增减交给原生临时能力，在目标方回合结束时恢复。

奥斯提、灵魂、灾厄仅在亡灵范围开放；充能球、集中、燃料在机器人范围开放；星、铸造与储君衍生牌在储君范围开放；毒、小刀、奇巧在静默范围开放。棱彩宝石或牌组中已存在对应原生卡池可开放扩展，生成牌不作为解锁证据。扩展是可选能力，没有频率或必选主题。

具体覆盖和限制见 [MECHANIC_COVERAGE.md](MECHANIC_COVERAGE.md)，可验证示例见 examples/mechanic-cards.json。

### 充能球种类与玻璃球

`channel` 的 `orb` 支持 `lightning`（闪电）、`frost`（冰霜）、`dark`（黑暗）、`plasma`（等离子）、`glass`（玻璃）以及 `random`（交给原生游戏随机选择，并非第六种球）。例如 `{"kind":"channel","orb":"glass","amount":1}`。五种球及随机选择已有协议校验、原生执行映射和中文文案支持；各球的背景说明同样进入机器人范围的 system prompt。

玻璃球的被动与激发均对所有敌人造成伤害。每次正值被动触发都会将基础值减少 1，最低为 0，额外 `orb_passive` 也会造成此衰减；正常回合结束会触发被动。激发伤害为当前经集中修正的被动值的两倍。玻璃受集中影响，等离子不受集中影响。实际数值、修正与触发仍交给原生球实现；协议不能直接修改单个球的内部存储值。
