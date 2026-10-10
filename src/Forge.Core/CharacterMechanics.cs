using System.Text.Json;
namespace Forge.Core;

public sealed record CharacterMechanics(bool Poison, bool Stars, bool Necrobinder = false, bool Defect = false)
{
    public static CharacterMechanics FromRun(JsonElement run)
    {
        bool silent = HasId(run, "character", "SILENT"), regent = HasId(run, "character", "REGENT"),
            necro = HasId(run, "character", "NECROBINDER"), defect = HasId(run, "character", "DEFECT");
        if (run.ValueKind == JsonValueKind.Object)
        {
            if (run.TryGetProperty("relics", out var relics) && relics.ValueKind == JsonValueKind.Array && relics.EnumerateArray().Any(r => HasId(r, "id", "PRISMATIC_GEM"))) return new(true, true, true, true);
            if (run.TryGetProperty("deck", out var deck) && deck.ValueKind == JsonValueKind.Array)
                foreach (var card in deck.EnumerateArray())
                {
                    if (HasId(card, "origin", "generated") || card.ValueKind == JsonValueKind.Object && card.TryGetProperty("generated_definition", out var definition) && definition.ValueKind != JsonValueKind.Null) continue;
                    silent |= HasId(card, "pool", "SILENT_CARD_POOL"); regent |= HasId(card, "pool", "REGENT_CARD_POOL");
                    necro |= HasId(card, "pool", "NECROBINDER_CARD_POOL"); defect |= HasId(card, "pool", "DEFECT_CARD_POOL");
                }
        }
        return new(silent, regent, necro, defect);
    }
    public bool AllowsPower(string id) => id switch { "poison" => Poison, "doom" => Necrobinder, "focus" => Defect, _ => true };
    public bool AllowsCard(string id) => id switch
    {
        "soul" => Necrobinder, "shiv" => Poison, "fuel" => Defect,
        "debris" or "minion_strike" or "minion_sacrifice" or "minion_dive" or "sovereign_blade" => Stars, _ => true
    };
    public CardDefinition Validate(CardDefinition card)
    {
        CardValidator.Validate(card);
        foreach (var form in card.Forms)
        {
            if (!Stars && (form.Cost.Stars is not null || form.Cost.StarsX == true)) Unavailable();
            if (!Poison && form.Tags.Contains(ForgeKeyword.Sly)) Unavailable();
            foreach (var effect in form.AllEffects)
            {
                if (effect.Power is { } power && !AllowsPower(power) || effect.Card?.Id is { } id && !AllowsCard(id)
                    || effect.Kind is EffectKind.GainStars or EffectKind.Forge && !Stars
                    || (effect.Kind == EffectKind.Summon || effect.Actor == "osty" || effect.Target?.Ref == "osty") && !Necrobinder
                    || effect.Kind is EffectKind.Channel or EffectKind.Evoke or EffectKind.OrbPassive or EffectKind.OrbSlots && !Defect
                    || effect.Keyword == ForgeKeyword.Sly && !Poison) Unavailable();
                CheckFilter(effect.Target?.Filter); CheckFilter(effect.Card?.Filter);
                CheckNumber(effect.Amount); CheckNumber(effect.Repeat); CheckNumber(effect.Count); CheckNumber(effect.Target?.Count); CheckCondition(effect.Condition);
            }
            foreach (var rule in form.Listeners)
            {
                if (rule.Trigger.Event == RuleEvent.Summoned && !Necrobinder || rule.Trigger.Event is RuleEvent.OrbChanneled or RuleEvent.OrbEvoked && !Defect) Unavailable();
                CheckFilter(rule.Trigger.Filter); CheckCondition(rule.Condition);
            }
        }
        return card;
    }
    private void CheckFilter(CardFilter? filter)
    {
        if (filter?.Id is { } id && !AllowsCard(id) || filter?.Keyword == ForgeKeyword.Sly && !Poison) Unavailable();
    }
    private void CheckNumber(NumberExpression? number)
    {
        if (number is null) return;
        if (number.Of == "osty" && !Necrobinder || number.Stat == "power" && !AllowsPower(number.Id!)
            || number.Stat is "stars" or "paid_stars" && !Stars || number.Stat is "orb_count" or "orb_capacity" && !Defect) Unavailable();
        foreach (var child in number.Add ?? number.Sub ?? number.Mul ?? number.Div ?? []) CheckNumber(child);
    }
    private void CheckCondition(EffectCondition? condition)
    {
        if (condition is null) return;
        CheckNumber(condition.Left); CheckNumber(condition.Right);
        foreach (var child in condition.All ?? condition.Any ?? (condition.Not is null ? [] : new[] { condition.Not })) CheckCondition(child);
    }
    private static void Unavailable() => throw new FormatException("Mechanic is unavailable in this run.");
    private static bool HasId(JsonElement value, string property, string id) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() == id;
}
