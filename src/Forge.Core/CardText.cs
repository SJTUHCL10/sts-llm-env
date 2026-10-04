namespace Forge.Core;

// Text is a projection of the validated program, never an execution input.
public static class CardText
{
    public static string Render(CardDefinition card, bool chinese, Func<int, string> amount, bool triggeredOnly = false,
        bool resourceIcons = false, bool scaledPreview = false) =>
        string.Join("\n", card.Effects.Select((effect, index) => (effect, index))
            .Where(item => !triggeredOnly || item.effect.Trigger != EffectTrigger.OnPlay)
            .Select(item => RenderEffect(item.effect, chinese, amount(item.index),
                omitCombatLifetime: card.Type == ForgeCardType.Power, resourceIcons: resourceIcons, scaledPreview: scaledPreview)));

    public static string RenderEffect(CardEffect effect, bool chinese, string amount, bool omitCombatLifetime = false,
        bool resourceIcons = false, bool scaledPreview = false)
    {
        string target = effect.Target switch
        {
            EffectTarget.AllEnemies => chinese ? "所有敌人" : "ALL enemies",
            EffectTarget.RandomEnemy => chinese ? "随机敌人" : "a random enemy",
            _ => chinese ? "" : "the enemy"
        };
        string damageTarget = target.Length == 0 ? "" : $"对{target}";
        string action = chinese ? effect.Kind switch
        {
            EffectKind.Damage => $"{damageTarget}造成 {amount} 点伤害。",
            EffectKind.Block => $"获得 {amount} 点[gold]格挡[/gold]。",
            EffectKind.Draw => $"抽 {amount} 张牌。", EffectKind.Energy => resourceIcons ? $"获得{amount}。" : $"获得 {amount} 点能量。",
            EffectKind.Stars => resourceIcons ? $"获得{amount}。" : $"获得 {amount} 点[gold]星[/gold]。",
            EffectKind.Strength => $"获得 {amount} 点[gold]力量[/gold]。",
            EffectKind.Dexterity => $"获得 {amount} 点[gold]敏捷[/gold]。",
            EffectKind.Weak => $"给予{target} {amount} 层[gold]虚弱[/gold]。",
            EffectKind.Vulnerable => $"给予{target} {amount} 层[gold]易伤[/gold]。",
            EffectKind.Poison => $"给予{target} {amount} 层[gold]中毒[/gold]。",
            EffectKind.DiscardRandomHand => $"随机丢弃 {amount} 张其他手牌。",
            EffectKind.ExhaustRandomHand => $"随机[gold]消耗[/gold] {amount} 张其他手牌。",
            EffectKind.ReturnRandomDiscard => $"将弃牌堆中随机 {amount} 张牌移回手牌。",
            _ => throw new InvalidOperationException()
        } : effect.Kind switch
        {
            EffectKind.Damage => $"Deal {amount} damage to {target}.",
            EffectKind.Block => $"Gain {amount} [gold]Block[/gold].",
            EffectKind.Draw => $"Draw {amount} cards.", EffectKind.Energy => resourceIcons ? $"Gain {amount}." : $"Gain {amount} Energy.",
            EffectKind.Stars => resourceIcons ? $"Gain {amount}." : $"Gain {amount} [gold]Stars[/gold].",
            EffectKind.Strength => $"Gain {amount} [gold]Strength[/gold].",
            EffectKind.Dexterity => $"Gain {amount} [gold]Dexterity[/gold].",
            EffectKind.Weak => $"Apply {amount} [gold]Weak[/gold] to {target}.",
            EffectKind.Vulnerable => $"Apply {amount} [gold]Vulnerable[/gold] to {target}.",
            EffectKind.Poison => $"Apply {amount} [gold]Poison[/gold] to {target}.",
            EffectKind.DiscardRandomHand => $"Discard {amount} random other cards from hand.",
            EffectKind.ExhaustRandomHand => $"[gold]Exhaust[/gold] {amount} random other cards from hand.",
            EffectKind.ReturnRandomDiscard => $"Return {amount} random cards from discard to hand.",
            _ => throw new InvalidOperationException()
        };
        if (effect.Scaling != EffectScaling.None)
        {
            string units = (chinese, effect.Scaling) switch
            {
                (true, EffectScaling.SelfBlock) => "你当前的格挡", (false, EffectScaling.SelfBlock) => "your current Block",
                (true, EffectScaling.HandSize) => "当前手牌数", (false, EffectScaling.HandSize) => "cards in hand",
                (true, EffectScaling.DiscardSize) => "弃牌堆牌数", (false, EffectScaling.DiscardSize) => "cards in discard",
                (true, EffectScaling.ExhaustSize) => "消耗堆牌数", (false, EffectScaling.ExhaustSize) => "cards in exhaust",
                (true, EffectScaling.TargetPoison) => "该敌人的中毒层数", (false, EffectScaling.TargetPoison) => "that enemy's Poison",
                (true, EffectScaling.SelfStars) => "你当前的星数", (false, EffectScaling.SelfStars) => "your current Stars",
                _ => throw new InvalidOperationException()
            };
            string cap = effect.ScalingCap == 0 ? "" : chinese ? $"（最多计 {effect.ScalingCap}）" : $" (count at most {effect.ScalingCap})";
            action += scaledPreview
                ? chinese ? $"（已计入 {effect.ScalingAmount} × {units}{cap}。）" : $" (Includes {effect.ScalingAmount} × {units}{cap}.)"
                : chinese ? $" 数值额外增加 {effect.ScalingAmount} × {units}{cap}。" : $" Add {effect.ScalingAmount} × {units} to the amount{cap}.";
        }
        string condition = (chinese, effect.Condition) switch
        {
            (_, EffectCondition.None) => "",
            (true, EffectCondition.SelfHasBlock) => "若你有格挡：", (false, EffectCondition.SelfHasBlock) => "If you have Block: ",
            (true, EffectCondition.SelfHpBelowHalf) => "若你的生命低于一半：", (false, EffectCondition.SelfHpBelowHalf) => "If you are below half HP: ",
            (true, EffectCondition.TargetWeak) => "仅对有虚弱的敌人：", (false, EffectCondition.TargetWeak) => "Only against enemies with Weak: ",
            (true, EffectCondition.TargetVulnerable) => "仅对有易伤的敌人：", (false, EffectCondition.TargetVulnerable) => "Only against enemies with Vulnerable: ",
            _ => throw new InvalidOperationException()
        };
        if (effect.Repeat > 1) action += chinese ? $" 重复 {effect.Repeat} 次。" : $" Repeat {effect.Repeat} times.";
        if (effect.Trigger == EffectTrigger.OnPlay) return condition + action;
        string trigger = (chinese, effect.Trigger) switch
        {
            (true, EffectTrigger.NextTurnStart) => "下回合开始时", (false, EffectTrigger.NextTurnStart) => "At the start of your next turn",
            (true, EffectTrigger.TurnStart) => "你的回合开始时", (false, EffectTrigger.TurnStart) => "At the start of your turn",
            (true, EffectTrigger.TurnEnd) => "你的回合结束时", (false, EffectTrigger.TurnEnd) => "At the end of your turn",
            (true, EffectTrigger.CardPlayed) => "每当你打出牌后", (false, EffectTrigger.CardPlayed) => "After you play a card",
            (true, EffectTrigger.AttackPlayed) => "每当你打出攻击牌后", (false, EffectTrigger.AttackPlayed) => "After you play an Attack",
            (true, EffectTrigger.SkillPlayed) => "每当你打出技能牌后", (false, EffectTrigger.SkillPlayed) => "After you play a Skill",
            (true, EffectTrigger.CardDrawn) => "每当你抽牌后", (false, EffectTrigger.CardDrawn) => "After you draw a card",
            (true, EffectTrigger.CardExhausted) => "每当你消耗一张牌后", (false, EffectTrigger.CardExhausted) => "After you exhaust a card",
            _ => throw new InvalidOperationException()
        };
        string lifetime = effect.Trigger == EffectTrigger.NextTurnStart ? "" : effect.Duration == 0
            ? omitCombatLifetime ? "" : chinese ? "本场战斗中，" : "For this combat, "
            : effect.Trigger == EffectTrigger.TurnStart
                ? chinese ? $"接下来 {effect.Duration} 个回合，" : $"For your next {effect.Duration} turns, "
                : effect.Duration == 1 ? chinese ? "本回合，" : "This turn, "
                : chinese ? $"本回合及之后 {effect.Duration - 1} 个回合，" : $"This turn and your next {effect.Duration - 1} turns, ";
        string limit = EffectRules.IsEvent(effect.Trigger) && effect.MaxPerTurn > 0
            ? chinese ? $"（每回合最多 {effect.MaxPerTurn} 次）" : $" (at most {effect.MaxPerTurn} times per turn)" : "";
        string attempts = effect.Condition == EffectCondition.None || effect.MaxPerTurn == 0 ? ""
            : chinese ? " 条件不满足也计入次数与持续时间。" : " Failed conditions still consume the event quota and duration.";
        return lifetime + trigger + limit + (chinese ? "：" : ": ") + condition + action + attempts;
    }

    public static string ResourceIcons(int count, string highlightedAmount, string iconPath)
    {
        string icon = $"[img]{iconPath}[/img]";
        if (count is < 1 or >= 4) return highlightedAmount + icon;
        string icons = string.Concat(Enumerable.Repeat(icon, count));
        // Native resource formatters omit coloring small icon counts. Preserve upgrade/buff highlighting.
        int end = highlightedAmount.IndexOf(']');
        return end >= 0 && highlightedAmount.StartsWith('[')
            ? highlightedAmount[..(end + 1)] + icons + highlightedAmount[highlightedAmount.LastIndexOf('[')..] : icons;
    }
}
