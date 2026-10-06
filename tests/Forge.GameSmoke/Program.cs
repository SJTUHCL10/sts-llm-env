using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Forge.Core;
using Forge.Mod;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

string game = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("STS2_GAME_DIR") ?? throw new ArgumentException("Provide game directory.");
string lib = Path.Combine(game, "data_sts2_windows_x86_64");
AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(lib, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(lib, name.Name + ".dll")) : null;
Smoke.Run();

static class Smoke
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run()
    {
        var registry = typeof(CardModel).Assembly.GetTypes().Single(t => t.Name == "AbstractModelSubtypes");
        var native = (IEnumerable<Type>)(AccessTools.Property(registry, "All")?.GetValue(null) ?? AccessTools.Field(registry, "All").GetValue(null))!;
        var types = native.Concat([typeof(NeowGeneratedCard), typeof(GeneratedEffectPower)]).ToArray();
        AccessTools.Field(typeof(ModelDb), "_allAbstractModelSubtypes").SetValue(null, types); ModelDb.Init(types);
        Check(ModelDb.Relic<PrismaticGem>().Id.Entry == "PRISMATIC_GEM");
        foreach (string id in MechanicCatalog.Cards) Check(NativeMechanics.Card(id).IsCanonical);
        foreach (string id in MechanicCatalog.Powers) Check(NativeMechanics.Power(id).IsCanonical);
        foreach (string id in new[] { "strength", "focus" }) foreach (bool negative in new[] { false, true }) Check(NativeMechanics.TemporaryPower(id, negative).IsCanonical);
        Check(ModelDb.Character<Necrobinder>().CardPool.Id.Entry == "NECROBINDER_CARD_POOL" && ModelDb.Character<Defect>().CardPool.Id.Entry == "DEFECT_CARD_POOL");
        Console.WriteLine("PASS native card/power aliases and role pools resolve against v0.111.0");
        var definition = new CardDefinition
        {
            Name = "涅奥的试炼", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Uncommon,
            Forms = [new() { Cost = new() { Energy = 2, Stars = 2 }, Keywords = [ForgeKeyword.Exhaust], Effects = [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = 10 }] },
                     new() { Cost = new() { Energy = 1, Stars = 1 }, Keywords = [ForgeKeyword.Retain], Effects = [new() { Kind = EffectKind.Damage, Target = "all_enemies", Amount = 13 }, new() { Kind = EffectKind.Block, Amount = 7 }] }]
        };
        var card = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        _ = card.EnergyCost; _ = card.DynamicVars; _ = card.Keywords;
        card.DefinitionPayload = Wire.Encode(definition);
        Check(card.EnergyCost.Canonical == 2 && card.BaseStarCost == 2 && card.TargetType == TargetType.AnyEnemy && card.Keywords.Contains(CardKeyword.Exhaust));
        var clone = (NeowGeneratedCard)card.ClonePreservingMutability(); clone.DynamicVars["E0"].BaseValue = 19;
        Check(card.DynamicVars["E0"].BaseValue == 10 && clone.DefinitionPayload == card.DefinitionPayload);
        Check(card.InstanceKey != clone.InstanceKey);
        card.EnergyCost.SetThisCombat(2);
        card.UpgradeInternal(); card.FinalizeUpgradeInternal();
        Check(card.TargetType == TargetType.AllEnemies && card.DynamicVars.Count() == 2 && card.DynamicVars["E0"].BaseValue == 13 && card.DynamicVars["E1"].BaseValue == 7);
        Check(card.EnergyCost.Canonical == 1 && card.BaseStarCost == 1 && !card.Keywords.Contains(CardKeyword.Exhaust) && card.Keywords.Contains(CardKeyword.Retain));
        Check(card.EnergyCost.GetWithModifiers(CostModifiers.Local) == 1);
        ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(typeof(NeowGeneratedCard));
        typeof(ModelIdSerializationCache).GetField("_initialized", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        var restored = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable());
        Check(restored.IsUpgraded && restored.ActiveForm.Immediate.Length == 2 && restored.DynamicVars["E1"].BaseValue == 7 && restored.BaseStarCost == 1);
        Check(restored.InstanceKey == card.InstanceKey);
        restored.DowngradeInternal();
        Check(restored.TargetType == TargetType.AnyEnemy && restored.DynamicVars.Count() == 1 && restored.EnergyCost.Canonical == 2 && restored.BaseStarCost == 2 && restored.Keywords.Contains(CardKeyword.Exhaust));
        Console.WriteLine("PASS native upgrade/save/load/downgrade changes effect shape, target, costs and keywords");
        var crucible = (SilverCrucible)ModelDb.Relic<SilverCrucible>().ToMutable();
        crucible.TimesUsed = 3;
        var nativeReward = new CardCreationResult(restored);
        nativeReward.ModifyCard(card, crucible);
        var generatedReward = new CardCreationResult(restored);
        GeneratedRewardModifiers.Apply(generatedReward, [nativeReward], model => (CardModel)model.ClonePreservingMutability());
        Check(generatedReward.Card.IsUpgraded && generatedReward.Card.DynamicVars["E0"].BaseValue == 13
            && generatedReward.Card.TargetType == TargetType.AllEnemies && !generatedReward.originalCard.IsUpgraded
            && generatedReward.ModifyingRelics.Single() == crucible && crucible.TimesUsed == 3);
        GeneratedRewardModifiers.Apply(generatedReward, [nativeReward], model => (CardModel)model.ClonePreservingMutability());
        Check(generatedReward.Card.CurrentUpgradeLevel == 1 && crucible.TimesUsed == 3);
        var unmodifiedReward = new CardCreationResult(restored);
        GeneratedRewardModifiers.Apply(unmodifiedReward, [new CardCreationResult(card)], model => (CardModel)model.ClonePreservingMutability());
        Check(!unmodifiedReward.Card.IsUpgraded);
        Console.WriteLine("PASS Silver Crucible reward upgrade changes the complete form without consuming another charge");
        var x = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        x.DefinitionPayload = Wire.Encode(definition with { Forms = definition.Forms.Select(f => f with { Cost = new() { Energy = 0, EnergyX = true, Stars = 0, StarsX = true } }).ToArray() });
        x.UpgradeInternal(); x.FinalizeUpgradeInternal();
        Check(x.EnergyCost.CostsX && x.HasStarCostX && ((NeowGeneratedCard)CardModel.FromSerializable(x.ToSerializable())).HasStarCostX);
        Console.WriteLine("PASS native X energy/star cost properties and upgraded serialization");
        var engine = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        engine.DefinitionPayload = Wire.Encode(new CardDefinition
        {
            Name = "余烬之心", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare,
            Forms = new[] { 1, 2 }.Select(n => new CardForm { Cost = new() { Energy = 1 }, Rules = [new() { Trigger = new() { Event = RuleEvent.CardDiscarded, Limit = new() { Count = 1 } }, Effects = [new() { Kind = EffectKind.Draw, Amount = n }, new() { Kind = EffectKind.Block, Amount = n * 2 }] }] }).ToArray()
        });
        engine.UpgradeInternal(); engine.FinalizeUpgradeInternal();
        var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); power.Configure(engine);
        var snapshot = Wire.Decode<GeneratedPowerSnapshot>(power.RuntimePayload);
        Check(snapshot.SourceUpgraded && snapshot.Amounts.SequenceEqual(new decimal[] { 2, 4 }));
        Check(snapshot.SourceInstanceKey == engine.InstanceKey);
        EffectTriggerRuntime.TryConsume(engine.ActiveForm, snapshot.State, 0, RuleEvent.CardDiscarded);
        power.RuntimePayload = Wire.Encode(snapshot);
        ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(typeof(GeneratedEffectPower));
        var loadedPower = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); SavedProperties.From(power)!.Fill(loadedPower);
        Check(Wire.Decode<GeneratedPowerSnapshot>(loadedPower.RuntimePayload).State.Activations[0] == 1);
        var copiedPower = (GeneratedEffectPower)power.ClonePreservingMutability();
        var copiedSnapshot = (GeneratedPowerSnapshot)AccessTools.Field(typeof(GeneratedEffectPower), "_snapshot").GetValue(copiedPower)!;
        EffectTriggerRuntime.BeginTurn(engine.ActiveForm, copiedSnapshot.State);
        Check(Wire.Decode<GeneratedPowerSnapshot>(power.RuntimePayload).State.Activations[0] == 1);
        engine.DowngradeInternal(); Check(Wire.Decode<GeneratedPowerSnapshot>(power.RuntimePayload).Amounts[0] == 2);
        Console.WriteLine("PASS rule instances preserve captured form, grouped counters and clone isolation through native properties");
        CheckCombatContracts();
        var harmony = new Harmony("neowscompany.tests");
        typeof(Bootstrap).GetMethod("VerifyCompatibility", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        harmony.PatchAll(typeof(Bootstrap).Assembly);
        var original = typeof(MegaCrit.Sts2.Core.Rewards.CardReward).GetMethod("OnSelect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(Harmony.GetPatchInfo(original)!.Prefixes.Any(p => p.owner == harmony.Id));
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(CombatHistory), "Add"))!.Prefixes.Any(p => p.owner == harmony.Id));
        Check(power.PackedIconPath == ModelDb.Power<DexterityPower>().PackedIconPath);
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("PASS native Harmony reward/history/description hooks and placeholder power icons");
        Console.WriteLine("PASS game API smoke checks (rendered UI/live combat not exercised)");
    }
    private static void CheckCombatContracts()
    {
        var guard = new Harmony("neowscompany.tests.saves");
        guard.Patch(AccessTools.PropertyGetter(typeof(SaveManager), nameof(SaveManager.Instance)), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipSaveManager)));
        guard.Patch(AccessTools.Method(typeof(Player), "PopulateStartingInventory"), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipStartingInventory)));
        var owner = Player.CreateForNewRun<Regent>(UnlockState.all, 1); var other = Player.CreateForNewRun<Regent>(UnlockState.all, 2);
        var run = RunState.CreateForTest([owner, other], acts: [], seed: "PROTOCOL5");
        var combat = new CombatState(runState: run); combat.AddPlayer(owner); combat.AddPlayer(other); owner.ResetCombatState(); other.ResetCombatState();
        owner.PlayerCombatState!.Stars = 5;
        ((StrengthPower)ModelDb.Power<StrengthPower>().ToMutable()).ApplyInternal(owner.Creature, 2, silent: true);
        ((WeakPower)ModelDb.Power<WeakPower>().ToMutable()).ApplyInternal(owner.Creature, 1, silent: true);
        ((VulnerablePower)ModelDb.Power<VulnerablePower>().ToMutable()).ApplyInternal(other.Creature, 1, silent: true);
        var turnField = AccessTools.Field(typeof(CombatManager), "_turnState"); object? previous = turnField.GetValue(CombatManager.Instance);
        object turn = Activator.CreateInstance(turnField.FieldType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [combat], null)!;
        AccessTools.Property(turn.GetType(), "IsInProgress").SetValue(turn, true); turnField.SetValue(CombatManager.Instance, turn);
        try
        {
            var card = combat.CreateCard<NeowGeneratedCard>(owner);
            var amount = new NumberExpression { Add = [8, new() { Mul = [2, new() { Stat = "stars" }] }] };
            var form = new CardForm { Cost = new() { Energy = 1, Stars = 2 }, Effects = [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = amount }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "星潮", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Common, Forms = [form, form] });
            owner.PlayerCombatState.Hand.AddInternal(card);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, other.Creature, card.DynamicVars);
            Check(card.DynamicVars["E0"].PreviewValue == 18 && card.DynamicVars["E0"].BaseValue == 0 && owner.PlayerCombatState.Stars == 5);
            Console.WriteLine("PASS expression preview includes payment/Strength/Weak/Vulnerable without mutating state or base values");
            var effect = new CardEffect { Kind = EffectKind.Damage, Target = "all_enemies", Amount = 7 };
            var ruleForm = new CardForm { Cost = new() { Energy = 1 }, Rules = [new() { Trigger = new() { Event = RuleEvent.TurnEnd }, Effects = [effect] }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "星幕", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare, Forms = [ruleForm, ruleForm] });
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, other.Creature, card.DynamicVars); Check(card.DynamicVars["E0"].PreviewValue == 7);
            var baseForm = ruleForm with { Rules = [new() { Trigger = new() { Event = RuleEvent.CardPlayed, Filter = new() { Type = "skill" }, Occurrence = new() { First = 1 } }, Effects = [new() { Kind = EffectKind.Block, Amount = 1 }] }] };
            var play = new CardPlay { Card = card, Player = owner, Target = null, Resources = new() { EnergySpent = 1, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, ResultPile = PileType.Discard, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
            var entry = new CardPlayFinishedEntry(play, combat.RoundNumber, combat.CurrentSide, CombatManager.Instance.History, combat.Players);
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "技能", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Common, Forms = [form with { Effects = [new() { Kind = EffectKind.Block, Amount = 1 }] }, form with { Effects = [new() { Kind = EffectKind.Block, Amount = 2 }] }] });
            GeneratedEventFacts.Capture(entry); AccessTools.Method(typeof(CombatHistory), "Add").Invoke(CombatManager.Instance.History, [combat, entry]);
            var trigger = baseForm.Listeners[0].Trigger with { Filter = new() { Type = "skill", Upgraded = false, Cost = 1 } };
            Check(GeneratedEventFacts.Ordinal(trigger, owner.Creature, combat) == 1);
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            Check(GeneratedEventFacts.Ordinal(trigger, owner.Creature, combat) == 1);
            Check(GeneratedEventFacts.MatchesCurrent(trigger, card, owner.Creature, combat));
            Check(NativeMechanics.Matches(NativeMechanics.Card("soul"), new() { Id = "soul", Type = "skill" }));
            Console.WriteLine("PASS native event ordinals include earlier events and snapshot filter properties before later upgrades");
        }
        finally { CombatManager.Instance.History.Clear(); turnField.SetValue(CombatManager.Instance, previous); guard.UnpatchAll(guard.Id); }
    }
    private static bool SkipSaveManager(ref SaveManager? __result) { __result = null; return false; }
    private static bool SkipStartingInventory() => false;
    private static void Check(bool value) { if (!value) throw new Exception("Game contract assertion failed."); }
}
