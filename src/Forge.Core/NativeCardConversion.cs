namespace Forge.Core;

// Audit inputs are separate from the executable/saved-card wire schema.
public sealed record NativeCardForm
{
    public required CardCost Cost { get; init; }
    public required Dictionary<string, decimal> Variables { get; init; }
    public string[] Keywords { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public string Target { get; init; } = "Self";
    public string? Description { get; init; }
    public string? TextError { get; init; }
}

public sealed record NativeCardSnapshot
{
    public required string Id { get; init; }
    public required string ClassName { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Rarity { get; init; }
    public required NativeCardForm[] Forms { get; init; }
    public string? Pool { get; init; }
    public bool MultiplayerOnly { get; init; }
    public bool Upgradable { get; init; } = true;
}

public enum NativeConversionStatus { EffectsMapped, Partial, Unsupported, Unreviewed, Error }
public sealed record NativeConversion(NativeConversionStatus Status, CardDefinition? Definition, string[] Reasons);

public static class NativeCardConversion
{
    public const string GameVersion = "v0.111.0 / 41cef1ea";
    // Reviewed OnPlay/OnUpgrade and relevant powers against this exact assembly. Fail closed after updates.
    public const string AssemblySha256 = "0861BFA1DF347538D932F22D580E75420F08082792EB914E53B4882764ACDBE9";

