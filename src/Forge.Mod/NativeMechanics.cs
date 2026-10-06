using Forge.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
namespace Forge.Mod;

internal static class NativeMechanics
{
    private static readonly Func<PlayerChoiceContext, Player, OrbModel, bool, Task> EvokeCommand = HarmonyLib.AccessTools.Method(typeof(OrbCmd), "Evoke").CreateDelegate<Func<PlayerChoiceContext, Player, OrbModel, bool, Task>>();
    internal static Task Evoke(PlayerChoiceContext choice, Player player, OrbModel orb, bool remove) => EvokeCommand(choice, player, orb, remove);
    internal static PowerModel TemporaryPower(string id, bool negative) => (id, negative) switch
    {
        ("strength", false) => ModelDb.Power<FlexPotionPower>(), ("strength", true) => ModelDb.Power<DarkShacklesPower>(),
        ("focus", false) => ModelDb.Power<HotfixPower>(), ("focus", true) => ModelDb.Power<HyperbeamFocusDownPower>(),
        _ => throw new FormatException("Unsupported temporary power.")
    };
    internal static CardModel Card(string id) => id switch
    {
        "soul" => ModelDb.Card<Soul>(), "shiv" => ModelDb.Card<Shiv>(), "wound" => ModelDb.Card<Wound>(), "dazed" => ModelDb.Card<Dazed>(), "burn" => ModelDb.Card<Burn>(), "void" => ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.Void>(), "slimed" => ModelDb.Card<Slimed>(), "fuel" => ModelDb.Card<Fuel>(), "debris" => ModelDb.Card<Debris>(), "minion_strike" => ModelDb.Card<MinionStrike>(), "minion_sacrifice" => ModelDb.Card<MinionSacrifice>(), "minion_dive" => ModelDb.Card<MinionDiveBomb>(), "sovereign_blade" => ModelDb.Card<SovereignBlade>(), _ => throw new FormatException("Unknown card alias.")
    };
    internal static PowerModel Power(string id) => id switch
    {
        "strength" => ModelDb.Power<StrengthPower>(), "dexterity" => ModelDb.Power<DexterityPower>(), "weak" => ModelDb.Power<WeakPower>(), "vulnerable" => ModelDb.Power<VulnerablePower>(), "frail" => ModelDb.Power<FrailPower>(), "poison" => ModelDb.Power<PoisonPower>(), "doom" => ModelDb.Power<DoomPower>(), "focus" => ModelDb.Power<FocusPower>(), "vigor" => ModelDb.Power<VigorPower>(), "thorns" => ModelDb.Power<ThornsPower>(), "plating" => ModelDb.Power<PlatingPower>(), "intangible" => ModelDb.Power<IntangiblePower>(), "artifact" => ModelDb.Power<ArtifactPower>(), "buffer" => ModelDb.Power<BufferPower>(), "retain_block" => ModelDb.Power<BarricadePower>(), _ => throw new FormatException("Unknown power alias.")
    };
    internal static bool Matches(CardModel card, CardFilter? filter) => filter is null
        || (filter.Id is null || card.Id == Card(filter.Id).Id)
        && (filter.Type is null || card.Type.ToString().Equals(filter.Type, StringComparison.OrdinalIgnoreCase))
        && (filter.Rarity is null || card.Rarity.ToString().Equals(filter.Rarity.ToString(), StringComparison.OrdinalIgnoreCase))
        && (filter.Cost is null || !card.EnergyCost.CostsX && card.EnergyCost.GetWithModifiers(CostModifiers.All) == filter.Cost)
        && (filter.Upgraded is null || card.IsUpgraded == filter.Upgraded)
        && (filter.Keyword is null || card.Keywords.Contains(NeowGeneratedCard.NativeKeyword(filter.Keyword.Value)));
    internal static Task Channel(PlayerChoiceContext choice, Player player, string orb) => orb switch
    {
        "lightning" => OrbCmd.Channel<LightningOrb>(choice, player), "frost" => OrbCmd.Channel<FrostOrb>(choice, player), "dark" => OrbCmd.Channel<DarkOrb>(choice, player), "plasma" => OrbCmd.Channel<PlasmaOrb>(choice, player), "glass" => OrbCmd.Channel<GlassOrb>(choice, player), "random" => OrbCmd.Channel(choice, OrbModel.GetRandomOrb(player.RunState.Rng.CombatOrbGeneration).ToMutable(), player), _ => throw new FormatException("Unknown orb.")
    };
    internal static IEnumerable<IHoverTip> OrbTips(string id) => [id switch
    {
        "lightning" => HoverTipFactory.FromOrb<LightningOrb>(), "frost" => HoverTipFactory.FromOrb<FrostOrb>(), "dark" => HoverTipFactory.FromOrb<DarkOrb>(), "plasma" => HoverTipFactory.FromOrb<PlasmaOrb>(), "glass" => HoverTipFactory.FromOrb<GlassOrb>(), _ => throw new FormatException("Unknown orb.")
    }];
    internal static PileType Pile(CardPileName name) => name switch { CardPileName.Hand => PileType.Hand, CardPileName.Draw => PileType.Draw, CardPileName.Discard => PileType.Discard, _ => PileType.Exhaust };
}
