using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Forge.Core;
using Forge.Mod;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

string game = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("STS2_GAME_DIR")
    ?? throw new ArgumentException("Provide game directory.");
string lib = Path.Combine(game, "data_sts2_windows_x86_64");
AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(lib, name.Name + ".dll"))
    ? context.LoadFromAssemblyPath(Path.Combine(lib, name.Name + ".dll")) : null;
Smoke.Run();

static class Smoke
{
    // Delay JIT until the game's dependency resolver is registered. No game process or save is touched.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run()
    {
        ModelDb.Init([typeof(NeowGeneratedCard), typeof(GeneratedEffectPower), typeof(SilverCrucible), typeof(DexterityPower)]);
        var definition = new CardDefinition
        {
            Name = "涅奥的试炼", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Uncommon, Cost = 2,
            Keywords = [ForgeKeyword.Exhaust],
            Effects = [new() { Kind = EffectKind.Damage, Target = EffectTarget.Enemy, Amount = 10, UpgradeAmount = 3 },
                new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 5, UpgradeAmount = 2 }]
        };
        var card = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        _ = card.EnergyCost; _ = card.DynamicVars; _ = card.Keywords; // Force fallback caches before configuring.
        card.DefinitionPayload = Wire.Encode(definition);
        Check(card.EnergyCost.Canonical == 2 && card.Type == CardType.Attack && card.TargetType == TargetType.AnyEnemy);
        Check(card.DynamicVars["E0"].BaseValue == 10 && card.Keywords.Contains(CardKeyword.Exhaust));
        Console.WriteLine("PASS game mutable card configuration and canonical-cache reset");

        var clone = (NeowGeneratedCard)card.ClonePreservingMutability();
        clone.DynamicVars["E0"].BaseValue = 19;
        Check(card.DynamicVars["E0"].BaseValue == 10 && clone.DefinitionPayload == card.DefinitionPayload);
        Console.WriteLine("PASS game native cloning preserves definition and isolates variables");

        // Initialize this model's serialization metadata without booting Godot or the game UI.
        ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(typeof(NeowGeneratedCard));
        typeof(ModelIdSerializationCache).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        card.UpgradeInternal();
        card.FinalizeUpgradeInternal();
        var save = card.ToSerializable();
        Check(save.Props!.strings!.Single(p => p.name == nameof(NeowGeneratedCard.DefinitionPayload)).value == card.DefinitionPayload);
        var loaded = (NeowGeneratedCard)CardModel.FromSerializable(save);
        Check(loaded.DefinitionPayload == card.DefinitionPayload && loaded.DynamicVars["E0"].BaseValue == 13
            && loaded.DynamicVars["E1"].BaseValue == 7 && loaded.EnergyCost.Canonical == 2 && loaded.IsUpgraded);
        Console.WriteLine("PASS game ToSerializable / FromSerializable and upgraded definition round trip");
        loaded.DowngradeInternal();
        Check(!loaded.IsUpgraded && loaded.DynamicVars["E0"].BaseValue == 10 && loaded.DynamicVars["E1"].BaseValue == 5
            && loaded.Keywords.Contains(CardKeyword.Exhaust) && loaded.EnergyCost.Canonical == 2);
        Console.WriteLine("PASS game downgrade preserves per-instance base effects and keywords");

        foreach (var (cost, amount, upgrade) in new[] { (4, 52, 11), (5, 65, 15) })
        {
            var heavy = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
            heavy.DefinitionPayload = Wire.Encode(definition with { SchemaVersion = 2, Cost = cost, Rarity = ForgeRarity.Rare,
                Keywords = [], Effects = [definition.Effects[0] with { Amount = amount, UpgradeAmount = upgrade }] });
            heavy.UpgradeInternal(); heavy.FinalizeUpgradeInternal();
            var restoredHeavy = (NeowGeneratedCard)CardModel.FromSerializable(heavy.ToSerializable());
            Check(restoredHeavy.EnergyCost.Canonical == cost && restoredHeavy.DynamicVars["E0"].BaseValue == amount + upgrade);
            restoredHeavy.DowngradeInternal();
            Check(restoredHeavy.EnergyCost.Canonical == cost && restoredHeavy.DynamicVars["E0"].BaseValue == amount);
        }
        Console.WriteLine("PASS game four/five-cost cards preserve expanded damage and upgrades through native save/downgrade");

