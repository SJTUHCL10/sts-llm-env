using System.Text.Json;

namespace Forge.Core;

// Availability is independent of design preference. Never infer permissions from localized prose or generated cards.
public sealed record CharacterMechanics(bool Poison, bool Stars)
{
    public static CharacterMechanics FromRun(JsonElement run)
    {
        bool poison = HasId(run, "character", "SILENT");
        bool stars = HasId(run, "character", "REGENT");
        if (run.ValueKind == JsonValueKind.Object)
        {
            if (run.TryGetProperty("relics", out var relics) && relics.ValueKind == JsonValueKind.Array
                && relics.EnumerateArray().Any(relic => HasId(relic, "id", "PRISMATIC_GEM")))
                return new(true, true);
            if (run.TryGetProperty("deck", out var deck) && deck.ValueKind == JsonValueKind.Array)
                foreach (var card in deck.EnumerateArray())
                {
                    if (card.ValueKind != JsonValueKind.Object
                        || HasId(card, "origin", "generated")
                        || card.TryGetProperty("generated_definition", out var definition) && definition.ValueKind != JsonValueKind.Null)
                        continue;
                    poison |= HasId(card, "pool", "SILENT_CARD_POOL");
                    stars |= HasId(card, "pool", "REGENT_CARD_POOL");
                }
        }
        return new(poison, stars);
    }

    public CardDefinition Validate(CardDefinition card)
    {
        CardValidator.Validate(card);
        if (!Poison && card.Effects.Any(e => e.Kind == EffectKind.Poison || e.Scaling == EffectScaling.TargetPoison))
            throw new FormatException("Poison mechanics are unavailable in this run.");
        if (!Stars && (card.StarCost >= 0 || card.Effects.Any(e => e.Kind == EffectKind.Stars || e.Scaling == EffectScaling.SelfStars)))
            throw new FormatException("Star mechanics are unavailable in this run.");
        return card;
    }

    private static bool HasId(JsonElement value, string property, string id) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.String && item.GetString() == id;
}
