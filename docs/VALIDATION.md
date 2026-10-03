# 验证指南

目标游戏版本为 `v0.111.0`。核心测试无需游戏或外部服务；GameSmoke 读取本机游戏程序集，不启动 Godot，也不修改游戏存档。两者均为控制台测试程序，以退出码和本次输出为准。

## 自动检查

```powershell
dotnet run --project tests/Forge.Tests -c Release
$env:STS2_GAME_DIR = 'C:\Games\Slay the Spire 2'
dotnet run --project tests/Forge.GameSmoke -c Release
# 或运行完整测试、编译与打包：
.\build.ps1 -GameDir $env:STS2_GAME_DIR
```

核心测试覆盖配置、协议校验、旧版兼容、提示词裁剪、请求形状、失败与超时、凭据脱敏、触发生命周期、候选池去重与容量、稳定奖励、取消和持久化回滚。GameSmoke 覆盖原生卡牌/能力的复制、升级、降级、保存恢复、星费用、白银熔炉奖励升级及 Harmony 挂钩契约。

这些检查不能证明实际 UI 布局、卡牌平衡、游戏中的效果执行或任意战斗检查点恢复正确。不要把历史测试数量当作当前验证结果。

## 本地接口联调

在一个终端运行固定响应服务：

```powershell
python tools/mock_provider.py --port 8000
```

另一个终端使用仓库示例配置：

```powershell
dotnet run --project src/Forge.Tool -- generate config.example.json examples/context.json generated.local.json
dotnet run --project src/Forge.Tool -- validate generated.local.json
dotnet run --project src/Forge.Tool -- prompt config.example.json examples/context.json prompt.local.json
```

`generate` 会实际调用本地 HTTP 服务，并默认写入 `generated.local.json.response.local.json` 响应诊断；`prompt` 完全离线。可用 `--fixture examples/complex-cards.json --card-index N`（N 为 0–4）或 `--fixture examples/star-cards.json --card-index N`（N 为 0–2）切换模拟卡牌。星机制观测示例为 `examples/regent-context.json`。

所有卡牌夹具均可直接交给 `validate`：`cards.json`、`complex-cards.json`、`high-cost-cards.json`、`star-cards.json`。模拟接口只验证传输与协议，不代表真实模型的生成质量。

## 实机验收

使用测试存档，记录游戏/Mod 版本、配置及复现步骤；分享材料前检查凭据与日志内容。

1. **生成时机**：敌方首回合结束后启动请求，包括完全格挡、敌方不攻击；第一轮获胜也应提交最终摘要。检查每场预算和请求间隔。
2. **奖励稳定**：可用候选追加到原版选项；慢请求或失败时默认模式立即给出原版奖励。晚到结果不改变已打开界面，可供之后战斗使用。
3. **候选池与读档**：重新打开、跳过、重掷、保存/加载奖励，确认无重复且选项固定，包括空奖励。重开同种子新局不得沿用上一局候选。
4. **生命周期**：死亡、放弃、退出和重新加载局时取消旧请求；结果不能进入新局。检查 `wait_on_reward` 和其他修改奖励的 Mod 共存。
5. **即时效果**：实际打出攻击/技能，核对力量、虚弱、易伤、格挡、全体伤害、抽牌、能量、中毒及关键词。
6. **触发效果**：逐一使用复杂示例，核对条件、多次命中、缩放、未来回合效果、随机牌堆操作、事件额度及多个独立能力实例。虚无消耗应发生在有限事件效果到期之前；递归触发不得卡死。
7. **星资源**：储君获得/支付星、按剩余星缩放，以及能量/星费用升级；核对费用图标、文本和原生扣费。
8. **保存与显示**：升级、复制、保存/加载及降级后定义与数值一致。核对卡牌/能力描述换行、占位图标、条件和动态数值；零升级增量不应误标绿色。战斗中恢复范围以原生检查点为准。
9. **白银熔炉**：前三次奖励的生成牌随原版候选升级，不额外消耗次数；后续原版随机升级不能强制生成牌升级。检查读档和重掷。
10. **日志与服务**：关闭战斗、提示词、reasoning 记录后核对开关；保留生成审计时应仍有请求结果和取消诊断。真实服务的参数支持、耗时和卡牌有效率需单独验证。
11. **多人**：确认跳过生成，保留原版奖励。

## 发布前检查

- 查看 `git status` 与待提交差异，确认 `config.example.json` 无凭据。
- 检查当前文件及 Git 历史中的凭据；`.gitignore` 不会移除已跟踪文件或历史内容。
- 确认包中只有 Mod 文件、无凭据的示例配置和公开文档/示例，不含 `config.json`、`data`、日志或游戏程序集。
- 发布说明列明实际执行的检查、目标游戏版本及未完成的实机项目。
