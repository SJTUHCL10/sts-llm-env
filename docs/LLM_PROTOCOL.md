# LLM 输入与输出

## 请求

请求发往 `/chat/completions`，`messages` 按顺序仅包含一条 `system` 和一条 `user`。`PromptBuilder.Build` 把投影后的观测 JSON 序列化并拼在 `user.content` 的 `OBSERVATION_JSON:` 标记后；它不是独立消息或请求字段。

系统提示按设计目标、输出结构、动作、数值与条件、规则时机、观察数据组织。模板直接消费当前局的角色机制：费用、关键词、目标、动作、stat、事件和别名目录只列可用项；角色说明融入对应位置，不再用 if enabled 或末尾 Optional 补丁。自定义 system_prompt 与 instructions 按配置原样使用，包括曾经的默认文字；不再按旧默认字符串自动替换。用户消息仅包含：

```text
REQUESTED_COUNT=1
OBSERVATION_JSON:
{...卡牌设计视图...}
```

`ObservationProjector.Project` 将内部完整观测转换成设计视图：

| 输入 | 发给 LLM 的内容 |
| --- | --- |
| 局状态 | 角色、进阶、章节、楼层、金币 |
| 当前幕与遭遇规划 | planning：当前幕 ID/名称、已分配 Boss（包括第二 Boss）、未来精英候选池及所属幕；遭遇及可能怪物的 ID/名称、已核对的定性机制；不暴露地图节点与连线 |
| 牌组 | 合并相同卡牌后的 name、type、rarity（原生枚举小写）、协议结构 cost、升级状态、含关键词的 text、必要附魔/负面属性及张数 |
| 遗物 | 名称、说明、有效计数 |
| 生成历史 | name、status、type（attack/skill/power）、rarity（common/uncommon/rare）、基础 cost 与含基础形态关键词的 text；最多 prompt_history_limit 项，默认 100 |
| 当前战斗 | encounter（类型、遭遇 ID、map/event 来源及事件 ID）、轮数、生命/格挡/能力、能量/星、球与球槽、盟友、敌人和意图 |
| 战斗摘要 | 已打出牌计数、伤害/格挡/资源统计、事件计数和结束结果 |

不发送 combat_key、schema_version、种子、运行实例标识、反射 state 树、完整牌堆、逐条原始事件、generated_definition 或重复的首次回合摘要。原生说明去掉显示标签；不把未知状态补成零。没有累计摘要的离线观测仅给出 observed_events 计数，避免冒充完整历史。

deck 与 history 的 cost 均使用 `{energy?,stars?,energy_x?,stars_x?}`；deck 使用原生标准费用，X 标记替代对应固定费用，负值哨兵不作为固定费用发送。未知费用省略。原生 text 沿用含关键词的游戏说明；生成定义回退文本与 history 文本将关键词并入 text，不单列 keywords。

combat.encounter.type 使用 monster/elite/boss；source 为 map/event。事件来源读取原生房间栈底的 EventRoom 或 CombatRoom.ParentEventId，因此事件里的精英战仍能同时表达 type:elite 和 source:event；地图问号直接遇怪不冒充事件战斗。旧离线观测仅有 room_type 时只投影已知 type。cards_played.origin 为 native/generated，表示原生卡/模组生成卡，不表示来自永久牌组还是临时生成；该字段经过事件压缩仍保留。summary.complete 在当前战斗摘要封口后为 true，won 为最终胜负，战斗中为 null。

完整内部观测仍用于线程协调、角色可用性、日志和回放；投影不会修改原对象。提示词超过预算时仅缩减可选生成历史；牌组、风格及机制说明无法容纳时报告预算不足，不悄悄删掉设计依据。

### 机制背景与未来构筑

system 的 `GAME MECHANICS BACKGROUND` 按本局实际开放的机制加入说明：猎人的奇巧触发与回合结束弃牌的区别；储君的星、铸造、君王之剑和三类仆从牌；亡灵契约师的召唤、奥斯提、灵魂和灾厄；机器人的球与集中。跨角色原生卡池和棱彩宝石沿用现有 `CharacterMechanics` 判定；背景说明不会开放额外协议动作。角色能力细节仍见 ACTIONS。规则背景和敌人机制摘要集中在 `GameKnowledge.cs`，依据本机 v0.111.0 原生模型与本地化核对，更新游戏后应复核。

为缓解只详细介绍新机制带来的选型偏向，所有角色并列提供通用机制背景：伤害/格挡、力量/敏捷、虚弱/易伤/脆弱、抽牌/能量、弃牌/消耗/保留，以及生命/金币的取舍；猎人的小刀、毒、弃牌与防御协同同奇巧并列。system 明确说明介绍篇幅不代表推荐权重，传统与新机制地位相同，按牌组/遗物支持、当前需求、成本和历史设计选择，允许简单有效的卡，也不设置机制配额。这是提示词层面的缓解，不能保证真实模型的分布；需比较多次生成的机制分布和实际可用性。

