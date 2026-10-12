# LLM 输入与输出

## 请求

系统提示按设计目标、输出结构、动作、数值与条件、规则时机、观察数据组织。模板直接消费当前局的角色机制：费用、关键词、目标、动作、stat、事件和别名目录只列可用项；角色说明融入对应位置，不再用 if enabled 或末尾 Optional 补丁。自定义 system_prompt 与 instructions 仍受配置控制；旧默认 system_prompt 自动使用简化后的默认文字，不改写本地配置。用户消息仅包含：

```text
REQUESTED_COUNT=1
OBSERVATION_JSON:
{...卡牌设计视图...}
```

`ObservationProjector.Project` 将内部完整观测转换成设计视图：

| 输入 | 发给 LLM 的内容 |
| --- | --- |
| 局状态 | 角色、进阶、章节、楼层 |
| 牌组 | 合并相同卡牌后的名称、类型、费用、升级状态、效果说明、必要附魔/负面属性及张数 |
| 遗物 | 名称、说明、有效计数 |
| 生成历史 | name、status、type（attack/skill/power）、rarity（common/uncommon/rare）、基础 cost 与 text；最多 prompt_history_limit 项，默认 100 |
| 当前战斗 | encounter（类型、遭遇 ID、map/event 来源及事件 ID）、轮数、生命/格挡/能力、能量/星、球与球槽、盟友、敌人和意图 |
| 战斗摘要 | 已打出牌计数、伤害/格挡/资源统计、事件计数和结束结果 |

不发送 combat_key、schema_version、种子、运行实例标识、反射 state 树、完整牌堆、逐条原始事件、generated_definition 或重复的首次回合摘要。原生说明去掉显示标签；不把未知状态补成零。没有累计摘要的离线观测仅给出 observed_events 计数，避免冒充完整历史。

combat.encounter.type 使用 monster/elite/boss；source 为 map/event。事件来源读取原生房间栈底的 EventRoom 或 CombatRoom.ParentEventId，因此事件里的精英战仍能同时表达 type:elite 和 source:event；地图问号直接遇怪不冒充事件战斗。旧离线观测仅有 room_type 时只投影已知 type。cards_played.origin 为 native/generated，表示原生卡/模组生成卡，不表示来自永久牌组还是临时生成；该字段经过事件压缩仍保留。summary.complete 在当前战斗摘要封口后为 true，won 为最终胜负，战斗中为 null。

完整内部观测仍用于线程协调、角色可用性、日志和回放；投影不会修改原对象。提示词超过预算时仅缩减可选生成历史；牌组、风格及机制说明无法容纳时报告预算不足，不悄悄删掉设计依据。

history 的 status 有五种：candidate（已校验、等待进入奖励的候选）、shown（已展示、选择待定）、selected（奖励关闭时确认已进入牌组）、skipped（已展示但未选，含选择其他牌或跳过奖励）、expired（候选池容量满时淘汰，未展示）。这些状态描述候选生命周期，不表示生成失败；失败结果不会进入 history。提示词明确要求参考类型与稀有度分布、避免默认生成能力牌，但不强制固定配额。

history 按生成先后顺序取最新 k 张，再按从旧到新输出；k 为 `prompt_history_limit`，默认 100，范围 0–1000。所有状态共用同一窗口，不按状态分配名额；状态变化不会重新排列记录。提示预算不足时继续移除最早记录，实际数量可能小于 k。候选池保存最多 `max(100,k)` 项历史和窗口以外的存活候选，不重复输出同一候选，不更改存档格式。旧配置缺少该字段时使用默认 100；旧版本已经裁掉的历史不会自动恢复。

## 输出

只返回 `{"cards":[...]}`，张数必须等于 REQUESTED_COUNT。每张卡包含 name/type/rarity/forms，可选 flavor；恰好两个完整形态，基础形态和升级形态。结构见 [CARD_PROGRAM.md](CARD_PROGRAM.md)。不要求模型回传请求标识、协议版本或元信息。

传输兼容 Chat Completions 的 OpenAI-compatible 服务，可接受单层 Markdown JSON 围栏。拒绝截断响应、未知字段、无效结构、错误张数及当前角色不允许的机制。整批通过校验后发布；描述由协议渲染，不能携带脚本或任意原生类型名。

亡灵动作说明包含奥斯提的伤害转移机制及专用状态目录。新响应中的奥斯提格挡、无效状态在 `CharacterMechanics.Validate` 被拒绝，覆盖两个形态和规则效果。目录及用途见 [CARD_PROGRAM.md](CARD_PROGRAM.md#奥斯提的生成限制)。

## 客户端元信息与审计

请求修订号、combat_key、协议版本、耗时、候选 ID 等保留在客户端会话和审计中，不进入模型设计数据。generation_request/generation_ready 使用 card_protocol:6；候选池使用自己的 schema_version 和 card_protocol。卡牌的内部实例标识服务于原生保存与规则引用，不由 LLM 生成。

ProviderDiagnostics 仅提取模型 content、reasoning_content、finish_reason、数值 usage 与缓存使用信息；缺少 content 或 reasoning 时记录 null。content 在去除空白、代码围栏及解析卡牌之前记录，截断或无效卡牌 JSON 也可查看。已知凭据和服务地址在 content 与 reasoning 中脱敏；请求头、完整响应信封、任意 HTTP 错误正文不写入日志。提示词和 reasoning 的记录分别遵守配置开关，content 随 record_generation 保存。generation_ready 的 card_texts 按卡牌、形态顺序保存共享 CardText 渲染器的中文效果文本，不改变卡牌定义或存档格式。

## 奖励与候选池

每批有效结果进入本局候选池。执行定义忽略名称/风味后计算指纹；近期已展示机制形状影响选择顺序，其中包括目标和触发事件。奖励冻结与候选移除使用同一原子快照，已展示奖励包括空奖励保持固定。迟到结果进入后续奖励，不修改已打开的奖励界面。

v6 将 Number 改为 decimal，div 改为普通除法；条件比较保留小数。旧候选池按协议版本隔离，不提供迁移，使用新局测试。已有原生保存卡牌可以被读取，但其 div 按当前普通除法语义执行，不承诺恢复旧整数除法行为。
