# 卡牌协议 v4

新牌使用 v4，旧 v1/v2/v3 卡牌及保存的能力状态继续按原规则执行。效果文字由 CardText 生成，不执行模型文本。完整上下文和 HTTP 输出见 [LLM_PROTOCOL.md](LLM_PROTOCOL.md)。

## 新卡牌字段

| 字段 | 说明 / 缺省 |
| --- | --- |
| `schema_version` | 新生成必须为 4 |
| `name/flavor` | 纯文本名称≤40字符；风味≤160字符，风味可空 |
| `type` | attack / skill / power |
| `rarity` | common / uncommon / rare |
| `cost` | 非负整数能量费用，无原来的 0～5 上限 |
| `star_cost` | -1（没有星费用）或非负整数星费用 |
| `upgrade_cost/upgrade_star_cost` | 升级减少的费用，缺省0，不能超过原费用 |
| `keywords` | exhaust / ethereal / retain / innate；最多3种，不重复 |
| `effects` | 1～8 条有序效果；围绕一个想法设计，单效果也可以 |

`kind` 支持 damage、block、draw、energy、**stars**、strength、dexterity、weak、vulnerable、poison、discard_random_hand、exhaust_random_hand、return_random_discard。

这是可执行结构的全集，不是每个角色的生成权限。新生成的中毒/星按角色、棱彩宝石和原生跨角色卡池开放，生成结果也检查权限；保存的定义不追溯限制。范围规则见 [LLM_PROTOCOL.md](LLM_PROTOCOL.md)，机制差距见 [MECHANIC_COVERAGE.md](MECHANIC_COVERAGE.md)。

新增 `self_stars` 缩放，读取原生费用支付后、每次执行时的剩余星数；获得星使用 PlayerCmd.GainStars，星费用使用原生 CanonicalStarCost/扣费流程，可升级并通过原生存档、复制和降级恢复。

## 新效果字段

| 字段 | v4 语义 / 缺省 |
| --- | --- |
| `kind/target/amount` | 必填；amount≥1；目标 self / enemy / all_enemies / random_enemy |
| `upgrade_amount` | 非负整数，缺省0 |
| `trigger` | 与旧v2触发种类相同，缺省on_play |
| `duration` | 非负整数，缺省1；0仅能力牌可用，表示整场；不再限制最多3回合 |
| `max_per_turn` | 事件填0表示无限；显式正整数可表示特定额度，无1～3上限。为兼容旧牌缺省仍1；非事件必须为1 |
| `repeat` | 正整数，缺省1；不再限制最多3次 |
| `condition` | none / self_has_block / self_hp_below_half / target_weak / target_vulnerable；缺省none |
| `scaling` | none / self_block / hand_size / discard_size / exhaust_size / target_poison / self_stars；缺省none |
| `scaling_amount` | 缩放时≥1，未缩放时0；缺省0，无1～3上限 |

v4 删除 `scaling_cap`，即使填写 0 也会拒绝；缩放始终按实际状态单位计算。旧 v3 的 0 表示无限，正整数上限继续保留；v1/v2 仍使用原来的必填缩放上限。旧定义的序列化形状和候选指纹保持兼容，不会改写已经生成的「残章缀星」。

取消 v3 效果数值和升级增量固定上限、强度预算打分、零费抽牌/回能强制消耗规则。校验仍保留可执行结构、目标关系、未知枚举/字段拒绝、整数及加法范围；1～8效果和关键词限制属于协议结构。原生引擎的显示/数值边界、手牌容量和牌堆规则继续生效。

事件不限次数不会产生“最多X次”的文字，无上限缩放不会显示“最多计X”。duration=1 显示“本回合”；能力牌整场持续省略“本场战斗中”，有限持续期仍显示。出牌触发省略“另一张”。星和能量使用原生图标，小数量（1～3）重复图标，其他数量使用数字＋图标；能量颜色来自所属角色。

即时伤害通过原生攻击命令执行，受力量、虚弱、目标易伤等攻击修正影响；回合及事件触发伤害使用 `ValueProp.Unpowered` 的原生伤害命令，与滚石、黑洞、冰雹风暴一致，不受这些攻击修正影响。

手牌预览先计算星费用和先前即时效果带来的星、格挡及牌堆数量变化，再计算 scale，最后使用原生伤害/格挡钩子计入力量、虚弱、目标易伤、敏捷、脆弱和附魔。基础 DynamicVar 不被预览改写。选择目标后才计入该目标状态；未来触发、随机抽牌引发的事件链和先前效果新增的能力/敌人 debuff 无法完整预测。缩放数值显示已计入的来源，避免把公式误读成额外增量。

