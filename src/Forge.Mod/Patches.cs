using System.Reflection;
using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
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

// Prepare network results before vanilla creates any reward cards or consumes reward RNG.
[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering))]
internal static class RewardPreparationPatch
{
    private static bool Prefix(RewardsSet __instance, ref Task __result)
    {
        if (Bootstrap.Runtime is not { Eligible: true } runtime
            || runtime.Config.GenerationTiming != GenerationTiming.WaitOnReward
            || __instance.Room is not MegaCrit.Sts2.Core.Rooms.CombatRoom
            || __instance.Rewards.OfType<CardReward>().All(r => r.IsPopulated)) return true;
        __result = PrepareAndGenerate(__instance, runtime);
        return false;
    }
    private static async Task PrepareAndGenerate(RewardsSet rewards, ForgeRuntime runtime)
    {
        try { await runtime.PrepareReward(rewards.Player); }
        catch (Exception ex) { runtime.Audit("reward_generation_failed", new { category = ex.GetType().Name }); }
        await Original(rewards);
    }
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering))]
    internal static Task Original(RewardsSet instance) => throw new NotSupportedException("Harmony reverse patch not installed.");
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
internal static class RewardPopulatePatch
{
    private static readonly FieldInfo Manual = AccessTools.Field(typeof(CardReward), "_cardsWereManuallySet");
    private static readonly FieldInfo Rerolled = AccessTools.Field(typeof(CardReward), "_hasBeenRerolled");
    private static readonly FieldInfo Options = AccessTools.Field(typeof(CardReward), "<Options>k__BackingField");
    private static readonly FieldInfo RerollOptions = AccessTools.Field(typeof(CardReward), "<RerollOptions>k__BackingField");
    private static void Prefix(CardReward __instance, out GeneratedRewardCreation? __state)
    {
        __state = null;
        if (Bootstrap.Runtime is not { Eligible: true } runtime || Manual.GetValue(__instance) is true
            || __instance.IsPopulated) return;
        var options = (CardCreationOptions?)(Rerolled.GetValue(__instance) is true
            ? RerollOptions.GetValue(__instance) : Options.GetValue(__instance));
        if (options?.Source != CardCreationSource.Encounter) return;
        __state = new GeneratedRewardCreation(__instance.Player, options, () => runtime.FreezeReward(__instance));
    }
    private static void Postfix(CardReward __instance, GeneratedRewardCreation? __state)
    {
        if (__state is not { Extended: true } || Bootstrap.Runtime is not { } runtime) return;
        Bootstrap.Safe(() => runtime.Audit("reward_shown", new { original_count = __state.OriginalCount,
            generated_count = __state.GeneratedCount, options = runtime.CaptureCards(__instance.Cards) }));
    }
    private static void Finalizer(GeneratedRewardCreation? __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(CardReward), "OnSelect")]
internal static class RewardSelectPatch
{
    private static void Postfix(CardReward __instance, ref Task<bool> __result)
    {
        if (__instance.Cards.Any(c => c is NeowGeneratedCard) && Bootstrap.Runtime is { } runtime)
            __result = Observe(__result, __instance, runtime);
    }
    private static async Task<bool> Observe(Task<bool> original, CardReward reward, ForgeRuntime runtime)
    {
        bool selected = await original;
        Bootstrap.Safe(() =>
        {
            runtime.RecordRewardChoice(reward);
            runtime.Audit("reward_closed", new { selected, deck = runtime.CaptureCards(reward.Player.Deck.Cards) });
        });
        return selected;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunCleanupPatch
{
    private static readonly PropertyInfo State = AccessTools.Property(typeof(RunManager), "State");
    private static void Prefix(RunManager __instance, bool graceful) => Bootstrap.Safe(() =>
    {
        if (State.GetValue(__instance) is RunState run) Bootstrap.Runtime?.UnloadRun(run.TotalFloor, graceful);
    });
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class RunEndedPatch
{
    private static void Prefix() => Bootstrap.Safe(() => Bootstrap.Runtime?.StopRun());
}