动态信息位于 user 的 `OBSERVATION_JSON.planning`，system 解释其含义并要求平衡即时生存、牌组协同及未来 Boss/精英需求：

- `act_id/act_title`：当前幕，而非只用数字幕序号，兼容替代幕。
- `bosses`：读取 `Act.BossEncounter/SecondBossEncounter` 已分配的遭遇，包括 ID、原生本地化名称与可能怪物。当前房间为 Boss 战时输出空数组 `[]`（包括第二 Boss 战），当前 Boss 由 `combat` 描述；旧/离线观测投影也根据 `combat.encounter.type`（缺失时回退到 `room_type`）清空该列表。其他房间是本幕已分配的 Boss 列表，不表示每项尚未打过，也不预读后续幕。
- `elite_pool`：先从当前节点的子节点沿可达路线计算未来最多还能经过几场精英，不包含当前战斗，也不把互斥分支的场次数相加。有未来精英时，结合本幕已消耗的遭遇和原生抽袋规则，合并这些场次可能遇到的类型；没有时改用下一幕完整精英池，第三幕或没有下一幕则为空。`elite_act_id/elite_act_title` 标明精英池所属幕，独立于当前幕与 Boss；无所属幕时内部为 null，LLM 投影省略这两个字段。地图缺失时保守使用当前幕完整池，不把未知当成没有精英。不提供节点绑定、遭遇顺序或概率。
- v0.111.0 的 `ActModel.GenerateRooms` 从精英池无放回抽取，袋空后重新装袋；原生每幕池为三种，因此前三场不同，第四场开始允许遇到先前类型，后续每袋仍各抽一次。`AddWithoutRepeatingTags` 优先排除与上一场相同或共享标签的候选，无合适候选才回退。原生预生成 15 场队列；采集仅通过 `ToSave().SerializableRooms` 读取 `EliteEncountersVisited` 和已消耗前缀，重建当前袋的剩余候选，不读取尚未消耗的预定顺序。当前地图战斗在进入房间时已由 `RunManager.EnterRoomInternal` 标记消耗，事件内的战斗不额外推进本幕队列。
- 每个遭遇的 `monsters` 来自 `AllPossibleMonsters`，是可能成员目录（包含后续召唤物），不能把目录中的每种怪当成独立遭遇或全部同时出场。已核对的多人遭遇增加 `initial_lineup: [{id,count,role}]`，明确初始阵容：三骑士各 1 只、女王与火炬头各 1 只、园丁 4 只、祭司与信徒 1+2、双蟹各 1 只、蜈蚣三节各 1 只；寄生蛙初始只有本体，死亡后才出现 4 只 Wriggler。未核对的遭遇省略阵容，不从去重后的目录猜测数量。
- 怪物 `initial_hp: {min,max}` 在游戏线程读取原生 `MinInitialHp/MaxInitialHp`，表示当前进阶下、每只怪的初始生命范围（含进阶 8 的加成），没有随机抽取具体血量，也不是当前生命、整个阵容的总生命或复活阶段的累计生命。该范围尚未应用遭遇额外修正。旧观测缺少时不补零或猜测。实验体的机制另列三阶段生命：100/200/300，进阶 8 起为 111/212/313。
- 投影按已知原生 ID 附加 `mechanics`，描述怪物特点、主要意图和构筑需求，省去临场出牌顺序与击杀步骤；只列有设计意义的已核对数量/阈值，不模拟完整行为或 Mod 改造。未知怪物保留 ID/名称并省略机制，不能解读为没有特殊机制。实验体二阶段伤口按 v0.111.0 `PainfulStabsPower` 判定：命中造成未格挡伤害才向弃牌堆塞伤口，完全格挡不会塞牌。
- 不向 LLM 暴露地图或节点信息；采集不再输出 `planning.map`，投影也会移除旧/离线观测中的该字段。地图仅用于上述内部可达性判断，问号不解析隐藏结果。当前节点未确定时从地图起点开始。

所有原生读取和 JSON 脱离仍在游戏线程完成，后台只接收数据。采集不调用 PullNextEncounter、GenerateMonsters 或创建地图，不消耗遭遇或 RNG；GameSmoke 比较读取前后的原生遭遇保存数据与 RNG 状态，覆盖抽袋重置、分支计数、跨幕切换、第三幕空池和保存后恢复。旧/离线观测可缺少 planning 或 Boss，按未知处理。planning 是额外设计依据，会占用提示预算，不通过移除它来悄悄满足字符限制。