    public static NativeConversion Convert(NativeCardSnapshot card)
    {
        if (card.MultiplayerOnly) return Unavailable("联机专属行为不在当前单人协议范围内。");
        if (card.Type is not ("Attack" or "Skill" or "Power"))
            return Unavailable("协议的卡牌类型只有 attack/skill/power，不能定义原生状态牌或诅咒牌。");
        if (Gaps.TryGetValue(card.ClassName, out string? gap)) return Unavailable(gap);
        if (card.Forms.Length != 2) return new(NativeConversionStatus.Error, null, ["需要基础和升级两个原生快照。"]);
        var reasons = new List<string>();
        if (!Enum.TryParse(card.Rarity, out ForgeRarity rarity))
        {
            rarity = ForgeRarity.Common;
            reasons.Add($"原生稀有度 {card.Rarity} 不能表达；候选定义暂用 common，相关筛选与遗物交互不等价。");
        }
        var forms = new List<CardForm>();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var native = card.Forms[i];
                decimal V(string key) => native.Variables.TryGetValue(key, out decimal value) ? value
                    : throw new FormatException($"缺少原生数值 {key}。");
                CardEffect Damage(string key = "Damage", string target = "enemy") => new() { Kind = EffectKind.Damage, Target = target, Amount = V(key) };
                CardEffect Block() => new() { Kind = EffectKind.Block, Amount = V("Block") };
                CardEffect Draw() => new() { Kind = EffectKind.Draw, Amount = V("Cards") };
                CardEffect Power(string id, string key, string target = "self") => new() { Kind = EffectKind.ApplyPower, Power = id, Target = target, Amount = V(key) };
                CardEffect Channel(string orb, decimal count = 1) => new() { Kind = EffectKind.Channel, Orb = orb, Amount = count };
                CardEffect Select(EffectKind kind, SelectionMode pick, decimal? count = 1) => new()
                {
                    Kind = kind, Target = new() { Pile = CardPileName.Hand, Pick = pick, Count = count is decimal n ? (NumberExpression)n : null }
                };
                CardEffect Shivs() => new() { Kind = EffectKind.CreateCard, Card = new() { Id = "shiv" }, Count = V("Cards"), To = CardPileName.Hand };
                CardEffect[]? effects = card.ClassName switch
                {
                    "StrikeIronclad" or "StrikeSilent" or "StrikeDefect" or "StrikeNecrobinder" or "StrikeRegent" or "Defile" => [Damage()],
                    "DefendIronclad" or "DefendSilent" or "DefendDefect" or "DefendNecrobinder" or "DefendRegent" => [Block()],
                    "Bash" => [Damage(), Power("vulnerable", "VulnerablePower", "enemy")],
                    "PommelStrike" => [Damage(), Draw()],
                    "TwinStrike" => [Damage() with { Repeat = 2 }],
                    "IronWave" => [Block(), Damage()],
                    "ShrugItOff" or "Backflip" => [Block(), Draw()],
                    "Armaments" => [Block(), Select(EffectKind.Upgrade, i == 0 ? SelectionMode.Choose : SelectionMode.All, i == 0 ? 1 : null)],
                    "TrueGrit" => [Block(), Select(EffectKind.Exhaust, i == 0 ? SelectionMode.Random : SelectionMode.Choose)],
                    "BodySlam" => [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = new() { Stat = "block", Of = "self" } }],
                    "Feed" => [Damage() with { OnFatal = [new() { Kind = EffectKind.GainMaxHp, Amount = V("MaxHp") }] }],
                    "Inflame" => [Power("strength", "StrengthPower")],
                    "DeadlyPoison" => [Power("poison", "PoisonPower", "enemy")],
                    "BouncingFlask" => [Power("poison", "PoisonPower", "random_enemy") with { Repeat = V("Repeat") }],
                    "BladeDance" => [Shivs()],
                    "CloakAndDagger" => [Block(), Shivs()],
                    "Acrobatics" => [Draw(), Select(EffectKind.Discard, SelectionMode.Choose)],
                    "Prepared" => [Draw(), Select(EffectKind.Discard, SelectionMode.Choose, V("Cards"))],
                    "Zap" => [Channel("lightning")],
                    "Dualcast" => [new() { Kind = EffectKind.Evoke, Target = "first_orb", Remove = false }, new() { Kind = EffectKind.Evoke, Target = "first_orb" }],
                    "ColdSnap" => [Damage(), Channel("frost")],
                    "BallLightning" => [Damage(), Channel("lightning")],
                    "Coolheaded" => [Channel("frost"), Draw()],
                    "Glacier" => [Block(), Channel("frost", 2)],
                    "Chaos" => [Channel("random", V("Repeat"))],
                    "Hotfix" => [Power("focus", "FocusPower") with { Until = LifetimeKind.Turn }],
                    "Hyperbeam" => [Damage(target: "all_enemies"), Power("focus", "FocusPower") with { Amount = -V("FocusPower"), Until = LifetimeKind.Turn }],
                    "Venerate" or "RoyalGamble" => [new() { Kind = EffectKind.GainStars, Amount = V("Stars") }],
                    "Soul" => [Draw()],
                    "Afterlife" => [new() { Kind = EffectKind.Summon, Amount = V("Summon") }],
                    "BigBang" => [Draw(), new() { Kind = EffectKind.GainStars, Amount = V("Stars") },
                        new() { Kind = EffectKind.GainEnergy, Amount = V("Energy") }, new() { Kind = EffectKind.Forge, Amount = V("Forge") }],
                    "InfiniteBlades" or "FeelNoPain" or "DemonForm" => [],
                    "Poke" => [Damage("OstyDamage") with { Actor = "osty" }],
                    "Unleash" => [new() { Kind = EffectKind.Damage, Target = "enemy", Actor = "osty", Amount = new()
                    {
                        Add = [V("CalculationBase"), new() { Mul = [V("ExtraDamage"), new() { Stat = "hp", Of = "osty" }] }]
                    } }],
                    "Whirlwind" => [Damage(target: "all_enemies") with { Repeat = new() { Stat = "paid_energy" } }],
                    _ => null
                };
                if (effects is null) return new(NativeConversionStatus.Unreviewed, null, ["尚未核对效果实现；未映射不等于协议无法表达。"]);
                var keywords = new List<ForgeKeyword>();
                foreach (string keyword in native.Keywords)
                {
                    if (!Enum.TryParse(keyword, out ForgeKeyword parsed)) return Unavailable($"协议不能表达原生关键词 {keyword}。");
                    keywords.Add(parsed);
                }
                foreach (string tag in native.Tags.Where(t => t is not ("None" or "OstyAttack")))
                    reasons.Add($"协议不能保留 {tag} 标签，其他牌／遗物按标签识别时不等价。");
                if (native.Target == "RandomEnemy") reasons.Add("随机敌人效果可表达，但生成卡的 UI TargetType 为 Self，原生为 RandomEnemy。");
                CardRule[] rules = card.ClassName switch
                {
                    "InfiniteBlades" => [new() { Trigger = new() { Event = RuleEvent.TurnStart }, Effects =
                        [new() { Kind = EffectKind.CreateCard, Card = new() { Id = "shiv" }, Count = 1, To = CardPileName.Hand }] }],
                    "FeelNoPain" => [new() { Trigger = new() { Event = RuleEvent.CardExhausted }, Effects =
                        [new() { Kind = EffectKind.Block, Amount = V("Power") }] }],
                    "DemonForm" => [new() { Trigger = new() { Event = RuleEvent.TurnStart }, Effects = [Power("strength", "StrengthPower")] }],
                    _ => []
                };
                forms.Add(new() { Cost = native.Cost, Keywords = keywords.ToArray(), Effects = effects, Rules = rules.Length > 0 ? rules : null });
            }
            if (card.ClassName is "TwinStrike" or "Whirlwind")
                reasons.Add("repeat 会创建多次独立攻击，原版 WithHitCount 是一次多段攻击；原生攻击级钩子（如活力消耗）的交互可能不同。");
            if (card.ClassName == "Whirlwind")
                reasons.Add("paid_energy 是实付能量，原版 ResolveEnergyXValue 还应用 X 值修正；有相关修正时不等价。");
            if (card.ClassName == "Feed") reasons.Add("协议未保留 CanBeGeneratedInCombat=false 的原生生成限制。");
            if (card.ClassName is "InfiniteBlades" or "FeelNoPain" or "DemonForm")
                reasons.Add("原生能力叠层后单次结算总量，协议保存独立规则实例；多次打出时结算次数／钩子交互可能不同。");
            if (card.ClassName == "InfiniteBlades") reasons.Add("原生 BeforeHandDraw 生成小刀；协议 turn_start 在 AfterPlayerTurnStart 触发，抽牌前后的时序不等价。");
            if (card.ClassName == "DemonForm") reasons.Add("原生 AfterSideTurnStart 增加力量；协议 turn_start 在 AfterPlayerTurnStart 触发，时序不等价。");
            var definition = CardValidator.Validate(new()
            {
                Name = card.Name, Type = Enum.Parse<ForgeCardType>(card.Type), Rarity = rarity, Forms = forms.ToArray()
            });
            return new(reasons.Count == 0 ? NativeConversionStatus.EffectsMapped : NativeConversionStatus.Partial,
                definition, reasons.Distinct().ToArray());
        }
        catch (FormatException ex) { return new(NativeConversionStatus.Error, null, [ex.Message]); }
    }

    private static NativeConversion Unavailable(string reason) => new(NativeConversionStatus.Unsupported, null, [reason]);
    private static readonly Dictionary<string, string> Gaps = new(StringComparer.Ordinal)
    {
        ["Barricade"] = "协议不支持跨回合保留格挡的原生状态。",
        ["Afterimage"] = "原生余像在 BeforeCardPlayed 快照层数，AfterCardPlayed 按快照结算；协议只有打出后的事件，无法准确保留叠加及自身打出时序。",
        ["Darkness"] = "需要按 DarkOrb 类型筛选球并触发被动；协议只能首球／末球／全部球。",
        ["Flatten"] = "卡进入战斗或奥斯提攻击后自行降费；协议缺少未打出卡的这些监听及奥斯提本回合攻击历史。",
        ["Shiv"] = "原生小刀根据 FanOfKnivesPower 改变攻击目标；协议无法查询此状态，也不能保留 Shiv 标签。可生成原生小刀，但不能完整定义它。",
        ["PerfectedStrike"] = "需要统计带 Strike 标签的所有战斗牌；协议没有该标签筛选及跨牌堆计数。",
        ["DarkEmbrace"] = "原生为虚无消耗延迟抽牌到回合末弃牌流程之后；协议无法区分 causedByEthereal 并保留该延迟结算。",
        ["MachineLearning"] = "原生修改回合抽牌数量，而不是额外执行 draw；协议没有 ModifyHandDraw 拦截点。"
    };
}
