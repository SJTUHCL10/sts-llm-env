namespace Forge.Core;

public static class CardValidator
{
    public static CardDefinition Validate(CardDefinition card)
    {
        if (card is null || card.SchemaVersion != 1) throw new FormatException("Unsupported card schema.");
        if (!SafeText(card.Name, 40) || !SafeText(card.Flavor, 160, allowEmpty: true))
            throw new FormatException("Invalid card name/flavor (plain text only).");
        if (!Enum.IsDefined(card.Type) || !Enum.IsDefined(card.Rarity) || card.Cost is < 0 or > 3)
            throw new FormatException("Invalid card type/rarity/cost.");
        if (card.Keywords is null || card.Keywords.Length > 3 || card.Keywords.Distinct().Count() != card.Keywords.Length
            || card.Keywords.Any(k => !Enum.IsDefined(k))) throw new FormatException("Invalid keywords.");
        if (card.Effects is null || card.Effects.Length is < 1 or > 4) throw new FormatException("A card needs 1..4 effects.");
        foreach (var e in card.Effects)
        {
            if (e is null || !Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.Target)) throw new FormatException("Unknown effect.");
            var (max, upgrade) = Limits(e.Kind);
            if (e.Amount < 1 || e.Amount > max || e.UpgradeAmount < 0 || e.UpgradeAmount > upgrade
                || e.Amount + e.UpgradeAmount > max) throw new FormatException($"Effect {e.Kind} exceeds bounds.");
            bool hostile = e.Kind is EffectKind.Damage or EffectKind.Weak or EffectKind.Vulnerable or EffectKind.Poison;
            if (hostile == (e.Target == EffectTarget.Self)) throw new FormatException("Effect target does not match effect kind.");
        }
        if ((card.Type == ForgeCardType.Attack) != card.Effects.Any(e => e.Kind == EffectKind.Damage))
            throw new FormatException("Only attack cards may deal damage; every attack needs damage.");
        // A coarse ceiling, not a complete simulation: configuration styles cannot bypass it.
        double score = card.Effects.Sum(e => Weight(e.Kind) * (e.Amount + e.UpgradeAmount) * (e.Target == EffectTarget.AllEnemies ? 1.8 : 1));
        double budget = 10 + 12 * card.Cost + (card.Rarity == ForgeRarity.Rare ? 8 : card.Rarity == ForgeRarity.Uncommon ? 4 : 0)
            + (card.Keywords.Contains(ForgeKeyword.Exhaust) ? 10 : 0);
        if (score > budget) throw new FormatException("Card exceeds the initial power budget.");
        if (card.Cost == 0 && !card.Keywords.Contains(ForgeKeyword.Exhaust)
            && card.Effects.Any(e => e.Kind is EffectKind.Energy or EffectKind.Draw))
            throw new FormatException("Zero-cost draw/energy requires exhaust.");
        return card;
    }

    public static (int Amount, int Upgrade) Limits(EffectKind kind) => kind switch
    {
        EffectKind.Damage => (30, 6), EffectKind.Block => (25, 5), EffectKind.Draw => (3, 1),
        EffectKind.Energy => (2, 1), EffectKind.Strength or EffectKind.Dexterity => (3, 1),
        EffectKind.Weak or EffectKind.Vulnerable => (3, 1), EffectKind.Poison => (10, 3),
        _ => throw new FormatException("Unknown effect kind.")
    };
    private static double Weight(EffectKind kind) => kind switch
    {
        EffectKind.Draw => 5, EffectKind.Energy => 8, EffectKind.Strength or EffectKind.Dexterity => 6,
        EffectKind.Weak or EffectKind.Vulnerable => 3, EffectKind.Poison => 2, _ => 1
    };
    private static bool SafeText(string? value, int max, bool allowEmpty = false) => value is not null
        && value.Length <= max && (allowEmpty || !string.IsNullOrWhiteSpace(value))
        && !value.Any(c => char.IsControl(c) || c is '[' or ']' or '{' or '}' or '<' or '>');
}