history 的 status 有五种：candidate（已校验、等待进入奖励的候选）、shown（已展示、选择待定）、selected（奖励关闭时确认已进入牌组）、skipped（已展示但未选，含选择其他牌或跳过奖励）、expired（候选池容量满时淘汰，未展示）。这些状态描述候选生命周期，不表示生成失败；失败结果不会进入 history。提示词明确要求参考类型与稀有度分布、避免默认生成能力牌，但不强制固定配额。

history 按生成先后顺序取最新 k 张，再按从旧到新输出；k 为 `prompt_history_limit`，默认 100，范围 0–1000。所有状态共用同一窗口，不按状态分配名额；状态变化不会重新排列记录。提示预算不足时继续移除最早记录，实际数量可能小于 k。候选池保存最多 `max(100,k)` 项历史和窗口以外的存活候选，不重复输出同一候选，不更改存档格式。旧配置缺少该字段时使用默认 100；旧版本已经裁掉的历史不会自动恢复。

## 输出

只返回 `{"cards":[...]}`，张数必须等于 REQUESTED_COUNT。每张卡包含 name/type/rarity/forms，可选 flavor；恰好两个完整形态，基础形态和升级形态。结构见 [CARD_PROGRAM.md](CARD_PROGRAM.md)。不要求模型回传请求标识、协议版本或元信息。

传输兼容 Chat Completions 的 OpenAI-compatible 服务，可接受单层 Markdown JSON 围栏。拒绝截断响应、未知字段、无效结构、错误张数及当前角色不允许的机制。整批通过校验后发布；描述由协议渲染，不能携带脚本或任意原生类型名。

亡灵动作说明包含奥斯提的伤害转移机制及专用状态目录。新响应中的奥斯提格挡、无效状态在 `CharacterMechanics.Validate` 被拒绝，覆盖两个形态和规则效果。目录及用途见 [CARD_PROGRAM.md](CARD_PROGRAM.md#奥斯提的生成限制)。

## 客户端元信息与审计

请求修订号、combat_key、协议版本、耗时、候选 ID 等保留在客户端会话和审计中，不进入模型设计数据。generation_request/generation_ready 使用 card_protocol:9；候选池使用自己的 schema_version 和 card_protocol。候选池仅接受当前 v9 协议，旧版本不迁移。卡牌的内部实例标识服务于原生保存与规则引用，不由 LLM 生成。

ProviderDiagnostics 仅提取模型 content、reasoning_content、finish_reason、数值 usage 与缓存使用信息；缺少 content 或 reasoning 时记录 null。content 在去除空白、代码围栏及解析卡牌之前记录，截断或无效卡牌 JSON 也可查看。已知凭据和服务地址在 content 与 reasoning 中脱敏；请求头、完整响应信封、任意 HTTP 错误正文不写入日志。提示词和 reasoning 的记录分别遵守配置开关，content 随 record_generation 保存。generation_ready 的 card_texts 按卡牌、形态顺序保存共享 CardText 渲染器的中文效果文本，不改变卡牌定义或存档格式。

## 奖励与候选池

每批有效结果进入本局候选池。执行定义忽略名称/风味后计算指纹。奖励优先展示当前层、本场战斗生成的候选（按 combat_key 匹配），不足时从旧候选池补齐；两组内部均优先选择近期较少展示的机制形状，其中包括目标和触发事件，同分时先选较早生成的候选。展示数量上限为 generated_cards_per_reward。奖励冻结与候选移除使用同一原子快照，已展示奖励包括空奖励保持固定。迟到结果进入后续奖励，不修改已打开的奖励界面。

v7 增加 gain_max_hp/lose_max_hp、gain_gold/lose_gold、gold 数值与 damage.on_fatal。斩杀组允许全部动作，嵌套动作计入动作预算，沿用原生 Fatal 资格与 DamageResult；最后一击在伤害内部结算收益。详情见 CARD_PROGRAM.md。

v8 增加 block_gained/damage_given/power_amount_changed/forged/stars_gained/stars_spent；trigger.where 区分执行者、来源生物、受影响对象、来源模型类别、攻击性质、回合、能力和球类型，以及抽牌/消耗/连打信息。事件实付资源与安装规则时的资源分开，伤害事件保留单次命中目标，激发球保留完整目标集合，卡牌筛选支持费用区间和原生标签。只有本局开放储君机制时才提示/允许生成星和铸造事件。未知来源保留为空或 unknown；星原生回调无来源模型，付款后收益使用 card_played 的事件实付星数与 last_in_series。新增 where 不可与基于历史的 occurrence 混用，改用 limit 表达生效额度。v9 移除整次攻击事件；新响应、保存定义和规则载荷中的已移除事件均不再接受。auto_play/last_in_series 和事件实付资源仅用于 card_played。

v6 将 Number 改为 decimal，div 改为普通除法；条件比较保留小数。旧候选池按协议版本隔离，不提供迁移，使用新局测试。已有原生保存卡牌可以被读取，但其 div 按当前普通除法语义执行，不承诺恢复旧整数除法行为。
