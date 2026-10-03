using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace Forge.Mod;

internal static class GeneratedRewardModifiers
{
    internal static void Apply(CardCreationResult generated, IEnumerable<CardCreationResult> originals,
        Func<CardModel, CardModel> clone)
    {
        // Native reward creation already consumed this relic's charge, including its third charge.
        // Use that reward's provenance instead of checking the relic's remaining counter or running hooks twice.
        var crucible = originals.SelectMany(result => result.ModifyingRelics).OfType<SilverCrucible>().FirstOrDefault();
        if (crucible is null || !generated.Card.IsUpgradable) return;
        var upgraded = clone(generated.Card);
        upgraded.UpgradeInternal();
        upgraded.FinalizeUpgradeInternal();
        generated.ModifyCard(upgraded, crucible);
    }
}
