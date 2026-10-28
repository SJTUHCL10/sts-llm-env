using System.Reflection.Emit;
using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Forge.Mod;

// Populate is synchronous on the game thread. Scope only that reward, never event/shop factories.
internal sealed class GeneratedRewardCreation : IDisposable
{
    [ThreadStatic] private static GeneratedRewardCreation? _current;
    private static readonly Func<Player, IEnumerable<CardModel>, CardCreationOptions, CardModel> NativeCreate =
        AccessTools.MethodDelegate<Func<Player, IEnumerable<CardModel>, CardCreationOptions, CardModel>>(
            AccessTools.Method(typeof(CardFactory), nameof(CardFactory.CreateForReward),
                [typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardCreationOptions)]));
    private readonly GeneratedRewardCreation? _previous;
    private readonly Player _player;
    private readonly CardCreationOptions _options;
    private readonly Func<CardDefinition[]> _definitions;
    private NeowGeneratedCard[] _cards = [];
    internal bool Extended { get; private set; }
    internal int OriginalCount { get; private set; }
    internal int GeneratedCount => _cards.Length;

    internal GeneratedRewardCreation(Player player, CardCreationOptions options, Func<CardDefinition[]> definitions)
    {
        _player = player; _options = options; _definitions = definitions;
        _previous = _current;
        _current = this;
    }
    public void Dispose() => _current = _previous;

    internal static int ExtendCount(Player player, int count, CardCreationOptions options)
    {
        if (_current is not { Extended: false } scope || scope._player != player
            || !ReferenceEquals(scope._options, options) || count <= 0) return count;
        try
        {
            // Validate and configure the entire batch before registering any native models.
            scope._cards = scope._definitions().Select(definition =>
            {
                var card = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
                card.DefinitionPayload = Wire.Encode(definition);
                return card;
            }).ToArray();
            scope.OriginalCount = count;
            scope.Extended = true;
            return checked(count + scope._cards.Length);
        }
        catch (Exception ex)
        {
            Bootstrap.Runtime?.Audit("reward_generation_failed", new { category = ex.GetType().Name });
            return count;
        }
    }

    internal static CardModel CreateCard(Player player, IEnumerable<CardModel> excluded, CardCreationOptions options)
    {
        if (_current is { Extended: true } scope && scope._player == player
            && ReferenceEquals(scope._options, options))
        {
            int index = excluded.Count() - scope.OriginalCount;
            if (index >= 0 && index < scope._cards.Length)
            {
                var card = scope._cards[index];
                player.RunState.AddCard(card, player);
                card.AfterCreated();
                return card;
            }
        }
        return NativeCreate(player, excluded, options);
    }
}

[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward),
    [typeof(Player), typeof(int), typeof(CardCreationOptions)])]
internal static class GeneratedRewardFactoryPatch
{
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        var single = AccessTools.Method(typeof(CardFactory), nameof(CardFactory.CreateForReward),
            [typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardCreationOptions)]);
        var calls = body.Where(i => i.Calls(single)).ToArray();
        if (calls.Length != 1) throw new MissingMethodException("CardFactory reward creation loop changed.");
        calls[0].operand = AccessTools.Method(typeof(GeneratedRewardCreation), nameof(GeneratedRewardCreation.CreateCard));
        // Extend the vanilla loop: every appended card passes the existing random upgrade branch,
        // then all cards pass the native reward hooks together exactly once.
        return new CodeInstruction[]
        {
            new(OpCodes.Ldarg_0), new(OpCodes.Ldarg_1), new(OpCodes.Ldarg_2),
            new(OpCodes.Call, AccessTools.Method(typeof(GeneratedRewardCreation), nameof(GeneratedRewardCreation.ExtendCount))),
            new(OpCodes.Starg_S, (byte)1)
        }.Concat(body);
    }
}