        var complexDefinition = new CardDefinition
        {
            SchemaVersion = 2, Name = "余烬之心", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare, Cost = 2,
            Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 1, UpgradeAmount = 1,
                Trigger = EffectTrigger.CardExhausted, Duration = 0, MaxPerTurn = 3 }]
        };
        var engineCard = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        engineCard.DefinitionPayload = Wire.Encode(complexDefinition);
        engineCard.UpgradeInternal(); engineCard.FinalizeUpgradeInternal();
        var restoredEngine = (NeowGeneratedCard)CardModel.FromSerializable(engineCard.ToSerializable());
        Check(restoredEngine.Type == CardType.Power && restoredEngine.Definition.SchemaVersion == 2
            && restoredEngine.DynamicVars["E0"].BaseValue == 2 && restoredEngine.Definition.Effects[0].Duration == 0);
        Console.WriteLine("PASS game v2 Power card type, upgrade and native save round trip");

        var complexAttack = definition with { SchemaVersion = 2, Effects =
        [
            definition.Effects[0] with { Amount = 3, UpgradeAmount = 1, Repeat = 2,
                Condition = EffectCondition.TargetVulnerable, Scaling = EffectScaling.ExhaustSize, ScalingAmount = 1, ScalingCap = 3 },
            definition.Effects[1] with { Trigger = EffectTrigger.NextTurnStart }
        ] };
        var programCard = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        programCard.DefinitionPayload = Wire.Encode(complexAttack);
        programCard.UpgradeInternal(); programCard.FinalizeUpgradeInternal();
        var restoredProgram = (NeowGeneratedCard)CardModel.FromSerializable(programCard.ToSerializable());
        Check(restoredProgram.DynamicVars["E0"].BaseValue == 4 && restoredProgram.DynamicVars["E1"].BaseValue == 7
            && restoredProgram.Definition.Effects[0].Repeat == 2 && restoredProgram.Definition.Effects[0].ScalingCap == 3
            && restoredProgram.Definition.Effects[1].Trigger == EffectTrigger.NextTurnStart);
        Console.WriteLine("PASS game v2 repeat/condition/scaling/delay card native save round trip");

        ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(typeof(GeneratedEffectPower));
        var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable();
        typeof(GeneratedEffectPower).GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(power, [restoredEngine]);
        var powerSnapshot = Wire.Decode<GeneratedPowerSnapshot>(power.RuntimePayload);
        Check(powerSnapshot.SourceUpgraded && powerSnapshot.Amounts[0] == 2 && powerSnapshot.IgnoreArmingPlay
            && powerSnapshot.State.Remaining[0] == -1 && power.InstanceType == PowerInstanceType.Instanced);
        EffectTriggerRuntime.TryConsume(complexDefinition, powerSnapshot.State, 0, EffectTrigger.CardExhausted);
        power.RuntimePayload = Wire.Encode(powerSnapshot with { IgnoreArmingPlay = false });
        var savedPower = SavedProperties.From(power)!;
        var restoredPower = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable();
        savedPower.Fill(restoredPower);
        var restoredSnapshot = Wire.Decode<GeneratedPowerSnapshot>(restoredPower.RuntimePayload);
        Check(restoredPower.RuntimePayload == power.RuntimePayload && restoredSnapshot.State.Activations[0] == 1
            && !restoredSnapshot.IgnoreArmingPlay);
        var powerClone = (GeneratedEffectPower)power.ClonePreservingMutability();
        var snapshotField = typeof(GeneratedEffectPower).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clonedSnapshot = (GeneratedPowerSnapshot)snapshotField.GetValue(powerClone)!;
        EffectTriggerRuntime.TryConsume(complexDefinition, clonedSnapshot.State, 0, EffectTrigger.CardExhausted);
        Check(Wire.Decode<GeneratedPowerSnapshot>(power.RuntimePayload).State.Activations[0] == 1
            && Wire.Decode<GeneratedPowerSnapshot>(powerClone.RuntimePayload).State.Activations[0] == 2);
        foreach (var invalidSnapshot in new[]
        {
            restoredSnapshot with { SchemaVersion = 2 },
            restoredSnapshot with { Amounts = [] },
            restoredSnapshot with { State = restoredSnapshot.State with { Remaining = [2] } },
            restoredSnapshot with { State = restoredSnapshot.State with { Activations = [4] } }
        })
        {
            string before = restoredPower.RuntimePayload;
            try { restoredPower.RuntimePayload = Wire.Encode(invalidSnapshot); throw new Exception("Expected invalid power rejection."); }
            catch (FormatException) { Check(restoredPower.RuntimePayload == before); }
        }
        Console.WriteLine("PASS game instanced Power captured values, native SavedProperties and clone isolation");

        var applyModifiers = typeof(Bootstrap).Assembly.GetType("Forge.Mod.GeneratedRewardModifiers")!
            .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
        var crucible = (SilverCrucible)ModelDb.Relic<SilverCrucible>().ToMutable();
        crucible.TimesUsed = 3; // The native factory has already consumed its final charge.
        var nativeReward = new CardCreationResult(loaded);
        nativeReward.ModifyCard(card, crucible);
        var generatedReward = new CardCreationResult(loaded);
        Func<CardModel, CardModel> cloneCard = model => (CardModel)model.ClonePreservingMutability();
        applyModifiers.Invoke(null, [generatedReward, new[] { nativeReward }, cloneCard]);
        Check(generatedReward.Card.IsUpgraded && generatedReward.Card.DynamicVars["E0"].BaseValue == 13
            && !generatedReward.originalCard.IsUpgraded && generatedReward.ModifyingRelics.Single() == crucible
            && crucible.TimesUsed == 3);
        applyModifiers.Invoke(null, [generatedReward, new[] { nativeReward }, cloneCard]);
        Check(generatedReward.Card.CurrentUpgradeLevel == 1 && crucible.TimesUsed == 3);
        var unmodifiedReward = new CardCreationResult(loaded);
        applyModifiers.Invoke(null, [unmodifiedReward, new[] { new CardCreationResult(card) }, cloneCard]);
        Check(!unmodifiedReward.Card.IsUpgraded); // Randomly upgraded vanilla cards do not imply a relic upgrade.
        Console.WriteLine("PASS generated reward inherits Silver Crucible's third charge without consuming or upgrading twice");

        var starDefinition = new CardDefinition
        {
            SchemaVersion = 3, Name = "星潮", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Uncommon,
            Cost = 1, StarCost = 2, UpgradeStarCost = 1, UpgradeCost = 1,
            Effects = [new() { Kind = EffectKind.Damage, Target = EffectTarget.Enemy, Amount = 8, UpgradeAmount = 3,
                Scaling = EffectScaling.SelfStars, ScalingAmount = 2 },
                new() { Kind = EffectKind.Stars, Target = EffectTarget.Self, Amount = 1 }]
        };
        var starCard = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        _ = starCard.BaseStarCost; // Initialize fallback star cache before changing the definition.
        starCard.DefinitionPayload = Wire.Encode(starDefinition);
        Check(starCard.CanonicalStarCost == 2 && starCard.BaseStarCost == 2 && starCard.EnergyCost.Canonical == 1);
        starCard.UpgradeInternal();
        Check(starCard.DynamicVars["E0"].WasJustUpgraded && !starCard.DynamicVars["E1"].WasJustUpgraded
            && starCard.DynamicVars["E1"].ToHighlightedString(false) == "1"
            && starCard.BaseStarCost == 1 && starCard.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0);
        starCard.FinalizeUpgradeInternal();
        var starLoaded = (NeowGeneratedCard)CardModel.FromSerializable(starCard.ToSerializable());
        Check(starLoaded.BaseStarCost == 1 && starLoaded.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0
            && starLoaded.DynamicVars["E0"].BaseValue == 11 && starLoaded.Definition.Effects[1].Kind == EffectKind.Stars);
        starLoaded.DowngradeInternal();
        Check(starLoaded.BaseStarCost == 2 && starLoaded.EnergyCost.GetWithModifiers(CostModifiers.Local) == 1
            && starLoaded.DynamicVars["E0"].BaseValue == 8);
        Console.WriteLine("PASS native v3 star/energy costs, save/downgrade and unchanged upgrade slot highlighting");

        var harmony = new Harmony("neowscompany.tests");
        typeof(Bootstrap).GetMethod("VerifyCompatibility", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        harmony.PatchAll(typeof(Bootstrap).Assembly);
        var original = typeof(MegaCrit.Sts2.Core.Rewards.CardReward).GetMethod("OnSelect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(Harmony.GetPatchInfo(original)!.Prefixes.Any(p => p.owner == harmony.Id));
        Check(power.PackedIconPath == ModelDb.Power<DexterityPower>().PackedIconPath);
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("PASS Harmony targets and reverse-patch installation against real game DLL");
        Console.WriteLine("PASS: 11 game API smoke checks (UI/live combat not exercised)");
    }
    private static void Check(bool value) { if (!value) throw new Exception("Game contract assertion failed."); }
}