升级说明临时恢复变化槽位的原生升级高亮，兼容升级完成后清除标记的查看流程；零升级增量不应标绿。小数量资源的绿色包围图标，较大数量的绿色包围数字。

触发时机、敌方目标、持续期、随机牌堆操作和独立能力实例规则与旧v2一致。内部实例重入和8层跨能力事件深度保护用于避免程序递归卡死，不是对玩家正常打牌次数的限制。

示例：`examples/star-cards.json`（星费用/剩余星成长、攻击后获得星、即时获得星）。

---

# 旧协议 v1/v2（存档兼容）

旧生成内容的 `schema_version: 2` 保持原有执行语义。以下数值上限和强度预算仅适用于 v1/v2；共同的触发、目标与生命周期规则也适用于 v3/v4。

## 结构

卡牌保留 `name/type/rarity/cost/keywords/effects/flavor`。`type` 增加 `power`，`effects` 最多 8 条。每条效果是一个独立组件，字段如下：

| 字段 | 语义及限制 | 缺省 |
| --- | --- | --- |
| `kind` | 原有 9 种命令，加 `discard_random_hand`、`exhaust_random_hand`、`return_random_discard` | 必填 |
| `target` | `self/enemy/all_enemies/random_enemy` | 必填 |
| `amount` | 基础数值，至少 1 | 必填 |
| `upgrade_amount` | 升级增加基础数值 | 0 |
| `trigger` | 见下表 | `on_play` |
| `duration` | 1–3；0 为本场战斗持续，仅能力牌可用 | 1 |
| `max_per_turn` | 事件组件每个自己的回合区间最多响应 1–3 次；其他组件必须为 1 | 1 |
| `repeat` | 每次执行重复 1–3 次，伤害逐次经过原生伤害流程 | 1 |
| `condition` | `none/self_has_block/self_hp_below_half/target_weak/target_vulnerable` | `none` |
| `scaling` | `none/self_block/hand_size/discard_size/exhaust_size/target_poison` | `none` |
| `scaling_amount` | 每单位增加 1–3；无缩放必须为 0 | 0 |
| `scaling_cap` | 最多计入 1–5 单位；无缩放必须为 0 | 0 |

实际数值为 `基础数值 + 升级增量 + scaling_amount × clamp(当前状态单位数, 0, scaling_cap)`。状态在每次重复执行时读取；敌人条件和中毒缩放按每个敌人读取。低生命条件为严格低于最大生命的一半。条件失败仍消耗该事件的响应次数，持续时间照常减少；描述会注明这一点。

伤害、虚弱、易伤、中毒只指向敌人；其他效果只指向自己。`enemy` 是出牌时选择的敌人，只能配 `on_play`。延迟/事件敌方效果用全部或随机敌人，避免存储已死亡/已离场目标。随机目标每次重复重新抽取，使用游戏的 `CombatTargets` RNG；随机牌使用 `CombatCardSelection` RNG。随机手牌操作排除当前正在执行的牌；弃牌回收在手牌满时停止。

## 生命周期

| `trigger` | 执行时点与持续时间 |
| --- | --- |
| `on_play` | 立即按数组顺序执行；`duration/max_per_turn` 必须为 1 |
| `next_turn_start` | 下一个自己的回合开始，执行一次；`duration` 必须为 1 |
| `turn_start` | 接下来 `duration` 个自己的回合开始；0 表示之后每个自己的回合 |
| `turn_end` | 本回合及之后 `duration-1` 个自己的回合结束；执行于手牌清理前 |
| `card_played/attack_played/skill_played` | 自己打出后续牌/攻击/技能后；忽略施放来源牌的这次出牌 |
| `card_drawn` | 自己抽到一张牌后，包括每回合正常抽牌 |
| `card_exhausted` | 自己消耗一张牌后，包括虚无导致的消耗 |

事件类有限持续期包含施放回合，随后每个自己的回合结束减少一次。起始触发组件不在施放回合结束时减少，而在未来回合开始响应后减少。事件额度在自己的 `BeforeSideTurnStart` 重置。起始效果用带选择上下文的 `AfterPlayerTurnStart`，可安全抽牌并经过其他 Mod 的选择钩子。结束时效果用 `BeforeSideTurnEnd`；事件持续期在 `AfterSideTurnEndLate` 才到期，使手牌清理期间的虚无消耗仍可触发。

