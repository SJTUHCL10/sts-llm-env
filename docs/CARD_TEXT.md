# 卡牌效果文本检查

描述统一从协议程序生成，入口是 `Forge.Core/CardText.cs`。LLM 只负责定义与风味文字；修正文案应修改共用模板，不能修改日志或让模型覆写效果说明。卡面、升级预览和持续效果状态说明共用资源图标与规则模板。

## 批量核对

```powershell
# 检查一局所有候选和奖励的基础／升级形态：
dotnet run --project src/Forge.Tool -- audit-text '<Mod目录>\data\runs\<run-key>.json' '.dotnet\card-text-run.md'
# 也接受单个 cards.json、generation/combats 日志文件或日志目录：
dotnet run --project src/Forge.Tool -- audit-text '<Mod目录>\data\generation' '.dotnet\card-text-logs.md'
# 每次改协议或模板，都核对仓库示例：
dotnet run --project src/Forge.Tool -- audit-text examples '.dotnet\card-text-examples.md'
```

导出只读取定义对象，按完整定义去重，不导出提示词、reasoning、配置或风味文字。报告中的 ⚡ 代表职业费用图标，⭐ 代表星图标；游戏中使用实际图片。遇到不兼容定义或不完整 JSONL 行会计数并返回非零退出码，仍保留可读取的报告。旧协议不会被自动改写。报告是文本检查工具，不能证明实际渲染与效果执行正确。

## 模板核对表

基准为本机游戏 v0.111.0 原生模型及简体中文本地化。重点参考 `BladeDance`、`InfiniteBlades`、`Discovery`、`Feral`、`Afterimage`、`ChildOfTheStars`，以及原生 `EnergyIconsFormatter`。

| 协议动作／结构 | 检查要求 |
| --- | --- |
| damage / block / draw / heal / lose_hp | 默认省略“你”；伤害仍明确随机、全体与奥斯提；数值与量词之间不加空格 |
| gain_energy / gain_stars / set_cost / filter.cost | 用图标；0 保留数值，1–3 直接拼接图标，4 及以上使用数值加单图标；费用图标跟随职业 |
| 数值表达式与条件 | 资源引用、资源比较也用图标；保留算式、向下取整、条件和重复次数，不为了简短而改变含义 |
| create_card / copy / move | 使用“添加到你的手牌”等原版措辞；明确张数、牌堆、顶部／底部／随机位置和复制品 |
| select / discard / exhaust / upgrade / transform / play | 保留选择或随机、至多、全部、筛选条件及自动打出；选择来源需要明确选项数与执行次数 |
| add_keyword / remove_keyword / apply_power | 关键词与状态名称统一；区分点／层、目标、临时持续时间和移除全部状态 |
| summon / forge / channel / evoke / orb_passive / orb_slots | 保留召唤、铸造、球种、球数量、激发目标与“不移除”；球栏位用获得/失去描述已知正负数量；符号未知的表达式注明负数表示失去 |
| 卡牌事件与回合事件 | 使用“每当你打出一张……”与“在你的回合开始时”；区别首次、前 N 次、第 N 次、每 N 张以及成功生效额度 |
| 规则生命周期 | 能力牌的整场战斗效果省略“本场战斗”；技能／攻击牌保留；本回合、下回合、有限回合以及战斗范围计数不能省略 |

复杂算式和选择绑定属于生成牌独有能力，不一定有对应的原版短句。保持语义可核对，必要时再为常见组合补专门模板。

## 回归验证

核心测试检查生成到手牌／抽牌堆、零费筛选、图标数量与升级高亮、资源条件、卡池词序、选择次数、有限回合以及清空状态的描述。

GameSmoke 检查五个职业的实际图标路径和基础／升级／能力状态文本；持续效果使用保存时捕获的固定数值。格挡检查调用生成效果执行器，并保留原生 `Hook.ModifyBlock` 与 `Creature.GainBlockInternal`，只跳过视听工作和无敌人测试场景的结束判断：直接格挡使用 `Move`，持续触发格挡使用 `Unpowered` 且无直接出牌来源。升级与读档也必须保持这些属性。此规则同样适用于技能／攻击牌安装的持续效果。

最后按 `VALIDATION.md` 实机检查手牌、奖励、升级预览与能力图标悬浮说明，分别使用敏捷、负敏捷及脆弱；检查 0/1/2/3/4 费用和星图标、长文本换行与存档恢复。协议升级时应同时跑核心测试、GameSmoke 和上述两份批量文本报告。
