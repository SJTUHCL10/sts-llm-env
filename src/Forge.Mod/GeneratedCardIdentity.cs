using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Badges;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Forge.Mod;

// Generated cards share a registered model ID. Their saved definition identifies the
// card kind; InstanceKey identifies a physical copy and must not split identical cards.
internal static class GeneratedCardIdentity
{
    internal static bool IsGenerated(SerializableCard card) => card.Id == ModelDb.Card<NeowGeneratedCard>().Id;

    internal static string Payload(SerializableCard card) => card.Props?.strings?
        .Where(property => property.name == nameof(NeowGeneratedCard.DefinitionPayload))
        .Select(property => property.value).FirstOrDefault() ?? "";

    internal static IEnumerable<IGrouping<(ModelId? Id, string Payload), SerializableCard>> NonBasicGroups(IEnumerable<SerializableCard> deck) =>
        deck.Where(card => SaveUtil.CardOrDeprecated(card.Id!).Rarity != CardRarity.Basic)
            .GroupBy(card => (card.Id, IsGenerated(card) ? Payload(card) : ""));
}

// NDeckHistory groups SerializableCard objects using their native equality, which
// compares ID, upgrade level and enchantment but omits SavedProperties entirely.
[HarmonyPatch(typeof(SerializableCard), nameof(SerializableCard.Equals), [typeof(object)])]
internal static class GeneratedSavedCardEqualityPatch
{
    private static void Postfix(SerializableCard __instance, object? obj, ref bool __result)
    {
        if (__result && GeneratedCardIdentity.IsGenerated(__instance) && obj is SerializableCard other)
            __result = GeneratedCardIdentity.Payload(__instance) == GeneratedCardIdentity.Payload(other);
    }
}

[HarmonyPatch(typeof(SerializableCard), nameof(SerializableCard.GetHashCode))]
internal static class GeneratedSavedCardHashPatch
{
    private static void Postfix(SerializableCard __instance, ref int __result)
    {
        if (GeneratedCardIdentity.IsGenerated(__instance))
            __result = HashCode.Combine(__result, GeneratedCardIdentity.Payload(__instance));
    }
}

// Badge identity deliberately ignores upgrades and enchantments, as vanilla does.
// Re-evaluate only decks containing generated cards, leaving native decks alone.
[HarmonyPatch(typeof(Honed), nameof(Honed.IsObtained))]
internal static class GeneratedHonedPatch
{
    private static void Postfix(SerializablePlayer ____localPlayer, ref bool __result)
    {
        if (__result && ____localPlayer.Deck.Any(GeneratedCardIdentity.IsGenerated))
            __result = GeneratedCardIdentity.NonBasicGroups(____localPlayer.Deck).Any(group => group.Count() >= 5);
    }
}

[HarmonyPatch(typeof(Highlander), nameof(Highlander.IsObtained))]
internal static class GeneratedHighlanderPatch
{
    private static void Postfix(SerializablePlayer ____localPlayer, ref bool __result)
    {
        if (!__result && ____localPlayer.Deck.Any(GeneratedCardIdentity.IsGenerated))
            __result = GeneratedCardIdentity.NonBasicGroups(____localPlayer.Deck).All(group => group.Count() == 1);
    }
}