每次出牌先完成全部即时组件，再应用一个独立的 `GeneratedEffectPower`。延迟组件按原数组相对顺序响应各自事件，不会在施放时读取并锁定缩放状态。能力牌必须含至少一个延迟/事件组件；不能同时有消耗或保留。攻击必须含伤害，即时伤害必须属于攻击；技能/能力可通过延迟或事件造成伤害。

每个能力实例在异步执行前消耗响应额度。正在执行的实例不会被其产生的嵌套事件重新进入；生成能力之间的事件链还受 8 层深度上限约束。因此“抽牌时抽牌”“消耗时消耗”的组件可存在，但不会无限自触发。同一个实例的其他组件也不会响应其本次执行产生的嵌套事件。其他实例可以响应，直到各自额度或深度限制耗尽。外部原生能力仍通过游戏钩子执行。

## 校验与强度

最多 3 个不重复的原有关键词；费用为 0–5。最大升级/缩放后单次数值与升级增量上限如下（v1/v2 均采用这些放宽后的数值边界）：

| 效果 | 最大升级/缩放后数值 | 最大升级增量 |
| --- | --- | --- |
| 伤害 | 80 | 15 |
| 格挡 | 60 | 12 |
| 抽牌 | 6 | 3 |
| 能量 | 4 | 2 |
| 力量 / 敏捷 | 8 | 3 |
| 虚弱 / 易伤 | 6 | 3 |
| 中毒 | 24 | 6 |
| 牌堆操作 | 5 | 2 |

强度为各组件 `最大升级/缩放数值 × 权重 × 目标倍率 × repeat × 预计响应次数` 的总和。权重：伤害/格挡/随机弃牌 1、抽牌/弃牌回收 5、能量 8、力量/敏捷 6、虚弱/易伤/随机消耗手牌 3、中毒 2。全体目标倍率 1.8，其他 1。即时次数 1；延迟次数为持续回合数乘事件额度；战斗持续组件按 6 回合估值。条件不打折，弃牌等副作用也不给预算补偿。

预算上限为 `10 + 15×费用 + 稀有度加成(0/4/8) + 消耗加成10`。高费牌可以承载较大的单次数值，低费牌仍受预算限制；例如 4 费伤害 52、升级增加 11 可以通过，1 费同样的效果会被拒绝。重复、全体与持续触发仍乘入总预算。零费抽牌、能量或弃牌回收必须消耗。预算是有限估计，持续能力在长战斗中的价值及牌间协同仍需实机调参。

未知字段、未列出的 opcode/目标/触发/条件/缩放，以及非法槽位组合全部拒绝。LLM 不返回可执行代码或独立机械描述；中英文描述由 `CardText` 投影结构，数值显示继续使用原生 DynamicVar。

## 存档兼容

旧 `schema_version: 1` 卡牌继续接受，缺失的新字段解释为即时、单次、无条件、无缩放。v1 不允许携带 v2 专属语义，旧枚举成员没有重排。奖励 sidecar 和牌的 `DefinitionPayload` 原生 SavedProperty 保存完整定义，升级/复制/降级继续保留每张牌的结构。

`GeneratedEffectPower.RuntimePayload` 是独立版本化的原生 SavedProperty，包含完整定义、施放时 DynamicVar 基础数值快照、来源升级状态、每条效果剩余期数/响应次数以及来源出牌忽略标记。原生 `SavedProperties` 可往返该数据；克隆深拷贝可变状态。具体运行存档是否保存当前战斗能力、在哪个检查点恢复，仍由游戏控制；本 Mod 不扩展原生检查点语义。

## 示例与扩展

见 `examples/complex-cards.json`：多次命中并按消耗堆缩放、下回合抽牌/回能、消耗触发格挡的战斗能力、两回合内每次技能触发随机伤害、随机消耗后按消耗堆获得格挡并回收弃牌。旧 `examples/cards.json` 保留用于 v1 回归。

Mock 接口增加 `--fixture examples/complex-cards.json --card-index 1` 可选择特定组件案例，无需真实 LLM。CLI `validate` 检查整份示例，`generate` 仍经过相同协议和校验边界。

当前未开放任意递归效果树、X 费、自动重放、玩家选择/保留选择载荷、自定义 token/orb、卡费修改、持续规则修改或外部 opcode。扩展一条路由必须同时增加合同、校验、原生命令、文本/悬浮提示、生命周期测试及游戏 API 检查。
