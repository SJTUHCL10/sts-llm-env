namespace Forge.Core;

public static class CardValidator
{
    public static CardDefinition Validate(CardDefinition card)
    {
        if (card is null || card.SchemaVersion is not (1 or 2 or 3 or 4)) throw new FormatException("Unsupported card schema.");
        if (card.SchemaVersion >= 3) return ValidateModern(card);
        if (card.StarCost != -1 || card.UpgradeCost != 0 || card.UpgradeStarCost != 0
            || card.Effects?.Any(e => e is not null && (e.Kind == EffectKind.Stars || e.Scaling == EffectScaling.SelfStars)) == true)
            throw new FormatException("Star mechanics and cost upgrades require schema_version 3.");
        if (!SafeText(card.Name, 40) || !SafeText(card.Flavor, 160, allowEmpty: true))
            throw new FormatException("Invalid card name/flavor (plain text only).");
        if (!Enum.IsDefined(card.Type) || !Enum.IsDefined(card.Rarity) || card.Cost is < 0 or > 5)
            throw new FormatException("Invalid card type/rarity/cost.");
        if (card.Keywords is null || card.Keywords.Length > 3 || card.Keywords.Distinct().Count() != card.Keywords.Length
            || card.Keywords.Any(k => !Enum.IsDefined(k))) throw new FormatException("Invalid keywords.");
        if (card.Effects is null || card.Effects.Length < 1 || card.Effects.Length > (card.SchemaVersion == 1 ? 4 : 8))
            throw new FormatException("A card needs 1..4 (v1) or 1..8 (v2) effects.");
        if (card.SchemaVersion == 1 && (card.Type == ForgeCardType.Power || card.Effects.Any(e => e is not null &&
            (e.Kind > EffectKind.Poison || e.Target == EffectTarget.RandomEnemy || e.Trigger != EffectTrigger.OnPlay
            || e.Duration != 1 || e.MaxPerTurn != 1 || e.Repeat != 1 || e.Condition != EffectCondition.None
            || e.Scaling != EffectScaling.None || e.ScalingAmount != 0 || e.ScalingCap != 0))))
            throw new FormatException("Complex effects require schema_version 2.");
        foreach (var e in card.Effects)
        {
            if (e is null || !Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.Target) || !Enum.IsDefined(e.Trigger)
                || !Enum.IsDefined(e.Condition) || !Enum.IsDefined(e.Scaling)) throw new FormatException("Unknown effect.");
            var (max, upgrade) = Limits(e.Kind);
            if (e.Amount < 1 || e.Amount > max || e.UpgradeAmount < 0 || e.UpgradeAmount > upgrade
                || e.Amount + e.UpgradeAmount > max) throw new FormatException($"Effect {e.Kind} exceeds bounds.");
            bool hostile = e.Kind is EffectKind.Damage or EffectKind.Weak or EffectKind.Vulnerable or EffectKind.Poison;
            if (hostile == (e.Target == EffectTarget.Self)) throw new FormatException("Effect target does not match effect kind.");
            if (e.Repeat is < 1 or > 3 || e.Duration is < 0 or > 3 || e.MaxPerTurn is < 1 or > 3)
                throw new FormatException("Repeat/duration/activation limit exceeds bounds.");
            if (e.Trigger == EffectTrigger.OnPlay && (e.Duration != 1 || e.MaxPerTurn != 1)
                || e.Trigger == EffectTrigger.NextTurnStart && (e.Duration != 1 || e.MaxPerTurn != 1)
                || !EffectRules.IsEvent(e.Trigger) && e.MaxPerTurn != 1
                || e.Duration == 0 && (card.Type != ForgeCardType.Power || e.Trigger == EffectTrigger.OnPlay))
                throw new FormatException("Invalid trigger lifetime.");
            if (e.Scaling == EffectScaling.None ? e.ScalingAmount != 0 || e.ScalingCap != 0
                : e.ScalingAmount is < 1 or > 3 || e.ScalingCap is < 1 or > 5)
                throw new FormatException("Invalid scaling slots.");
            if (EffectRules.MaximumAmount(e) > max) throw new FormatException("Scaled amount exceeds effect bounds.");
            if (e.Target == EffectTarget.Self && (e.Condition is EffectCondition.TargetWeak or EffectCondition.TargetVulnerable
                || e.Scaling == EffectScaling.TargetPoison)) throw new FormatException("Target predicate requires an enemy.");
            // A selected enemy is a play-time identity. Persistent effects use random/all enemies instead of retaining stale targets.
            if (e.Trigger != EffectTrigger.OnPlay && e.Target == EffectTarget.Enemy)
                throw new FormatException("Triggered enemy effects must use random_enemy or all_enemies.");
        }
        bool immediateDamage = card.Effects.Any(e => e.Kind == EffectKind.Damage && e.Trigger == EffectTrigger.OnPlay);
        if (card.Type == ForgeCardType.Attack && !card.Effects.Any(e => e.Kind == EffectKind.Damage)
            || card.Type != ForgeCardType.Attack && immediateDamage)
            throw new FormatException("Immediate damage requires attack; every attack needs damage.");
        if (card.Type == ForgeCardType.Power && (!card.Effects.Any(e => e.Trigger != EffectTrigger.OnPlay)
            || card.Keywords.Any(k => k is ForgeKeyword.Exhaust or ForgeKeyword.Retain)))
            throw new FormatException("Power needs a persistent effect and cannot exhaust/retain.");
        // A coarse ceiling, not a complete simulation: configuration styles cannot bypass it.
        double score = card.Effects.Sum(e => Weight(e.Kind) * EffectRules.MaximumAmount(e) * e.Repeat
            * EffectRules.BudgetActivations(e) * (e.Target == EffectTarget.AllEnemies ? 1.8 : 1));
        double budget = 10 + 15 * card.Cost + (card.Rarity == ForgeRarity.Rare ? 8 : card.Rarity == ForgeRarity.Uncommon ? 4 : 0)
            + (card.Keywords.Contains(ForgeKeyword.Exhaust) ? 10 : 0);
        if (score > budget) throw new FormatException("Card exceeds the initial power budget.");
        if (card.Cost == 0 && !card.Keywords.Contains(ForgeKeyword.Exhaust)
            && card.Effects.Any(e => e.Kind is EffectKind.Energy or EffectKind.Draw or EffectKind.ReturnRandomDiscard))
            throw new FormatException("Zero-cost draw/energy/return requires exhaust.");
        return card;
    }

    public static (int Amount, int Upgrade) Limits(EffectKind kind) => kind switch
    {
        EffectKind.Damage => (80, 15), EffectKind.Block => (60, 12), EffectKind.Draw => (6, 3),
        EffectKind.Energy => (4, 2), EffectKind.Strength or EffectKind.Dexterity => (8, 3),
        EffectKind.Weak or EffectKind.Vulnerable => (6, 3), EffectKind.Poison => (24, 6),
        EffectKind.DiscardRandomHand or EffectKind.ExhaustRandomHand or EffectKind.ReturnRandomDiscard => (5, 2),
        EffectKind.Stars => (24, 6),
        _ => throw new FormatException("Unknown effect kind.")
    };
    private static CardDefinition ValidateModern(CardDefinition card)
    {
        if (!SafeText(card.Name, 40) || !SafeText(card.Flavor, 160, allowEmpty: true))
            throw new FormatException("Invalid card name/flavor (plain text only).");
        if (!Enum.IsDefined(card.Type) || !Enum.IsDefined(card.Rarity) || card.Cost < 0 || card.StarCost < -1
            || card.UpgradeCost < 0 || card.UpgradeCost > card.Cost || card.UpgradeStarCost < 0
            || card.UpgradeStarCost > Math.Max(0, card.StarCost))
            throw new FormatException("Invalid card type/rarity/cost.");
        if (card.Keywords is null || card.Keywords.Length > 3 || card.Keywords.Distinct().Count() != card.Keywords.Length
            || card.Keywords.Any(k => !Enum.IsDefined(k))) throw new FormatException("Invalid keywords.");
        if (card.Effects is null || card.Effects.Length is < 1 or > 8) throw new FormatException("A card needs 1..8 effects.");
        foreach (var e in card.Effects)
        {
            if (e is null || !Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.Target) || !Enum.IsDefined(e.Trigger)
                || !Enum.IsDefined(e.Condition) || !Enum.IsDefined(e.Scaling)) throw new FormatException("Unknown effect.");
            if (card.SchemaVersion == 4 && e.ScalingCap != 0) throw new FormatException("Scaling caps require a legacy card schema.");
            if (e.Amount < 1 || e.UpgradeAmount < 0 || (long)e.Amount + e.UpgradeAmount > int.MaxValue
                || e.Repeat < 1 || e.Duration < 0 || e.MaxPerTurn < 0 || e.ScalingAmount < 0 || e.ScalingCap < 0)
                throw new FormatException("Invalid numeric effect slots.");
            bool hostile = e.Kind is EffectKind.Damage or EffectKind.Weak or EffectKind.Vulnerable or EffectKind.Poison;
            if (hostile == (e.Target == EffectTarget.Self)) throw new FormatException("Effect target does not match effect kind.");
            if (e.Trigger is EffectTrigger.OnPlay or EffectTrigger.NextTurnStart && e.Duration != 1
                || !EffectRules.IsEvent(e.Trigger) && e.MaxPerTurn != 1
                || e.Duration == 0 && (card.Type != ForgeCardType.Power || e.Trigger == EffectTrigger.OnPlay))
                throw new FormatException("Invalid trigger lifetime.");
            if (e.Scaling == EffectScaling.None ? e.ScalingAmount != 0 || e.ScalingCap != 0 : e.ScalingAmount < 1)
                throw new FormatException("Invalid scaling slots.");
            if (e.Target == EffectTarget.Self && (e.Condition is EffectCondition.TargetWeak or EffectCondition.TargetVulnerable
                || e.Scaling == EffectScaling.TargetPoison)) throw new FormatException("Target predicate requires an enemy.");
            if (e.Trigger != EffectTrigger.OnPlay && e.Target == EffectTarget.Enemy)
                throw new FormatException("Triggered enemy effects must use random_enemy or all_enemies.");
        }
        if (card.Type == ForgeCardType.Attack && !card.Effects.Any(e => e.Kind == EffectKind.Damage)
            || card.Type != ForgeCardType.Attack && card.Effects.Any(e => e.Kind == EffectKind.Damage && e.Trigger == EffectTrigger.OnPlay))
            throw new FormatException("Immediate damage requires attack; every attack needs damage.");
        if (card.Type == ForgeCardType.Power && (!card.Effects.Any(e => e.Trigger != EffectTrigger.OnPlay)
            || card.Keywords.Any(k => k is ForgeKeyword.Exhaust or ForgeKeyword.Retain)))
            throw new FormatException("Power needs a persistent effect and cannot exhaust/retain.");
        return card;
    }
    private static double Weight(EffectKind kind) => kind switch
    {
        EffectKind.Draw => 5, EffectKind.Energy => 8, EffectKind.Strength or EffectKind.Dexterity => 6,
        EffectKind.Weak or EffectKind.Vulnerable => 3, EffectKind.Poison => 2,
        EffectKind.ReturnRandomDiscard => 5, EffectKind.ExhaustRandomHand => 3, _ => 1
    };
    private static bool SafeText(string? value, int max, bool allowEmpty = false) => value is not null
        && value.Length <= max && (allowEmpty || !string.IsNullOrWhiteSpace(value))
        && !value.Any(c => char.IsControl(c) || c is '[' or ']' or '{' or '}' or '<' or '>');
}
