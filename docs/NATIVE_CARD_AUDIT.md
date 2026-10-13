# 原生卡牌逆向映射与文本对照

`audit-native.ps1` 读取安装游戏的原生卡牌模型和 PCK 本地化，输出“原生卡 → 协议 JSON → 协议文本”的审计报告。它不调用 LLM、不启动游戏、不修改游戏文件或存档。需要 .NET 9 SDK 和已核对的 Windows 游戏 v0.111.0（commit `41cef1ea`）。

```powershell
# 全量审计，默认写入 build/native-audit/
.\audit-native.ps1 -GameDir 'C:\Games\Slay the Spire 2'
# 单张牌，按原生 ID 或 C# 类名筛选；建议指定另一个输出目录
.\audit-native.ps1 -GameDir 'C:\Games\Slay the Spire 2' -Card Armaments -OutputDir build/armaments-audit
# 英文对照；SDK 不在 PATH 时可用 -DotnetExe
.\audit-native.ps1 -GameDir 'C:\Games\Slay the Spire 2' -Language eng -OutputDir build/native-audit-eng
```

也可以设置 `STS2_GAME_DIR` 后省略 `-GameDir`。独立程序位于 `src/Forge.NativeAudit/`，不增加普通 Forge.Tool 或 Core 测试对游戏的依赖。

## 输出与分类

| 文件 | 内容 |
| --- | --- |
| `report.md` | 分类数量、基础／升级原文与协议文本、费用、差异原因、协议 JSON，以及未映射清单 |
| `audit.json` | 每张原生卡的 ID、类名、卡池、类型、稀有度、费用、变量、标签、关键词、原文、转换状态与定义；另有语言、协议版本和程序集指纹 |
| `cards.json` | 只包含 `effects_mapped` 的协议定义，可直接交给 Forge.Tool validate/audit-text |
| `partial-cards.json` | 已知存在差异的候选协议定义，适合文本检查，不应当作原生行为的完整替代 |

分类以原生模型为单位，不按卡名去重；五职业的打击／防御是不同模型。全量包含衍生牌、联机牌、状态／诅咒和废弃占位模型，不能把总数当作单人奖励卡数。

- `effects_mapped`：已核对效果实现，可以组合当前协议动作。它不证明整张牌在任意游戏状态下完全等价。
- `partial`：可以生成并通过校验的候选定义，但存在明确差异，如稀有度、标签、事件阶段、叠层方式或 X 修正。
- `unsupported`：已确认协议或本项目单人范围缺口，不输出残缺定义。
- `unreviewed`：尚未核对 C# 行为，没有结论；不能将其计入“不支持”。
- `error`：快照或已登记映射失败，例如预期动态变量不存在；审计返回失败退出码。

原生文本无法格式化时，`text_error` 单独保留。已输出定义的牌出现文本错误，也返回失败退出码；其他模型的文本错误不会伪装成协议缺口。模型本身不可升级时，第二份快照重复基础形态并明确标注。

## 映射依据与边界

原生效果是任意 C# 程序，不存在通用反射函数能把它无损转换成协议。`Forge.Core/NativeCardConversion.cs` 使用人工核对过的 `OnPlay`、`OnUpgrade`、生命周期钩子及相关 Power 实现建立白名单映射；反射只负责读取模型状态，不从描述或变量名字猜效果。

基础／升级分别从真实可变模型读取；升级实际执行原生 `UpgradeInternal` 和 `FinalizeUpgradeInternal`。费用读取 `CostModifiers.None`，保留原生升级后的基础费用，而非误用不随升级改变的 `EnergyCost.Canonical`。规则、关键词、动作顺序与升级选择方式分别建模；例如武装升级后选择所有手牌，坚毅从随机消耗变为自选消耗。

映射固定绑定游戏程序集 SHA-256；不同构建在加载游戏类型前拒绝转换，必须核对效果后更新指纹。该保护不意味着新版本与旧版本必然不兼容，而是避免沿用过时白名单产生看似准确的报告。PCK 本地化每次从安装目录读取，保留实际安装中的措辞。

初版在此构建扫描 596 个模型：25 个 `effects_mapped`、24 个 `partial`、84 个 `unsupported`、463 个 `unreviewed`。84 个已确认范围／表达缺口中，37 个为联机专属、40 个为状态／诅咒类型，另 7 个有已核对的专用行为缺口。该统计是映射进度，不是协议最终覆盖率。

已发现的代表差异：

| 原生卡／机制 | 当前结论 |
| --- | --- |
| 打击／防御 | 即时伤害／格挡可表示；Basic 稀有度和 Strike/Defend 标签缺失，影响其他牌和遗物识别 |
| 旋风斩／双重打击 | `repeat` 是多次独立攻击，原版是一次多段攻击；旋风斩还使用修正后的 X 值，不只是实付能量 |
| 余像 | 出牌前快照层数、出牌后按快照结算；当前只有出牌后事件 |
| 黑暗 | 需要按 DarkOrb 类型筛选球的被动触发 |
| 无尽刀刃／恶魔形态 | 可以写规则候选，但原生与协议的回合开始阶段不同；原生叠层和独立规则实例的结算也不同 |
| 黑暗之拥 | 虚无消耗引起的抽牌延迟到回合末弃牌之后；协议不能保留原因和延迟 |
| 机器学习 | 修改回合抽牌数量，不能用额外 draw 无损替代 |
| 小刀 | 协议能生成原生小刀；完整定义小刀还需要 Shiv 标签及 FanOfKnivesPower 的目标变化 |
| 吞噬 | 斩杀与最大生命奖励可写入 `on_fatal`；原生禁止战斗生成的元数据不能保留 |

所有重建定义仍使用生成牌自身的 ID、Token 卡池和占位视觉。原生身份判断、原生能力类识别、附魔、运行时数值改造和所有钩子组合不是本工具已经证明的等价范围。审计没有改变协议或保存格式，也没有为了覆盖率添加以单张原生牌命名的执行字段。

## 文本对照与验证

原文使用游戏本地化格式化器和牌库状态；协议正文使用共享 `CardText`，关键词使用游戏的本地化及原生展示顺序。两侧只去掉样式、把能量／星图标变为 ⚡／⭐，比较时忽略空白，不抹掉标点或重写语句。报告保留不同措辞以供人工判断：文本不同可能只是详略、单复数或词序，文本相同也不能证明事件时序和效果相同。

```powershell
dotnet run --project src/Forge.Tool -- validate build/native-audit/cards.json
dotnet run --project src/Forge.Tool -- validate build/native-audit/partial-cards.json
dotnet run --project src/Forge.Tool -- audit-text build/native-audit/cards.json build/native-audit/text.md
dotnet run --project tests/Forge.Tests -c Release
$env:STS2_GAME_DIR = 'C:\Games\Slay the Spire 2'
dotnet run --project tests/Forge.GameSmoke -c Release
```

Core 回归覆盖未知／缺口／错误分类、动作顺序、机制升级、标签／稀有度损失、X／多段攻击差异、动态表达式、斩杀和临时集中。GameSmoke 从实际游戏模型核对数值、升级费用、关键词和选择，再加载协议定义为生成卡并检查升级形态。两者尚未构成真实战斗中的行为差分测试；实机执行仍按 [VALIDATION.md](VALIDATION.md) 验收。

扩展时先核对类实现和相关能力／钩子，再添加映射及原生回归；无法表达的语义保留原因。优先用现有动作组合，只有确认属于可复用能力缺口后才考虑扩展协议。
