using System.Reflection;
using System.Runtime.CompilerServices;
using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace Forge.Mod;

[HarmonyPatch(typeof(CombatHistory), "Add")]
internal static class HistoryPatch
{
    private static void Postfix(ICombatState combatState, CombatHistoryEntry entry) =>
        Bootstrap.Safe(() => Bootstrap.Runtime?.OnHistory(combatState, entry));
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.Clear))]
internal static class HistoryClearPatch
{
    private static void Prefix() => Bootstrap.Safe(() => Bootstrap.Runtime?.BeforeHistoryClear());
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class ResetPatch
{
    private static void Postfix() => Bootstrap.Safe(() => Bootstrap.Runtime?.Reset());
}

// The original async method must begin only after definitions are attached, before it creates its screen.
[HarmonyPatch(typeof(CardReward), "OnSelect")]
internal static class RewardSelectPatch
{
    private sealed class Marker;
    private static readonly ConditionalWeakTable<CardReward, Marker> Attached = new();
    private static readonly FieldInfo Options = AccessTools.Field(typeof(CardReward), "<Options>k__BackingField");
    private static readonly FieldInfo Cards = AccessTools.Field(typeof(CardReward), "_cards");
    private static readonly FieldInfo Manual = AccessTools.Field(typeof(CardReward), "_cardsWereManuallySet");
    private static bool Prefix(CardReward __instance, ref Task<bool> __result)
    {
        try
        {
            if (Bootstrap.Runtime is not { Eligible: true } runtime || Manual.GetValue(__instance) is true
                || Options.GetValue(__instance) is not CardCreationOptions options || options.Source != CardCreationSource.Encounter
                || ((List<CardCreationResult>)Cards.GetValue(__instance)!).Count == 0) return true;
            if (runtime.Config.GenerationTiming == GenerationTiming.Prefetch)
            {
                EnsureAttached(__instance, runtime);
                // Default mode runs the original method normally, preserving other Harmony patches.
                return true;
            }
            __result = Select(__instance, runtime);
            return false;
        }
        catch (Exception ex)
        {
            Bootstrap.Runtime?.Audit("reward_generation_failed", new { category = ex.GetType().Name });
            return true;
        }
    }

    private static async Task<bool> Select(CardReward reward, ForgeRuntime runtime)
    {
        if (!Attached.TryGetValue(reward, out _))
        {
            try
            {
                await runtime.PrepareReward(reward.Player);
                if (runtime.Eligible) EnsureAttached(reward, runtime);
            }
            catch (Exception ex) { runtime.Audit("reward_generation_failed", new { category = ex.GetType().Name }); }
        }
        return await Original(reward);
    }

    private static void EnsureAttached(CardReward reward, ForgeRuntime runtime)
    {
        if (Attached.TryGetValue(reward, out _)) return;
        AppendCards(reward, runtime);
        Attached.Add(reward, new Marker());
    }

    private static void Postfix(CardReward __instance, ref Task<bool> __result)
    {
        if (Attached.TryGetValue(__instance, out _) && Bootstrap.Runtime is { } runtime)
            __result = Observe(__result, __instance, runtime);
    }

    private static async Task<bool> Observe(Task<bool> original, CardReward reward, ForgeRuntime runtime)
    {
        bool selected = await original;
        Bootstrap.Safe(() =>
        {
            runtime.RecordRewardChoice(reward.Player);
            runtime.Audit("reward_closed", new { selected, deck = runtime.CaptureCards(reward.Player.Deck.Cards) });
        });
        return selected;
    }

    private static void AppendCards(CardReward reward, ForgeRuntime runtime)
    {
        var cards = (List<CardCreationResult>)Cards.GetValue(reward)!;
        if (cards.Any(c => c.Card is NeowGeneratedCard)) return;
        var definitions = runtime.FreezeReward(reward.Player);
        // Configure detached mutable models first, register them only after the entire batch is valid.
        var additions = definitions.Select(definition =>
        {
            var card = (NeowGeneratedCard)MegaCrit.Sts2.Core.Models.ModelDb.Card<NeowGeneratedCard>().ToMutable();
            card.DefinitionPayload = Wire.Encode(definition);
            return card;
        }).ToArray();
        foreach (var card in additions)
        {
            reward.Player.RunState.AddCard(card, reward.Player);
            card.AfterCreated();
        }
        var results = additions.Select(card => new CardCreationResult(card)).ToArray();
        foreach (var result in results)
            GeneratedRewardModifiers.Apply(result, cards, reward.Player.RunState.CloneCard);
        cards.AddRange(results);
        Bootstrap.Safe(() => runtime.Audit("reward_shown", new { original_count = cards.Count - additions.Length,
            generated_count = additions.Length, options = runtime.CaptureCards(cards.Select(c => c.Card)) }));
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(CardReward), "OnSelect")]
    internal static Task<bool> Original(CardReward instance) => throw new NotSupportedException("Harmony reverse patch not installed.");

    internal static void AfterPopulate(CardReward reward)
    {
        if (!Attached.TryGetValue(reward, out _) || Bootstrap.Runtime is not { Eligible: true } runtime) return;
        AppendCards(reward, runtime);
        var screen = (NCardRewardSelectionScreen?)AccessTools.Field(typeof(CardReward), "_currentlyShownScreen").GetValue(reward);
        screen?.RefreshOptions((List<CardCreationResult>)Cards.GetValue(reward)!, CardRewardAlternative.Generate(reward));
    }
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
internal static class RewardPopulatePatch
{
    private static void Postfix(CardReward __instance) => Bootstrap.Safe(() => RewardSelectPatch.AfterPopulate(__instance));
    // Populate runs during an open-screen reroll: reattach frozen cards and refresh that screen immediately.
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunCleanupPatch
{
    private static void Prefix() => Bootstrap.Safe(() => Bootstrap.Runtime?.StopRun());
}
