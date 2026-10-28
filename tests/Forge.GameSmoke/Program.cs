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
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.TestSupport;

try
{
    string game = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("STS2_GAME_DIR") ?? throw new ArgumentException("Provide game directory.");
    string lib = Path.Combine(game, "data_sts2_windows_x86_64");
    AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(lib, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(lib, name.Name + ".dll")) : null;
    // Keep native types out of the entry-point JIT until the game resolver is registered.
    Assembly.GetExecutingAssembly().GetType("Smoke")!.GetMethod("Run")!.Invoke(null, null);
}
catch (Exception ex)
{
    // A console test failure should retain its stack trace without invoking a Windows crash dialog.
    Console.Error.WriteLine("FAIL game API smoke checks: " + ex);
    Environment.ExitCode = 1;
}

static class Smoke
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run()
    {
        var registry = typeof(CardModel).Assembly.GetTypes().Single(t => t.Name == "AbstractModelSubtypes");
        var native = (IEnumerable<Type>)(AccessTools.Property(registry, "All")?.GetValue(null) ?? AccessTools.Field(registry, "All").GetValue(null))!;
        var types = native.Concat([typeof(NeowGeneratedCard), typeof(GeneratedEffectPower)]).ToArray();
        AccessTools.Field(typeof(ModelDb), "_allAbstractModelSubtypes").SetValue(null, types); ModelDb.Init(types);
        CheckNativeConversionContracts();
        Check(ModelDb.Relic<PrismaticGem>().Id.Entry == "PRISMATIC_GEM");
        foreach (string id in MechanicCatalog.Cards) Check(NativeMechanics.Card(id).IsCanonical);
        foreach (string id in MechanicCatalog.Powers) Check(NativeMechanics.Power(id).IsCanonical);
        foreach (var orb in new MegaCrit.Sts2.Core.Models.OrbModel[]
        {
            ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.LightningOrb>(), ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.FrostOrb>(),
            ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.DarkOrb>(), ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.PlasmaOrb>(),
            ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.GlassOrb>()
        }) Check(orb.IsCanonical);
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
        _ = ModelDb.Card<NeowGeneratedCard>().Pool; _ = card.Pool;
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
        x.DefinitionPayload = Wire.Encode(definition with { Forms = definition.Forms.Select(f => f with { Cost = new() { EnergyX = true, StarsX = true } }).ToArray() });
        x.UpgradeInternal(); x.FinalizeUpgradeInternal();
        Check(x.EnergyCost.CostsX && x.HasStarCostX && x.BaseStarCost == 0 && ((NeowGeneratedCard)CardModel.FromSerializable(x.ToSerializable())).HasStarCostX);
        x.DefinitionPayload = Wire.Encode(definition with { Forms = [definition.Forms[0] with { Cost = new() { Energy = 2, StarsX = true } },
            definition.Forms[1] with { Cost = new() { Energy = 7, EnergyX = true, Stars = 9, StarsX = true } }] });
        x.DowngradeInternal(); Check(!x.EnergyCost.CostsX && x.EnergyCost.Canonical == 2 && x.CanonicalStarCost == 0);
        x.UpgradeInternal(); x.FinalizeUpgradeInternal();
        Check(x.EnergyCost.CostsX && x.EnergyCost.Canonical == 0 && x.CanonicalStarCost == 0);
        x = (NeowGeneratedCard)CardModel.FromSerializable(x.ToSerializable());
        Check(x.EnergyCost.CostsX && x.HasStarCostX);
        x.DowngradeInternal(); Check(!x.EnergyCost.CostsX && x.EnergyCost.Canonical == 2 && x.HasStarCostX);
        Console.WriteLine("PASS native X energy/star cost properties and upgraded serialization");
        var engine = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
        engine.DefinitionPayload = Wire.Encode(new CardDefinition
        {
            Name = "余烬之心", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare,
            Forms = new[] { 1, 2 }.Select(n => new CardForm { Cost = new() { Energy = 1 }, Keywords = [ForgeKeyword.Retain, ForgeKeyword.Sly], Rules = [new() { Trigger = new() { Event = RuleEvent.CardDiscarded, Limit = new() { Count = 1 } }, Effects = [new() { Kind = EffectKind.Draw, Amount = n }, new() { Kind = EffectKind.Block, Amount = n * 2 }] }] }).ToArray()
        });
        engine.UpgradeInternal(); engine.FinalizeUpgradeInternal();
        Check(engine.Keywords.Contains(CardKeyword.Retain) && engine.Keywords.Contains(CardKeyword.Sly));
        var restoredEngine = (NeowGeneratedCard)CardModel.FromSerializable(engine.ToSerializable());
        Check(restoredEngine.Keywords.Contains(CardKeyword.Retain) && restoredEngine.Keywords.Contains(CardKeyword.Sly));
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
        foreach (string property in new[] { nameof(PowerModel.Icon), nameof(PowerModel.BigIcon) })
            Check(Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(PowerModel), property))!.Prefixes.Any(p => p.owner == harmony.Id));
        foreach (bool large in new[] { false, true })
        {
            byte[] png = GeneratedPowerIcons.ReadPng(large);
            Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            int size = large ? 256 : 64;
            Check(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) == size);
            Check(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) == size);
            Check(png[24] == 8 && png[25] == 6); // 8-bit RGBA, without initializing Godot.
        }
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("PASS native Harmony reward/history/description hooks and embedded 64/256px power icon contracts");
        Console.WriteLine("PASS game API smoke checks (rendered UI/live combat not exercised)");
    }
    private static void CheckPlanningCapture()
    {
        var player = Player.CreateForNewRun<Regent>(UnlockState.all, 71);
        var run = RunState.CreateForTest([player], acts: [ModelDb.Act<Glory>()], seed: "PLANNING");
        run.Map.StartingMapPoint.AddChildPoint(new MapPoint(1, 1) { PointType = MapPointType.Elite });
        run.Act.SetBossEncounter(ModelDb.Encounter<AeonglassBoss>());
        run.Act.SetSecondBossEncounter(ModelDb.Encounter<QueenBoss>());
        string rng = Wire.Encode(run.Rng.ToSerializable());
        string rooms = Wire.Encode(run.Act.ToSave());
        var capture = new StateCapture();
        var planning = capture.Detach(StateCapture.CapturePlanning(run));
        Check(planning.GetProperty("bosses").GetArrayLength() == 2);
        Check(planning.GetProperty("bosses")[0].GetProperty("id").GetString() == "AEONGLASS_BOSS");
        var hp = planning.GetProperty("bosses")[0].GetProperty("monsters")[0].GetProperty("initial_hp");
        Check(hp.GetProperty("min").GetInt32() == ModelDb.Monster<Aeonglass>().MinInitialHp);
        Check(hp.GetProperty("max").GetInt32() == ModelDb.Monster<Aeonglass>().MaxInitialHp);
        Check(planning.GetProperty("elite_pool").EnumerateArray().Any(e => e.GetProperty("id").GetString() == "MECHA_KNIGHT_ELITE"));
        Check(!planning.TryGetProperty("map", out _));
        Check(Wire.Encode(run.Rng.ToSerializable()) == rng && Wire.Encode(run.Act.ToSave()) == rooms);
        foreach (var encounter in new EncounterModel[] { ModelDb.Encounter<AeonglassBoss>(), ModelDb.Encounter<QueenBoss>(), ModelDb.Encounter<MechaKnightElite>(), ModelDb.Encounter<AxebotsNormal>() })
        {
            run.PushRoom(new CombatRoom(encounter.ToMutable(), run));
            try
            {
                var inRoom = capture.Detach(StateCapture.CapturePlanning(run));
                Check(inRoom.GetProperty("bosses").GetArrayLength() == (encounter.RoomType == RoomType.Boss ? 0 : 2));
                Check(inRoom.GetProperty("elite_pool").GetRawText() == planning.GetProperty("elite_pool").GetRawText());
                Check(Wire.Encode(run.Rng.ToSerializable()) == rng && Wire.Encode(run.Act.ToSave()) == rooms);
            }
            finally { run.PopCurrentRoom(); }
        }
        Check(capture.Detach(StateCapture.CapturePlanning(run)).GetProperty("bosses").GetArrayLength() == 2);
        foreach (var act in new ActModel[] { ModelDb.Act<Underdocks>(), ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>() })
            foreach (var encounter in act.AllBossEncounters.Concat(act.AllEliteEncounters))
            {
                foreach (var monster in encounter.AllPossibleMonsters)
                    Check(GameKnowledge.MonsterMechanics(monster.Id.Entry) is not null);
                if (GameKnowledge.InitialLineup(encounter.Id.Entry) is { } lineup)
                    foreach (var member in lineup)
                        Check(member.Count > 0 && encounter.AllPossibleMonsters.Any(m => m.Id.Entry == member.Id));
            }
        var projected = ObservationProjector.Project(new GenerationContext { CombatKey = "planning", Run = System.Text.Json.JsonSerializer.SerializeToElement(new { planning }),
            State = System.Text.Json.JsonSerializer.SerializeToElement(new { }), RecentEvents = [] });
        var queen = projected.GetProperty("planning").GetProperty("bosses")[1];
        Check(queen.GetProperty("initial_lineup").GetArrayLength() == 2);
        Check(queen.GetProperty("monsters").GetArrayLength() == 2);
        CheckPlanningHpAscension();
        var current = new MapPoint(1, 1) { PointType = MapPointType.Monster };
        var elite = new MapPoint(2, 1) { PointType = MapPointType.Elite };
        var unknown = new MapPoint(2, 2) { PointType = MapPointType.Unknown };
        var boss = new MapPoint(3, 1) { PointType = MapPointType.Boss };
        current.AddChildPoint(elite); current.AddChildPoint(unknown);
        elite.AddChildPoint(boss); unknown.AddChildPoint(boss);
        Check(StateCapture.CountFutureElites(current) == 1 && StateCapture.CountFutureElites(boss) == 0);
        Check(StateCapture.CountFutureElites(elite) == 0 && current.Children.Count == 2);
        var alternative = new MapPoint(2, 3) { PointType = MapPointType.Elite };
        current.AddChildPoint(alternative); alternative.AddChildPoint(boss);
        Check(StateCapture.CountFutureElites(current) == 1); // Separate branches are not two fights.
        CheckPlanningElitePool();
        Console.WriteLine("PASS planning captures assigned bosses and possible future elites without exposing maps or consuming encounters/RNG");
    }

    private static int _planningAscension;

    private static void CheckPlanningElitePool()
    {
        var player = Player.CreateForNewRun<Regent>(UnlockState.all, 73);
        var run = RunState.CreateForTest([player], acts: [ModelDb.Act<Underdocks>(),
            ModelDb.Act<Overgrowth>(), ModelDb.Act<Glory>()], seed: "PLANNING_ELITES");
        var capture = new StateCapture();
        // Keep the native map object, replacing only the test start's future edges.
        run.ClearVisitedMapCoordsDebug();
        var start = run.Map!.StartingMapPoint;
        foreach (var child in start.Children.ToArray()) start.RemoveChildPoint(child);
        var first = new MapPoint(1, 1) { PointType = MapPointType.Elite };
        var second = new MapPoint(2, 1) { PointType = MapPointType.Elite };
        start.AddChildPoint(first);

        for (int actIndex = 0; actIndex < 3; actIndex++)
        {
            run.CurrentActIndex = actIndex;
            var act = run.Act;
            act.GenerateRooms(run.Rng.UpFront, UnlockState.all, false);
            var saved = act.ToSave();
            var queue = saved.SerializableRooms.EliteEncounterIds;
            var pool = act.AllEliteEncounters.Select(e => e.Id).ToHashSet();
            Check(pool.Count == 3 && queue.Count == 15);
            for (int i = 0; i < queue.Count; i += pool.Count)
                Check(queue.Skip(i).Take(pool.Count).ToHashSet().SetEquals(pool));
            for (int consumed = 0; consumed <= 7; consumed++)
            {
                saved.SerializableRooms.EliteEncountersVisited = consumed;
                var restored = ActModel.FromSave(saved);
                string rng = Wire.Encode(run.Rng.ToSerializable()), rooms = Wire.Encode(restored.ToSave());
                var candidates = StateCapture.PossibleEliteEncounters(restored, 1).Select(e => e.Id).ToHashSet();
                Check(candidates.Contains(queue[consumed]));
                Check(StateCapture.PossibleEliteEncounters(restored, 0).Count() == 0);
                // Inside each bag, previously drawn types are excluded. At refill,
                // the last type is excluded when a non-conflicting choice exists.
                int used = consumed % pool.Count;
                if (used > 0)
                    Check(!candidates.Overlaps(queue.Skip(consumed - used).Take(used)));
                else if (consumed > 0)
                    Check(!candidates.Contains(queue[consumed - 1]));
                var acrossRefill = StateCapture.PossibleEliteEncounters(restored, 4).Select(e => e.Id).ToHashSet();
                Check(acrossRefill.SetEquals(pool));
                if (used == 2)
                {
                    Check(candidates.Count == 1);
                    Check(StateCapture.PossibleEliteEncounters(restored, 2).Select(e => e.Id).ToHashSet().SetEquals(pool));
                }
                Check(Wire.Encode(run.Rng.ToSerializable()) == rng && Wire.Encode(restored.ToSave()) == rooms);
                var reloaded = ActModel.FromSave(restored.ToSave());
                Check(StateCapture.PossibleEliteEncounters(reloaded, 1).Select(e => e.Id).ToHashSet().SetEquals(candidates));
                // Unseen pre-rolls must not affect the possible-candidate projection.
                var hidden = restored.ToSave();
                hidden.SerializableRooms.EliteEncounterIds.Reverse(consumed, queue.Count - consumed);
                Check(StateCapture.PossibleEliteEncounters(ActModel.FromSave(hidden), 1).Select(e => e.Id).ToHashSet().SetEquals(candidates));
            }

            // Entering two native map elites advances the consumed prefix; capture
            // must combine it with the reachable fight count, not the whole act pool.
            act.MarkRoomVisited(RoomType.Elite); act.MarkRoomVisited(RoomType.Elite);
            string beforeRng = Wire.Encode(run.Rng.ToSerializable()), beforeRooms = Wire.Encode(act.ToSave());
            var planning = capture.Detach(StateCapture.CapturePlanning(run));
            Check(planning.GetProperty("elite_act_id").GetString() == act.Id.Entry);
            Check(planning.GetProperty("elite_pool").GetArrayLength() == 1 && !planning.TryGetProperty("map", out _));
            Check(planning.GetProperty("elite_pool")[0].GetProperty("id").GetString() == queue[2].Entry);
            first.AddChildPoint(second);
            Check(capture.Detach(StateCapture.CapturePlanning(run)).GetProperty("elite_pool").GetArrayLength() == 3);
            first.RemoveChildPoint(second);
            start.RemoveChildPoint(first); // No reachable elite: advance the pool only.
            planning = capture.Detach(StateCapture.CapturePlanning(run));
            Check(planning.GetProperty("act_id").GetString() == act.Id.Entry);
            if (actIndex < 2)
            {
                var next = run.Acts[actIndex + 1];
                Check(planning.GetProperty("elite_act_id").GetString() == next.Id.Entry);
                Check(planning.GetProperty("elite_pool").EnumerateArray().Select(e => e.GetProperty("id").GetString())
                    .ToHashSet().SetEquals(next.AllEliteEncounters.Select(e => e.Id.Entry)));
            }
            else
                Check(planning.GetProperty("elite_pool").GetArrayLength() == 0);
            Check(Wire.Encode(run.Rng.ToSerializable()) == beforeRng && Wire.Encode(act.ToSave()) == beforeRooms);
            start.AddChildPoint(first);
            first.AddChildPoint(second);
            Check(StateCapture.CountFutureElites(start) == 2);
            first.RemoveChildPoint(second);
        }
        // A missing map is unknown, rather than evidence that this act has no elites.
        run.Map = null!;
        Check(capture.Detach(StateCapture.CapturePlanning(run)).GetProperty("elite_pool").GetArrayLength() == 3);
        Console.WriteLine("PASS native elite bags, reachable fight counts, next-act/final-act pools, reload and read-only capture");
    }

    private static bool PlanningAscension(MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel level, ref bool __result)
    {
        __result = (int)level <= _planningAscension;
        return false;
    }

    private static void CheckPlanningHpAscension()
    {
        var player = Player.CreateForNewRun<Regent>(UnlockState.all, 72);
        var run = RunState.CreateForTest([player], acts: [ModelDb.Act<Overgrowth>()], seed: "PLANNING_HP");
        run.Map.StartingMapPoint.AddChildPoint(new MapPoint(1, 1) { PointType = MapPointType.Elite });
        var guard = new Harmony("forge.smoke.planning-hp");
        guard.Patch(AccessTools.Method(typeof(RunManager), "HasAscension"), prefix: new HarmonyMethod(typeof(Smoke), nameof(PlanningAscension)));
        try
        {
            run.Act.SetBossEncounter(ModelDb.Encounter<SoulFyshBoss>());
            foreach (int ascension in new[] { 0, 7, 8, 10 })
            {
                _planningAscension = ascension;
                string rng = Wire.Encode(run.Rng.ToSerializable()), rooms = Wire.Encode(run.Act.ToSave());
                var planning = new StateCapture().Detach(StateCapture.CapturePlanning(run));
                var hp = planning.GetProperty("bosses")[0].GetProperty("monsters")[0].GetProperty("initial_hp");
                Check(hp.GetProperty("min").GetInt32() == (ascension >= 8 ? 221 : 211));
                Check(hp.GetProperty("max").GetInt32() == hp.GetProperty("min").GetInt32());
                var parasite = planning.GetProperty("elite_pool").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "PHROG_PARASITE_ELITE")
                    .GetProperty("monsters").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "PHROG_PARASITE").GetProperty("initial_hp");
                Check(parasite.GetProperty("min").GetInt32() == (ascension >= 8 ? 66 : 61));
                Check(parasite.GetProperty("max").GetInt32() == (ascension >= 8 ? 68 : 64));
                Check(Wire.Encode(run.Rng.ToSerializable()) == rng && Wire.Encode(run.Act.ToSave()) == rooms);
            }
        }
        finally { guard.UnpatchAll(guard.Id); }
        Console.WriteLine("PASS native planning HP ranges follow ascension 8 without rolling HP or changing encounters/RNG");
    }

    private static void CheckCombatRewardEligibility(Player player)
    {
        var run = RunState.CreateForTest([player], acts: [ModelDb.Act<Underdocks>(),
            ModelDb.Act<Overgrowth>(), ModelDb.Act<Glory>()], seed: "REWARDS");
        var bosses = new EncounterModel[] { ModelDb.Encounter<AeonglassBoss>().ToMutable(), ModelDb.Encounter<QueenBoss>().ToMutable() };
        foreach (int act in new[] { 0, 1, 2 })
        {
            run.CurrentActIndex = act;
            foreach (var boss in bosses)
            {
                var state = new CombatState(boss, run);
                Check(ForgeRuntime.HasCombatCardReward(state) == (act < 2));
                Check(new StateCapture().Detach(StateCapture.CaptureEncounter(state, run)).GetProperty("type").GetString() == "boss");
            }
            Check(ForgeRuntime.HasCombatCardReward(new CombatState(ModelDb.Encounter<AxebotsNormal>().ToMutable(), run)));
            Check(ForgeRuntime.HasCombatCardReward(new CombatState(ModelDb.Encounter<MechaKnightElite>().ToMutable(), run)));
        }
        // Native logic follows the actual act list, including shorter custom runs; no floor dependency.
        var shortRun = RunState.CreateForTest([Player.CreateForNewRun<Regent>(UnlockState.all, 4)],
            acts: [ModelDb.Act<Underdocks>()], seed: "SHORT");
        Check(!ForgeRuntime.HasCombatCardReward(new CombatState(bosses[0], shortRun)));
        foreach (var (encounter, type) in new (EncounterModel, string)[] { (ModelDb.Encounter<AxebotsNormal>().ToMutable(), "monster"), (ModelDb.Encounter<MechaKnightElite>().ToMutable(), "elite") })
        {
            var state = new CombatState(encounter, run);
            var capture = new StateCapture();
            Check(capture.Detach(StateCapture.CaptureEncounter(state, run)).GetProperty("type").GetString() == type);
            run.PushRoom(new EventRoom(ModelDb.Event<WoodCarvings>()));
            run.PushRoom(new CombatRoom(state));
            var info = capture.Detach(StateCapture.CaptureEncounter(state, run));
            Check(info.GetProperty("source").GetString() == "event" && info.GetProperty("event_id").GetString() == "WOOD_CARVINGS");
            run.PopCurrentRoom(); run.PopCurrentRoom();
            Check(capture.Detach(StateCapture.CaptureEncounter(state, run)).GetProperty("source").GetString() == "map");
        }
        Console.WriteLine("PASS final-act bosses skip generation, earlier bosses and normal/elite combats remain eligible");
    }

    private static void CheckCombatContracts()
    {
        // Use native localization/formatters with an in-memory table; no Godot assets or save initialization.
        var localization = (LocManager)RuntimeHelpers.GetUninitializedObject(typeof(LocManager));
        AccessTools.Field(typeof(LocManager), "_tables").SetValue(localization,
            new Dictionary<string, LocTable> { ["cards"] = new("cards", new()) });
        AccessTools.PropertySetter(typeof(LocManager), nameof(LocManager.Language)).Invoke(localization, ["zhs"]);
        AccessTools.PropertySetter(typeof(LocManager), nameof(LocManager.CultureInfo)).Invoke(localization, [System.Globalization.CultureInfo.InvariantCulture]);
        AccessTools.Method(typeof(LocManager), "LoadLocFormatters").Invoke(localization, null);
        AccessTools.PropertySetter(typeof(LocManager), nameof(LocManager.Instance)).Invoke(null, [localization]);
        var guard = new Harmony("neowscompany.tests.saves");
        guard.Patch(AccessTools.PropertyGetter(typeof(SaveManager), nameof(SaveManager.Instance)), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipSaveManager)));
        guard.Patch(AccessTools.Method(typeof(Player), "PopulateStartingInventory"), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipStartingInventory)));
        guard.Patch(AccessTools.PropertyGetter(typeof(CardModel), nameof(CardModel.Description)),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(GeneratedDescriptionPatch), "Postfix")));
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
            Check(card.GetDescriptionForPile(PileType.Hand, other.Creature).Contains("18"));
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
            Check(card.DynamicVars["E0"].PreviewValue == 12 && card.GetDescriptionForPile(PileType.Hand).Contains("12"));
            var loaded = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable()); loaded.Owner = owner;
            owner.PlayerCombatState.Hand.RemoveInternal(card); owner.PlayerCombatState.Hand.AddInternal(loaded);
            loaded.UpdateDynamicVarPreview(CardPreviewMode.Normal, other.Creature, loaded.DynamicVars);
            Check(loaded.GetDescriptionForPile(PileType.Hand, other.Creature).Contains("18") && loaded.DynamicVars["E0"].BaseValue == 0);
            owner.PlayerCombatState.Hand.RemoveInternal(loaded); owner.PlayerCombatState.Hand.AddInternal(card);
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, other.Creature, card.DynamicVars);
            Check(card.GetDescriptionForPile(PileType.Hand, other.Creature).Contains("18"));
            card.DowngradeInternal();
            var xForm = form with { Cost = new() { StarsX = true }, Effects = [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = new() { Mul = [5, new() { Stat = "paid_stars" }] } }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "碎星", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Uncommon, Forms = [xForm, xForm] });
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, other.Creature, card.DynamicVars);
            Check(card.DynamicVars["E0"].PreviewValue == 30.375m && card.GetDescriptionForPile(PileType.Hand, other.Creature).Contains("(5 × X)") && owner.PlayerCombatState.Stars == 5);
            owner.PlayerCombatState.Hand.RemoveInternal(card); owner.PlayerCombatState.DrawPile.AddInternal(card);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
            Check(card.RuntimeDescriptionText(true) == "造成(5 × X)点伤害。");
            owner.PlayerCombatState.DrawPile.RemoveInternal(card); owner.PlayerCombatState.Hand.AddInternal(card);
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            Check(card.GetDescriptionForPile(PileType.Hand, other.Creature).Contains("(5 × X)"));
            var savedX = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable()); savedX.Owner = owner;
            Check(savedX.RuntimeDescriptionText(true) == "造成(5 × X)点伤害。");
            card.DowngradeInternal();
            var energyX = xForm with { Cost = new() { EnergyX = true }, Effects = [new() { Kind = EffectKind.Draw, Amount = new() { Stat = "paid_energy" } }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "蓄势", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Common, Forms = [energyX, energyX] });
            Check(card.RuntimeDescriptionText(true) == "抽X张牌。");
            var slots = new CardForm { Cost = new(), Effects = [new() { Kind = EffectKind.OrbSlots, Amount = -1 }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "球槽压缩", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Uncommon,
                Forms = [slots, slots with { Effects = [new() { Kind = EffectKind.OrbSlots, Amount = -2 }] }] });
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
            string slotText = card.GetDescriptionForPile(PileType.Hand);
            Check(slotText.Contains("失去") && slotText.Contains("充能球栏位") && !slotText.Contains("-1"));
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
            slotText = card.GetDescriptionForPile(PileType.Hand);
            Check(slotText.Contains("失去") && slotText.Contains("2") && !slotText.Contains("-2"));
            var subtraction = slots with { Effects = [new() { Kind = EffectKind.OrbSlots, Amount = new() { Sub = [2, new() { Stat = "stars" }] } }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "动态栏位", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Uncommon, Forms = [subtraction, subtraction] });
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
            slotText = card.GetDescriptionForPile(PileType.Hand);
            Check(card.DynamicVars["E0"].PreviewValue == -3 && slotText.Contains("失去") && slotText.Contains("3") && !slotText.Contains("-3"));
            var savedSlots = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable());
            Check(savedSlots.DefinitionPayload == card.DefinitionPayload);
            Console.WriteLine("PASS signed orb-slot wording and subtraction preview survive upgrade and native serialization");
            Console.WriteLine("PASS expression preview includes payment/Strength/Weak/Vulnerable without mutating state or base values");
            Console.WriteLine("PASS X-energy/X-star descriptions retain symbols in hand/draw piles and through upgrade/save/load");
            CheckBlockContracts(card, owner, guard);
            CheckTextContracts();
            CheckOstyContracts(card, owner, other, combat);
            CheckFatalContracts(card, owner, combat);
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
            CheckCombatRewardEligibility(Player.CreateForNewRun<Regent>(UnlockState.all, 3));
            CheckPlanningCapture();
            CheckFirstCombatTiming();
            CheckExtendedEventContracts(card, owner, combat);
            CheckAnySelectionContracts(owner, combat);
            CheckQuantityAndDecimalContracts(owner, other, combat);
        }
        finally { CombatManager.Instance.History.Clear(); turnField.SetValue(CombatManager.Instance, previous); guard.UnpatchAll(guard.Id); }
    }
    private static void CheckExtendedEventContracts(NeowGeneratedCard card, Player owner, CombatState combat)
    {
        var choice = new BlockingPlayerChoiceContext();
        var enemy = combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, null);
        var second = combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, null);
        combat.AddCreature(enemy); combat.AddCreature(second);
        enemy.SetMaxHpInternal(50); enemy.SetCurrentHpInternal(50);
        second.SetMaxHpInternal(50); second.SetCurrentHpInternal(50);
        var sourceCard = (CardModel)NativeMechanics.Card("shiv").ToMutable(); sourceCard.Owner = owner;
        var listeners = new List<GeneratedEffectPower>();
        GeneratedEffectPower Listen(EffectTrigger trigger, EffectCondition? condition = null)
        {
            var form = new CardForm { Cost = new(), Rules = [new() { Trigger = trigger, Condition = condition,
                Effects = [new() { Kind = EffectKind.GainEnergy, Amount = 1, Condition = new() { Op = Comparison.Eq, Left = 0, Right = 1 } }] }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "事件契约", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare, Forms = [form, form] });
            var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); power.Configure(card);
            power.ApplyInternal(owner.Creature, 1, silent: true); listeners.Add(power); return power;
        }
        int Activations(GeneratedEffectPower power) => Wire.Decode<GeneratedPowerSnapshot>(power.RuntimePayload).State.Activations[0];
        EffectCondition Eq(string stat, decimal amount) => new() { Op = Comparison.Eq, Left = new() { Stat = stat }, Right = amount };
        try
        {
            var noDealer = new GeneratedRuleEvent(RuleEvent.DamageGiven) { Subject = enemy, Target = enemy, Targets = [enemy] };
            var ownerWithoutPet = combat.Players.First(p => p != owner).Creature;
            Check(ownerWithoutPet.Player!.Osty is null && !noDealer.Matches(new() { Event = RuleEvent.DamageGiven }, ownerWithoutPet, noDealer.Facts(ownerWithoutPet)));
            var block = Listen(new() { Event = RuleEvent.BlockGained, Filter = new() { Id = "shiv" }, Where = new() { Actor = "self", Target = "self", Source = "self", SourceKind = "card" } }, Eq("event_amount", 3.5m));
            block.AfterBlockGained(owner.Creature, 0, ValueProp.Unpowered, sourceCard).GetAwaiter().GetResult();
            block.AfterBlockGained(owner.Creature, 3.5m, ValueProp.Unpowered, null).GetAwaiter().GetResult();
            Check(Activations(block) == 0);
            block.AfterBlockGained(owner.Creature, 3.5m, ValueProp.Unpowered, sourceCard).GetAwaiter().GetResult();
            Check(Activations(block) == 1);
            var damage = Listen(new() { Event = RuleEvent.DamageGiven, Filter = new() { Type = "attack", Tag = "shiv", MinCost = 0, MaxCost = 0 }, Where = new() { Actor = "self", Target = "enemy", Attack = true } }, new()
            {
                All = [Eq("event_amount", 7), Eq("event_total_damage", 7), Eq("event_unblocked_damage", 4),
                    new() { All = [Eq("event_target_count", 1), new() { Op = Comparison.Eq, Left = new() { Stat = "hp", Of = "event.subject" }, Right = 50 }] }]
            });
            var hit = new DamageResult(enemy, ValueProp.Move) { BlockedDamage = 3, UnblockedDamage = 4 };
            damage.AfterDamageGiven(choice, owner.Creature, hit, ValueProp.Unpowered, enemy, sourceCard).GetAwaiter().GetResult();
            damage.AfterDamageGiven(choice, enemy, hit, ValueProp.Move, enemy, sourceCard).GetAwaiter().GetResult();
            Check(Activations(damage) == 0);
            damage.AfterDamageGiven(choice, owner.Creature, hit, ValueProp.Move, enemy, sourceCard).GetAwaiter().GetResult();
            damage.AfterDamageGiven(choice, owner.Creature, new(second, ValueProp.Move) { UnblockedDamage = 7 }, ValueProp.Move, second, sourceCard).GetAwaiter().GetResult();
            Check(Activations(damage) == 1); // The per-hit unblocked amount differs on the second target.
            Check(damage.RuntimeDescriptionText(true).Contains("每当你对敌人造成攻击伤害时"));
            Check(!damage.RuntimeDescriptionText(true).Contains("执行者为"));
            Check(typeof(GeneratedEffectPower).GetMethod("AfterAttack")!.DeclaringType != typeof(GeneratedEffectPower));
            var repeated = Listen(new() { Event = RuleEvent.DamageGiven, Where = new() { Attack = true } }, Eq("event_amount", 7));
            for (int i = 0; i < 2; i++)
                foreach (var target in new[] { enemy, second })
                    repeated.AfterDamageGiven(choice, owner.Creature, new(target, ValueProp.Move) { UnblockedDamage = 7 }, ValueProp.Move, target, sourceCard).GetAwaiter().GetResult();
            Check(Activations(repeated) == 4);
            repeated.AfterDamageGiven(choice, owner.Creature, new(enemy, ValueProp.Move) { BlockedDamage = 7 }, ValueProp.Move, enemy, sourceCard).GetAwaiter().GetResult();
            Check(Activations(repeated) == 5); // Fully blocked hits still produce damage events.
            var strength = (StrengthPower)ModelDb.Power<StrengthPower>().ToMutable(); strength.ApplyInternal(enemy, -2, silent: true);
            var changed = Listen(new() { Event = RuleEvent.PowerAmountChanged, Where = new() { Power = "strength", PowerType = "debuff", Temporary = false, Target = "enemy", Actor = "self" } }, Eq("event_amount", -2));
            changed.AfterPowerAmountChanged(choice, strength, -2, owner.Creature, sourceCard).GetAwaiter().GetResult();
            changed.AfterPowerAmountChanged(choice, strength, 2, owner.Creature, sourceCard).GetAwaiter().GetResult();
            Check(Activations(changed) == 1);
            var forge = Listen(new() { Event = RuleEvent.Forged, Where = new() { SourceKind = "power", Actor = "self" } }, Eq("event_amount", 4.5m));
            forge.AfterForge(4.5m, owner, sourceCard).GetAwaiter().GetResult();
            forge.AfterForge(4.5m, owner, strength).GetAwaiter().GetResult();
            forge.AfterForge(0, owner, strength).GetAwaiter().GetResult();
            Check(Activations(forge) == 1);
            foreach (var kind in new[] { RuleEvent.StarsGained, RuleEvent.StarsSpent })
            {
                var stars = Listen(new() { Event = kind }, Eq("event_amount", 3));
                (kind == RuleEvent.StarsGained ? stars.AfterStarsGained(3, owner) : stars.AfterStarsSpent(3, owner)).GetAwaiter().GetResult();
                (kind == RuleEvent.StarsGained ? stars.AfterStarsGained(0, owner) : stars.AfterStarsSpent(0, owner)).GetAwaiter().GetResult();
                Check(Activations(stars) == 1);
            }
            var play = new CardPlay { Card = sourceCard, Player = owner, Target = enemy, Resources = new() { EnergySpent = 2, StarsSpent = 3, EnergyValue = 2, StarValue = 3 }, ResultPile = PileType.Discard, PlayCount = 2, PlayIndex = 1, IsAutoPlay = false };
            var spending = Listen(new() { Event = RuleEvent.CardPlayed, Where = new() { LastInSeries = true, AutoPlay = false } }, new() { All = [Eq("event_paid_energy", 2), Eq("event_paid_stars", 3)] });
            spending.AfterCardPlayed(choice, play).GetAwaiter().GetResult(); Check(Activations(spending) == 1);
            var received = Listen(new() { Event = RuleEvent.DamageReceived, Where = new() { Target = "self", Actor = "enemy" } }, new()
            {
                All = [Eq("event_amount", 4), new() { Op = Comparison.Eq, Left = new() { Stat = "hp", Of = "event.target" }, Right = 50 },
                    new() { Op = Comparison.Eq, Left = new() { Stat = "hp", Of = "event.subject" }, Right = owner.Creature.CurrentHp }]
            });
            received.AfterDamageReceived(choice, owner.Creature, new(owner.Creature, ValueProp.Move) { UnblockedDamage = 4 }, ValueProp.Move, enemy, null).GetAwaiter().GetResult();
            Check(Activations(received) == 1);
            var draw = Listen(new() { Event = RuleEvent.CardDrawn, Where = new() { FromHandDraw = false, SourceKind = "unknown" } });
            draw.AfterCardDrawn(choice, sourceCard, true).GetAwaiter().GetResult();
            draw.AfterCardDrawn(choice, sourceCard, false).GetAwaiter().GetResult(); Check(Activations(draw) == 1);
            var drawnSource = Listen(new() { Event = RuleEvent.CardDrawn, Where = new() { SourceKind = "card" } });
            drawnSource.AfterCardDrawn(choice, sourceCard, false).GetAwaiter().GetResult(); Check(Activations(drawnSource) == 0);
            var exhaust = Listen(new() { Event = RuleEvent.CardExhausted, Where = new() { CausedByEthereal = true } });
            exhaust.AfterCardExhausted(choice, sourceCard, false).GetAwaiter().GetResult();
            exhaust.AfterCardExhausted(choice, sourceCard, true).GetAwaiter().GetResult(); Check(Activations(exhaust) == 1);
            var lightning = ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.LightningOrb>().ToMutable(); lightning.Owner = owner;
            var frost = ModelDb.Orb<MegaCrit.Sts2.Core.Models.Orbs.FrostOrb>().ToMutable(); frost.Owner = owner;
            var evoke = Listen(new() { Event = RuleEvent.OrbEvoked, Where = new() { Orb = "lightning" } }, Eq("event_target_count", 2));
            evoke.AfterOrbEvoked(choice, frost, [enemy, second]).GetAwaiter().GetResult();
            evoke.AfterOrbEvoked(choice, lightning, [enemy, second, enemy]).GetAwaiter().GetResult(); Check(Activations(evoke) == 1);
            // Persistence and clone must retain filters and successful-activation quotas independently.
            var loaded = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); loaded.RuntimePayload = evoke.RuntimePayload;
            Check(Activations(loaded) == 1 && Wire.Decode<GeneratedPowerSnapshot>(loaded.RuntimePayload).Definition.Forms[0].Listeners[0].Trigger.Where!.Orb == "lightning");
            var clone = (GeneratedEffectPower)evoke.ClonePreservingMutability();
            var saved = Wire.Decode<GeneratedPowerSnapshot>(clone.RuntimePayload); saved.State.Activations[0] = 9; clone.RuntimePayload = Wire.Encode(saved);
            Check(Activations(evoke) == 1);
            // Execute a real multi-target rule action through native damage commands without visual effects.
            var guard = new Harmony("forge.smoke.extended-event-targets");
            guard.Patch(AccessTools.Method(typeof(CreatureCmd), "Damage", [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)]), prefix: new HarmonyMethod(typeof(Smoke), nameof(DamageWithoutVisuals)));
            try
            {
                var form = new CardForm { Cost = new(), Effects = [new() { Kind = EffectKind.Damage, Target = "event.targets", Amount = 3 }] };
                card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "多目标契约", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Common, Forms = [form with { Effects = null, Rules = [new() { Trigger = new() { Event = RuleEvent.OrbEvoked }, Effects = form.Effects! }] }, form with { Effects = null, Rules = [new() { Trigger = new() { Event = RuleEvent.OrbEvoked }, Effects = form.Effects! }] }] });
                GeneratedEffectExecutor.ExecuteGroup(new(card, choice) { Triggered = true, EventTargets = [enemy, second, enemy] }, form.Effects!, 0).GetAwaiter().GetResult();
                Check(enemy.CurrentHp == 47 && second.CurrentHp == 47);
            }
            finally { guard.UnpatchAll(guard.Id); }
        }
        finally
        {
            foreach (var power in listeners) owner.Creature.RemovePowerInternal(power);
            combat.RemoveCreature(enemy); combat.RemoveCreature(second);
        }
        Console.WriteLine("PASS native extended events preserve per-hit damage, signed power changes, resource payments, origins, targets, filters and persisted state");
    }
    private static bool SkipSaveManager(ref SaveManager? __result) { __result = null; return false; }
    private sealed class SelectionProbe : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        internal int Wanted, Calls;
        internal CardModel[] Candidates = [];
        internal int Min, Max;
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Calls++; Candidates = options.ToArray(); Min = minSelect; Max = maxSelect;
            return Task.FromResult(Candidates.Take(Math.Min(Wanted, maxSelect)));
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives) => throw new NotSupportedException();
    }
    private static void CheckAnySelectionContracts(Player owner, CombatState combat)
    {
        var source = combat.CreateCard<NeowGeneratedCard>(owner);
        var probe = new SelectionProbe();
        using var selector = CardSelectCmd.PushSelector(probe);
        var guard = new Harmony("neowscompany.tests.any-selection");
        // Native choice logging initializes Godot.OS, unavailable in this console harness.
        guard.Patch(AccessTools.Method(typeof(CardSelectCmd), "LogChoice"), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipSelectionLog)));
        guard.Patch(AccessTools.PropertyGetter(typeof(CombatManager), nameof(CombatManager.IsOverOrEnding)), prefix: new HarmonyMethod(typeof(Smoke), nameof(SelectionCombatOpen)));
        guard.Patch(AccessTools.PropertyGetter(typeof(CombatManager), nameof(CombatManager.IsEnding)), prefix: new HarmonyMethod(typeof(Smoke), nameof(SelectionCombatOpen)));
        try
        {
            foreach (var pileName in Enum.GetValues<CardPileName>())
            {
                var pile = NativeMechanics.Pile(pileName).GetPile(owner);
                var statuses = Enumerable.Range(0, 12).Select(_ => combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Wound>(owner)).ToArray();
                var other = combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Shiv>(owner);
                pile.AddInternal(source); pile.AddInternal(other);
                foreach (var status in statuses) pile.AddInternal(status);
                try
                {
                    var target = new EffectTarget { Pile = pileName, Pick = SelectionMode.Any, Filter = new() { Type = "status" } };
                    var form = new CardForm { Cost = new(), Effects = [new() { Kind = EffectKind.Select, Target = target, As = "chosen" }] };
                    source.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "任意选择", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Common, Forms = [form, form] });
                    var restored = (NeowGeneratedCard)CardModel.FromSerializable(source.ToSerializable());
                    Check(restored.ActiveForm.Immediate[0].Target!.Pick == SelectionMode.Any && !restored.DynamicVars.Any(v => v.Key == "T0"));
                    GeneratedExecutionContext Execute(EffectTarget selection)
                    {
                        var context = new GeneratedExecutionContext(source, new BlockingPlayerChoiceContext(), null);
                        GeneratedEffectExecutor.ExecuteGroup(context, [form.Immediate[0] with { Target = selection }], 0).GetAwaiter().GetResult();
                        return context;
                    }
                    foreach (int wanted in new[] { 0, 5, 12 })
                    {
                        probe.Wanted = wanted;
                        int before = probe.Calls;
                        Check(Execute(target).Selections["chosen"].Length == wanted);
                        Check(probe.Calls == before + 1 && probe.Min == 0 && probe.Max == 12);
                        Check(probe.Candidates.Length == 12 && probe.Candidates.All(c => statuses.Contains(c)));
                    }
                    probe.Wanted = 12;
                    Check(Execute(target with { Pick = SelectionMode.Choose, Count = 10, UpTo = true }).Selections["chosen"].Length == 10 && probe.Min == 0 && probe.Max == 10);
                    int calls = probe.Calls;
                    Check(Execute(target with { Pick = SelectionMode.All }).Selections["chosen"].Length == 12 && probe.Calls == calls);
                    Check(!Execute(target with { Filter = null }).Selections["chosen"].Contains(source));
                    calls = probe.Calls;
                    Check(Execute(target with { Filter = new() { Type = "curse" } }).Selections["chosen"].Length == 0 && probe.Calls == calls);
                }
                finally
                {
                    foreach (var status in statuses) pile.RemoveInternal(status);
                    pile.RemoveInternal(other); pile.RemoveInternal(source);
                }
            }
        }
        finally { guard.UnpatchAll(guard.Id); }
        Console.WriteLine("PASS any selection uses native choices for zero/partial/all cards in all piles, filters, source exclusion, save/load and bounded choose compatibility");
    }
    private static bool SelectionCombatOpen(ref bool __result) { __result = false; return false; }
    private static bool SkipSelectionLog() => false;
    private static void CheckFirstCombatTiming()
    {
        foreach (var roomType in new[] { RoomType.Monster, RoomType.Elite, RoomType.Boss })
        {
            var run = RunState.CreateForTest([Player.CreateForNewRun<Regent>(UnlockState.all, 5)],
                acts: [ModelDb.Act<Underdocks>(), ModelDb.Act<Overgrowth>()], seed: "FIRST_COMBAT");
            run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, null);
            run.AppendToMapPointHistory(MapPointType.RestSite, RoomType.RestSite, null);
            run.AppendToMapPointHistory(MapPointType.Unknown, roomType, null);
            Check(run.TotalFloor == 3 && ForgeRuntime.IsFirstCombat(run));
            run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
            Check(!ForgeRuntime.IsFirstCombat(run));
            run.CurrentActIndex = 1;
            run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
            Check(!ForgeRuntime.IsFirstCombat(run));
        }
        var eventRun = RunState.CreateForTest([Player.CreateForNewRun<Regent>(UnlockState.all, 6)], seed: "EVENT_COMBAT");
        eventRun.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, null);
        eventRun.CurrentMapPointHistoryEntry!.Rooms.Add(new MapPointRoomHistoryEntry { RoomType = RoomType.Monster });
        Check(ForgeRuntime.IsFirstCombat(eventRun));
        eventRun.CurrentMapPointHistoryEntry.Rooms.Add(new MapPointRoomHistoryEntry { RoomType = RoomType.Elite });
        Check(!ForgeRuntime.IsFirstCombat(eventRun));
        Console.WriteLine("PASS first-combat timing uses native room history across floors, event combats and acts");
    }
    private static void CheckQuantityAndDecimalContracts(Player owner, Player other, CombatState combat)
    {
        CheckRuleLimitPreviewContracts(owner, combat);
        var card = combat.CreateCard<NeowGeneratedCard>(owner);
        CardForm Form(int count) => new()
        {
            Cost = new() { Energy = 1 }, Effects =
            [
                new() { Kind = EffectKind.Damage, Target = "enemy", Amount = 6, Repeat = count },
                new() { Kind = EffectKind.CreateCard, Card = new() { Id = "shiv" }, To = CardPileName.Hand, Count = count },
                new() { Kind = EffectKind.Exhaust, Target = new() { Pile = CardPileName.Hand, Pick = SelectionMode.Random, Count = count } }
            ]
        };
        card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "暗影飞刃", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Common, Forms = [Form(1), Form(2)] });
        Check(card.GetDescriptionForUpgradePreview().Contains("1张小刀"));
        card.UpgradeInternal(); card.FinalizeUpgradeInternal();
        var flags = card.BeginUpgradePreview();
        try
        {
            string text = card.GetDescriptionForUpgradePreview();
            Check(text.Contains("造成6点伤害[green]2[/green]次。"));
            Check(text.Contains("[green]2[/green]张小刀") && text.Contains("[green]2[/green]次") && text.Contains("随机[green]2[/green]张牌"));
            Check(!text.Contains("[green]6[/green]"));
        }
        finally { card.EndUpgradePreview(flags); }
        Check(card.DynamicVars.Values.All(v => !v.WasJustUpgraded));
        var restored = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable()); restored.Owner = owner;
        flags = restored.BeginUpgradePreview();
        try
        {
            string text = restored.GetDescriptionForUpgradePreview();
            Check(text.Contains("[green]2[/green]张小刀") && text.Contains("造成6点伤害[green]2[/green]次。"));
        }
        finally { restored.EndUpgradePreview(flags); }
        var capture = new StateCapture();
        Check(capture.Detach(capture.Card(card)).GetProperty("origin").GetString() == "generated");
        Check(capture.Detach(capture.Card(NativeMechanics.Card("shiv"))).GetProperty("origin").GetString() == "native");
        var summary = new CombatSummary();
        // Capture the real card; keep the actor detached so headless tests avoid Godot icon assets on its powers.
        summary.Add(ObservationProjector.CompactEvent(System.Text.Json.JsonSerializer.SerializeToElement(new { detail = new { type = "CardPlayFinishedEntry",
            fields = new { Actor = new { id = owner.Character.Id.Entry }, CardPlay = new { card = capture.Detach(capture.Card(card)) } } } })), owner.Character.Id.Entry);
        Check(summary.Snapshot().GetProperty("cards_played")[0].GetProperty("origin").GetString() == "generated");

        var repeated = combat.CreateCard<NeowGeneratedCard>(owner);
        var repeatedForm = new CardForm { Cost = new(), Effects = [
            new() { Kind = EffectKind.GainEnergy, Amount = 1, Repeat = new() { Stat = "hand_size" } },
            new() { Kind = EffectKind.Draw, Amount = 2 }] };
        repeated.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "薪尽火传", Type = ForgeCardType.Skill,
            Rarity = ForgeRarity.Rare, Forms = [repeatedForm, repeatedForm] });
        Check(repeated.RuntimeDescriptionText(true).StartsWith("手牌中每有一张牌，就获得"));
        owner.PlayerCombatState!.Hand.AddInternal(repeated);
        try
        {
            repeated.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, repeated.DynamicVars);
            Check(repeated.RuntimeDescriptionText(true).StartsWith("重复{R0:diff()}次：『获得"));
            Check(repeated.GetDescriptionForPile(PileType.Hand).EndsWith("』。\n抽2张牌。"));
            var reloaded = (NeowGeneratedCard)CardModel.FromSerializable(repeated.ToSerializable()); reloaded.Owner = owner;
            Check(reloaded.RuntimeDescriptionText(true).StartsWith("手牌中每有一张牌，就获得"));
        }
        finally { owner.PlayerCombatState.Hand.RemoveInternal(repeated); }

        owner.Creature.SetMaxHpInternal(79); owner.Creature.SetCurrentHpInternal(39);
        var half = new NumberExpression { Div = [new() { Stat = "max_hp" }, 2] };
        var execution = new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext());
        Check(execution.Number(half) == 39.5m && execution.Matches(new() { Op = Comparison.Lt, Left = new() { Stat = "hp" }, Right = half }));
        var form = new CardForm { Cost = new(), Effects = [new() { Kind = EffectKind.Damage, Target = "enemy", Amount = new() { Mul = [new() { Stat = "max_hp" }, 0.5m] } }, new() { Kind = EffectKind.Draw, Amount = 1.5m }] };
        card.DowngradeInternal();
        card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "小数试炼", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Common, Forms = [form, form] });
        Check(card.GetDescriptionForPile(PileType.Deck).Contains("抽2张牌"));
        owner.PlayerCombatState!.Hand.AddInternal(card);
        card.UpdateDynamicVarPreview(CardPreviewMode.Normal, other.Creature, card.DynamicVars);
        Check(new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext()).Amount(form.Immediate[0], 0) == 39.5m);
        Check(card.DynamicVars["E1"].PreviewValue == 2 && card.GetDescriptionForPile(PileType.Hand).Contains("抽2张牌"));
        owner.PlayerCombatState.Hand.RemoveInternal(card);
        Console.WriteLine("PASS quantity upgrade highlights and reload, generated origins, decimal HP comparisons and native draw preview");
    }
    private static void CheckRuleLimitPreviewContracts(Player owner, CombatState combat)
    {
        foreach (var scope in new[] { CounterScope.Turn, CounterScope.Combat })
        {
            CardForm Form(int count) => new()
            {
                Cost = new() { Energy = 1 }, Rules = [new()
                {
                    Trigger = new() { Event = RuleEvent.OrbChanneled, Limit = new() { Count = count, Within = scope } },
                    Condition = new() { Op = Comparison.Ge, Left = new() { Stat = "orb_count" }, Right = new() { Stat = "orb_capacity" } },
                    Effects = [new() { Kind = EffectKind.Draw, Amount = 1 }]
                }]
            };
            var card = combat.CreateCard<NeowGeneratedCard>(owner);
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "溢流阀", Type = ForgeCardType.Power, Rarity = ForgeRarity.Uncommon, Forms = [Form(1), Form(2)] });
            Check(card.GetDescriptionForUpgradePreview().Contains("最多生效1次"));
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            var restored = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable()); restored.Owner = owner;
            foreach (var preview in new[] { card, restored })
            {
                Check(preview.GetDescriptionForPile(PileType.Deck).Contains("最多生效2次"));
                var flags = preview.BeginUpgradePreview();
                try
                {
                    string text = preview.GetDescriptionForUpgradePreview();
                    Check(text.Contains("最多生效[green]2[/green]次") && text.Contains("抽1张牌") && !text.Contains("[green]1[/green]"));
                    Check(preview.RuntimeDescriptionText(false).Contains("at most {L0:diff()} activations"));
                }
                finally { preview.EndUpgradePreview(flags); }
                Check(preview.DynamicVars.Values.All(v => !v.WasJustUpgraded));
                preview.DowngradeInternal();
                Check(preview.GetDescriptionForUpgradePreview().Contains("最多生效1次"));
            }
            card.DefinitionPayload = Wire.Encode(card.Definition with { Forms = [Form(2), Form(2)] });
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            var unchanged = card.BeginUpgradePreview();
            try { Check(!card.GetDescriptionForUpgradePreview().Contains("[green]")); }
            finally { card.EndUpgradePreview(unchanged); }
        }
        Console.WriteLine("PASS rule limit upgrade highlights, unchanged counts, reload and downgrade for turn/combat quotas");
    }
    private static void CheckOstyContracts(NeowGeneratedCard card, Player owner, Player attacker, CombatState combat)
    {
        var osty = combat.CreateCreature(ModelDb.Monster<Osty>().ToMutable(), CombatSide.Player, null);
        combat.AddCreature(osty); owner.PlayerCombatState!.AddPetInternal(osty);
        osty.SetMaxHpInternal(30); osty.SetCurrentHpInternal(30);
        ((DieForYouPower)ModelDb.Power<DieForYouPower>().ToMutable()).ApplyInternal(osty, 1, silent: true);
        osty.GainBlockInternal(100);
        var thorns = (ThornsPower)ModelDb.Power<ThornsPower>().ToMutable(); thorns.ApplyInternal(osty, 3, silent: true);
        var vulnerable = (VulnerablePower)ModelDb.Power<VulnerablePower>().ToMutable(); vulnerable.ApplyInternal(osty, 1, silent: true);
        // Native order: damage modifiers / thorns / player's block, then redirect and HP-loss hooks.
        decimal damage = Hook.ModifyDamage(owner.RunState, combat, owner.Creature, attacker.Creature, 10,
            ValueProp.Move, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
        Check(damage == 10); // Osty's Vulnerable does not modify a hit aimed at the player.
        int attackerHp = attacker.Creature.CurrentHp;
        thorns.BeforeDamageReceived(new BlockingPlayerChoiceContext(), owner.Creature, damage, ValueProp.Move, attacker.Creature, null).GetAwaiter().GetResult();
        Check(attacker.Creature.CurrentHp == attackerHp);
        Check(Hook.ModifyUnblockedDamageTarget(combat, owner.Creature, 10, ValueProp.Move, attacker.Creature) == osty);
        Check(Hook.ModifyUnblockedDamageTarget(combat, owner.Creature, 10, ValueProp.Unpowered, attacker.Creature) == owner.Creature);
        Check(Hook.ModifyHpLost(owner.RunState, combat, osty, 10, ValueProp.Move, attacker.Creature, null, HpLossHookPhase.AfterOsty, out _) == 10);
        Check(osty.Block == 100);
        var buffer = (BufferPower)ModelDb.Power<BufferPower>().ToMutable(); buffer.ApplyInternal(osty, 1, silent: true);
        Check(Hook.ModifyHpLost(owner.RunState, combat, osty, 10, ValueProp.Move, attacker.Creature, null, HpLossHookPhase.AfterOsty, out _) == 0);
        osty.RemovePowerInternal(buffer);
        var intangible = (IntangiblePower)ModelDb.Power<IntangiblePower>().ToMutable(); intangible.ApplyInternal(osty, 1, silent: true);
        Check(Hook.ModifyHpLost(owner.RunState, combat, osty, 10, ValueProp.Move, attacker.Creature, null, HpLossHookPhase.AfterOsty, out _) == 1);
        var artifact = (ArtifactPower)ModelDb.Power<ArtifactPower>().ToMutable(); artifact.ApplyInternal(osty, 1, silent: true);
        Check(artifact.TryModifyPowerAmountReceived(ModelDb.Power<WeakPower>(), osty, 1, null, out decimal blockedPower) && blockedPower == 0);
        Check(!artifact.TryModifyPowerAmountReceived(ModelDb.Power<StrengthPower>(), osty, 1, null, out _));
        var strength = (StrengthPower)ModelDb.Power<StrengthPower>().ToMutable(); strength.ApplyInternal(osty, 2, silent: true);
        var vigor = (VigorPower)ModelDb.Power<VigorPower>().ToMutable(); vigor.ApplyInternal(osty, 3, silent: true);
        var weak = (WeakPower)ModelDb.Power<WeakPower>().ToMutable(); weak.ApplyInternal(osty, 1, silent: true);
        Check(strength.ModifyDamageAdditive(attacker.Creature, 10, ValueProp.Move, osty, card, null) == 2);
        Check(vigor.ModifyDamageAdditive(attacker.Creature, 10, ValueProp.Move, osty, card, null) == 3);
        Check(weak.ModifyDamageMultiplicative(attacker.Creature, 10, ValueProp.Move, osty, card, null) == 0.75m);
        Check(strength.ModifyDamageAdditive(attacker.Creature, 10, ValueProp.Unpowered, osty, card, null) == 0);
        Check(vigor.ModifyDamageAdditive(attacker.Creature, 10, ValueProp.Unpowered, osty, card, null) == 0);
        Check(weak.ModifyDamageMultiplicative(attacker.Creature, 10, ValueProp.Unpowered, osty, card, null) == 1);
        // Reward text retains the expression; a hand preview still displays the resolved number.
        var form = new CardForm { Cost = new() { Energy = 1 }, Effects = [new() { Kind = EffectKind.Block, Amount = new() { Stat = "hp", Of = "osty" } }] };
        var description = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable(); description.Owner = owner;
        description.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "缚魂护壁", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Uncommon, Forms = [form, form] });
        Check(description.RuntimeDescriptionText(true) == "获得等量于奥斯提的当前生命值的格挡。");
        PileType.Hand.GetPile(owner).AddInternal(description);
        description.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, description.DynamicVars);
        Check(description.GetDescriptionForPile(PileType.Hand).Contains("点格挡"));
        var ruleForm = form with { Effects = null, Rules = [new() { Trigger = new() { Event = RuleEvent.Summoned }, Effects = [new() { Kind = EffectKind.Damage, Actor = "osty", Target = "random_enemy", Amount = new() { Stat = "event_amount" } }] }] };
        description.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "噬魂回响", Type = ForgeCardType.Power, Rarity = ForgeRarity.Uncommon, Forms = [ruleForm, ruleForm] });
        Check(description.RuntimeDescriptionText(true) == "每当你召唤时，奥斯提对随机敌人造成等量于本次召唤量的伤害。");
        var restored = (NeowGeneratedCard)CardModel.FromSerializable(description.ToSerializable());
        Check(restored.RuntimeDescriptionText(true) == description.RuntimeDescriptionText(true));
        combat.RemoveCreature(osty);
        Console.WriteLine("PASS native Osty redirection, ineffective block/thorns/vulnerable, effective powers and symbolic text/save contracts");
    }
    private static void CheckBlockContracts(NeowGeneratedCard card, Player owner, Harmony guard)
    {
        // Keep the native block hooks and rounding; bypass only audiovisual command work.
        // This test combat has no enemies, so keep its lifecycle open for executor checks.
        guard.Patch(AccessTools.PropertyGetter(typeof(CombatManager), nameof(CombatManager.IsEnding)), prefix: new HarmonyMethod(typeof(Smoke), nameof(KeepCombatOpen)));
        guard.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), [typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool)]),
            prefix: new HarmonyMethod(typeof(Smoke), nameof(GainBlockWithoutVisuals)));
        ((DexterityPower)ModelDb.Power<DexterityPower>().ToMutable()).ApplyInternal(owner.Creature, 3, silent: true);
        var immediate = new CardEffect { Kind = EffectKind.Block, Amount = 4 };
        var listener = new CardRule { Trigger = new() { Event = RuleEvent.CardPlayed, Filter = new() { Cost = 0 } }, Effects = [new() { Kind = EffectKind.Block, Amount = 2 }] };
        var form = new CardForm { Cost = new() { Energy = 1 }, Effects = [immediate], Rules = [listener] };
        foreach (bool frail in new[] { false, true })
        {
            if (frail) ((FrailPower)ModelDb.Power<FrailPower>().ToMutable()).ApplyInternal(owner.Creature, 1, silent: true);
            foreach (ForgeCardType type in new[] { ForgeCardType.Power, ForgeCardType.Skill })
            {
                if (card.IsUpgraded) card.DowngradeInternal();
                card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "触发格挡", Type = type, Rarity = ForgeRarity.Uncommon, Forms = [form, form] });
                card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
                Check(card.DynamicVars["E0"].PreviewValue == (frail ? 5.25m : 7m) && card.DynamicVars["E1"].PreviewValue == 2m);
                Check(((BlockVar)card.DynamicVars["E0"]).Props == ValueProp.Move && ((BlockVar)card.DynamicVars["E1"]).Props == ValueProp.Unpowered);
                var play = new CardPlay { Card = card, Player = owner, Target = null, IsAutoPlay = false, Resources = new() { EnergySpent = 1, EnergyValue = 1, StarsSpent = 0, StarValue = 0 }, ResultPile = PileType.Discard, PlayIndex = 0, PlayCount = 1 };
                int before = owner.Creature.Block;
                var context = new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext(), play);
                GeneratedEffectExecutor.ExecuteGroup(context, form.Immediate, 0).GetAwaiter().GetResult();
                Check(owner.Creature.Block - before == (frail ? 5 : 7) && BlockCapture.Props == ValueProp.Move && BlockCapture.Play == play);
                before = owner.Creature.Block;
                context = new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext(), play) { Triggered = true };
                GeneratedEffectExecutor.ExecuteGroup(context, listener.Effects, 1).GetAwaiter().GetResult();
                Check(owner.Creature.Block - before == 2 && BlockCapture.Props == ValueProp.Unpowered && BlockCapture.Play is null);
                card.UpgradeInternal(); card.FinalizeUpgradeInternal();
                var loaded = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable()); loaded.Owner = owner;
                loaded.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, loaded.DynamicVars);
                Check(loaded.DynamicVars["E1"].PreviewValue == 2 && ((BlockVar)loaded.DynamicVars["E1"]).Props == ValueProp.Unpowered);
            }
        }
        card.DowngradeInternal();
        Console.WriteLine("PASS native Dexterity/Frail apply to direct block only, with matching executor/preview/upgrade/save properties");
    }
    private static class BlockCapture
    {
        internal static ValueProp Props;
        internal static CardPlay? Play;
    }
    private static bool KeepCombatOpen(ref bool __result) { __result = FatalCapture.Ending; return false; }
    private static void CheckFatalContracts(NeowGeneratedCard card, Player owner, CombatState combat)
    {
        // Execute real resource commands and native HP-loss results. Bypass damage animations,
        // attack orchestration and sound; this harness does not establish live combat/UI correctness.
        var guard = new Harmony("neowscompany.tests.fatal");
        guard.Patch(AccessTools.Method(typeof(AttackCommand), nameof(AttackCommand.Execute)), prefix: new HarmonyMethod(typeof(Smoke), nameof(AttackWithoutVisuals)));
        guard.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
            [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)]),
            prefix: new HarmonyMethod(typeof(Smoke), nameof(DamageWithoutVisuals)));
        guard.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Heal)), prefix: new HarmonyMethod(typeof(Smoke), nameof(HealWithoutAnimation)));
        guard.Patch(AccessTools.Method(typeof(Cmd), nameof(Cmd.CustomScaledWait)), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipCommandWait)));
        guard.Patch(AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]), prefix: new HarmonyMethod(typeof(Smoke), nameof(SkipSound)));
        var added = new List<Creature>();
        int oldMaxHp = owner.Creature.MaxHp, oldHp = owner.Creature.CurrentHp, oldGold = owner.Gold;
        Creature Enemy(int hp)
        {
            var enemy = combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, null);
            enemy.SetMaxHpInternal(hp); enemy.SetCurrentHpInternal(hp); combat.AddCreature(enemy); added.Add(enemy);
            return enemy;
        }
        void ResetEnemies()
        {
            foreach (var enemy in added) combat.RemoveCreature(enemy);
            added.Clear(); FatalCapture.Ending = false; FatalCapture.EndAfterKill = false; FatalCapture.Attacks = 0;
        }
        void Configure(CardEffect[] effects, ForgeCardType type = ForgeCardType.Attack, CardRule[]? rules = null)
        {
            if (card.IsUpgraded) card.DowngradeInternal();
            var form = new CardForm { Cost = new(), Effects = effects, Rules = rules };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "斩杀试炼", Type = type, Rarity = ForgeRarity.Rare, Forms = [form, form] });
        }
        GeneratedExecutionContext Execute(Creature? target, bool triggered = false, CardEffect[]? effects = null, int offset = 0)
        {
            var play = new CardPlay { Card = card, Player = owner, Target = target, Resources = new() { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 }, ResultPile = PileType.Discard, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
            var context = new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext(), play) { Triggered = triggered, EventTarget = target };
            GeneratedEffectExecutor.ExecuteGroup(context, effects ?? card.ActiveForm.Immediate, offset).GetAwaiter().GetResult();
            return context;
        }
        var gold = new CardEffect { Kind = EffectKind.GainGold, Amount = 7 };
        var damage = new CardEffect { Kind = EffectKind.Damage, Target = "enemy", Amount = 10, OnFatal = [gold] };
        try
        {
            // Native permanent changes, including gain healing, loss above the new cap and gold floor.
            owner.Creature.SetMaxHpInternal(80); owner.Creature.SetCurrentHpInternal(40); owner.Gold = 20;
            Configure([new() { Kind = EffectKind.GainMaxHp, Amount = 3 }, new() { Kind = EffectKind.GainGold, Amount = new() { Stat = "max_hp" } },
                new() { Kind = EffectKind.LoseGold, Amount = 5 }], ForgeCardType.Skill);
            Execute(null);
            Check(owner.Creature.MaxHp == 83 && owner.Creature.CurrentHp == 43 && owner.Gold == 98);
            Check(new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext()).Read(new() { Stat = "gold" }) == 98);
            owner.Creature.SetCurrentHpInternal(83);
            Configure([new() { Kind = EffectKind.LoseMaxHp, Amount = 4 }, new() { Kind = EffectKind.LoseGold, Amount = 999 }], ForgeCardType.Skill);
            Execute(null);
            Check(owner.Creature.MaxHp == 79 && owner.Creature.CurrentHp == 79 && owner.Gold == 0);
            Configure([new() { Kind = EffectKind.GainMaxHp, Amount = 3 }, new() { Kind = EffectKind.GainGold, Amount = 12 },
                new() { Kind = EffectKind.LoseGold, Amount = 2 }, new() { Kind = EffectKind.Block, Amount = new() { Add = [new() { Stat = "max_hp" }, new() { Stat = "gold" }] } }], ForgeCardType.Skill);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
            Check(GeneratedEffectPreview.Resolve(card, 3, null, CardPreviewMode.Normal) == 92);
            Check(owner.Creature.MaxHp == 79 && owner.Gold == 0);

            // Nonlethal, skipped and disallowed native fatalities never pay out.
            var enemy = Enemy(20); Configure([damage]); Execute(enemy); Check(owner.Gold == 0 && enemy.CurrentHp == 10);
            Configure([damage with { Condition = new() { Op = Comparison.Eq, Left = 1, Right = 0 } }]); Execute(enemy);
            Check(owner.Gold == 0 && enemy.CurrentHp == 10);
            ((MinionPower)ModelDb.Power<MinionPower>().ToMutable()).ApplyInternal(enemy, 1, silent: true);
            Configure([damage]); Execute(enemy); Check(owner.Gold == 0 && enemy.IsDead);
            ResetEnemies(); enemy = Enemy(10); Execute(enemy); Check(owner.Gold == 7);

            // Uniform and per-target-expression AoEs each pay once, independent of native commands.
            ResetEnemies(); Enemy(5); Enemy(8); owner.Gold = 0;
            Configure([damage with { Target = "all_enemies" }]); Execute(null);
            Check(owner.Gold == 7 && FatalCapture.Attacks == 1);
            ResetEnemies(); Enemy(5); Enemy(8); owner.Gold = 0;
            Configure([damage with { Target = "all_enemies", Amount = new() { Stat = "hp", Of = "target" } }]); Execute(null);
            Check(owner.Gold == 7 && FatalCapture.Attacks == 2);
            ResetEnemies(); Enemy(5); enemy = Enemy(8);
            ((MinionPower)ModelDb.Power<MinionPower>().ToMutable()).ApplyInternal(enemy, 1, silent: true);
            owner.Gold = 0; Configure([damage with { Target = "all_enemies" }]); Execute(null); Check(owner.Gold == 7);

            // Each repeat has its own result. Nested damage is allowed and pays before continuing.
            ResetEnemies(); Enemy(5); Enemy(5); owner.Gold = 0;
            Configure([damage with { Target = "random_enemy", Repeat = 2 }]); Execute(null); Check(owner.Gold == 14);
            ResetEnemies(); enemy = Enemy(5); Enemy(5); owner.Gold = 0;
            Configure([damage with { OnFatal = [damage with { Target = "random_enemy" }, gold] }]); Execute(enemy); Check(owner.Gold == 14);
            ResetEnemies(); enemy = Enemy(5); owner.Gold = 0;
            Configure([damage with { OnFatal = [gold with { Amount = new() { Stat = "damage_dealt" } }] }]); Execute(enemy); Check(owner.Gold == 5);

            // Last-kill payoffs complete; ordinary subsequent effects remain blocked by ending.
            ResetEnemies(); enemy = Enemy(5); owner.Gold = 0; FatalCapture.EndAfterKill = true;
            int beforeMax = owner.Creature.MaxHp;
            Configure([damage with { OnFatal = [new() { Kind = EffectKind.GainMaxHp, Amount = 3 }, gold] }, gold with { Amount = 100 }]);
            var last = Execute(enemy);
            Check(FatalCapture.Ending && owner.Gold == 7 && owner.Creature.MaxHp == beforeMax + 3 && !last.ResolvingFatal);

            // Rule damage uses native unpowered results and flattened slots after immediate groups.
            ResetEnemies(); enemy = Enemy(5); owner.Gold = 0;
            var rule = new CardRule { Trigger = new() { Event = RuleEvent.TurnEnd }, Effects = [damage with { Target = "event.target" }, gold with { Amount = 2 }] };
            Configure([damage with { OnFatal = [gold with { Amount = 100 }] }], rules: [rule]);
            Execute(enemy, true, rule.Effects, card.ActiveForm.ImmediateEffects.Length);
            Check(owner.Gold == 9);

            // Nested numeric slots, symbolic payoff amounts, powered block, upgrades and saves.
            ResetEnemies(); enemy = Enemy(5);
            var baseForm = new CardForm { Cost = new(), Effects = [damage with { OnFatal = [new() { Kind = EffectKind.Block, Amount = 4 }, new() { Kind = EffectKind.GainMaxHp, Amount = 3 }] }, new() { Kind = EffectKind.GainGold, Amount = 2 }], Rules = [rule] };
            var upgraded = baseForm with { Effects = [damage with { OnFatal = [new() { Kind = EffectKind.Block, Amount = 4 }, new() { Kind = EffectKind.GainMaxHp, Amount = 4 }] }, new() { Kind = EffectKind.GainGold, Amount = 2 }] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "成长试炼", Type = ForgeCardType.Attack, Rarity = ForgeRarity.Rare, Forms = [baseForm, upgraded] });
            Check(card.DynamicVars["E2"].BaseValue == 3 && ((BlockVar)card.DynamicVars["E1"]).Props == ValueProp.Move);
            bool addedToHand = card.Pile?.Type != PileType.Hand;
            if (addedToHand) owner.PlayerCombatState!.Hand.AddInternal(card);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, enemy, card.DynamicVars);
            Check(card.GetDescriptionForPile(PileType.Hand).Contains("斩杀") && card.DynamicVars["E3"].PreviewValue == 2);
            if (addedToHand) owner.PlayerCombatState!.Hand.RemoveInternal(card);
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            Check(card.DynamicVars["E2"].BaseValue == 4);
            var flags = card.BeginUpgradePreview();
            try { Check(card.GetDescriptionForUpgradePreview().Contains("[green]4[/green]点最大生命")); }
            finally { card.EndUpgradePreview(flags); }
            var saved = (NeowGeneratedCard)CardModel.FromSerializable(card.ToSerializable()); saved.Owner = owner;
            Check(saved.DefinitionPayload == card.DefinitionPayload && saved.DynamicVars["E2"].BaseValue == 4);
            var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); power.Configure(card);
            var restored = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); restored.RuntimePayload = power.RuntimePayload;
            Check(restored.RuntimeDescriptionText(true).Contains("获得7枚金币") && restored.RuntimeDescriptionText(true).Contains("获得2枚金币"));

            ResetEnemies(); enemy = Enemy(5); owner.Gold = 0;
            Configure([damage with { OnFatal = [gold with { Amount = new() { Stat = "gold" } }] }, gold]);
            Check(card.RuntimeDescriptionText(true).Contains("你的金币"));
            var inherited = Execute(enemy);
            Check(owner.Gold == 7 && inherited.Selections.Count == 0);
            ResetEnemies(); enemy = Enemy(5);
            var chosen = combat.CreateCard<NeowGeneratedCard>(owner);
            owner.PlayerCombatState!.Hand.AddInternal(chosen);
            try
            {
                var select = new CardEffect { Kind = EffectKind.Select, Target = new() { Pile = CardPileName.Hand, Pick = SelectionMode.All }, As = "outer" };
                Configure([select, damage with { OnFatal = [select with { As = "inner" }, new() { Kind = EffectKind.Upgrade, Target = "selected:outer" }] }]);
                inherited = Execute(enemy);
                Check(chosen.IsUpgraded && inherited.Selections.ContainsKey("outer") && !inherited.Selections.ContainsKey("inner"));
            }
            finally { owner.PlayerCombatState.Hand.RemoveInternal(chosen); }
            ResetEnemies(); enemy = Enemy(5);
            Configure([damage with { OnFatal = [new() { Kind = EffectKind.GainGold, Amount = new() { Stat = "damage_dealt" } }] }]);
            // A failing nested action restores scope/fatal state and clears the shared operation budget.
            var failurePlay = new CardPlay { Card = card, Player = owner, Target = enemy, Resources = new() { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 }, ResultPile = PileType.Discard, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
            var failure = new GeneratedExecutionContext(card, new BlockingPlayerChoiceContext(), failurePlay);
            failure.Amounts[1] = decimal.MaxValue;
            try { GeneratedEffectExecutor.ExecuteGroup(failure, card.ActiveForm.Immediate, 0).GetAwaiter().GetResult(); throw new Exception("Expected fatal expression overflow."); }
            catch (OverflowException) { Check(!failure.ResolvingFatal); }
            Console.WriteLine("PASS native HP/gold commands, fatal eligibility/results, AoE/repeat/nesting/last kill and nested preview/upgrade/save slots");
        }
        finally
        {
            ResetEnemies(); guard.UnpatchAll(guard.Id);
            owner.Creature.SetMaxHpInternal(oldMaxHp); owner.Creature.SetCurrentHpInternal(oldHp); owner.Gold = oldGold;
        }
    }
    private static class FatalCapture
    {
        internal static bool Ending;
        internal static bool EndAfterKill;
        internal static int Attacks;
    }
    private static DamageResult[] NativeTestDamage(IEnumerable<Creature> targets, decimal amount, ValueProp props)
    {
        var creatures = targets.Where(t => t.IsAlive).ToArray();
        var results = creatures.Select(t => t.LoseHpInternal(amount, props)).ToArray();
        if (FatalCapture.EndAfterKill && creatures.FirstOrDefault()?.CombatState is { } combat && combat.Enemies.All(t => t.IsDead)) FatalCapture.Ending = true;
        return results;
    }
    private static bool AttackWithoutVisuals(AttackCommand __instance, ref Task<AttackCommand> __result)
    {
        FatalCapture.Attacks++;
        var targets = (IReadOnlyList<Creature>)AccessTools.Method(typeof(AttackCommand), "GetPossibleTargets").Invoke(__instance, null)!;
        decimal amount = (decimal)AccessTools.Field(typeof(AttackCommand), "_damagePerHit").GetValue(__instance)!;
        var results = (List<List<DamageResult>>)AccessTools.Field(typeof(AttackCommand), "_results").GetValue(__instance)!;
        results.Add(NativeTestDamage(targets, amount, ValueProp.Move).ToList());
        __result = Task.FromResult(__instance); return false;
    }
    private static bool DamageWithoutVisuals(IEnumerable<Creature>? targets, decimal amount, ValueProp props, ref Task<IEnumerable<DamageResult>> __result)
    {
        __result = Task.FromResult<IEnumerable<DamageResult>>(NativeTestDamage(targets ?? [], amount, props)); return false;
    }
    private static void HealWithoutAnimation(ref bool playAnim) => playAnim = false;
    private static bool SkipCommandWait(ref Task __result) { __result = Task.CompletedTask; return false; }
    private static bool SkipSound() => false;
    private static bool GainBlockWithoutVisuals(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay, ref Task<decimal> __result)
    {
        BlockCapture.Props = props; BlockCapture.Play = cardPlay;
        decimal modified = Math.Max(0, Hook.ModifyBlock(creature.CombatState!, creature, amount, props, cardPlay?.Card, cardPlay, out _));
        creature.GainBlockInternal(modified);
        __result = Task.FromResult(modified);
        return false;
    }
    private static void CheckTextContracts()
    {
        foreach (var character in new CharacterModel[] { ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(), ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>() })
        {
            var owner = Player.CreateForNewRun(character, UnlockState.all, 10);
            // Text requires an owner for the native character energy icon, no inventory or combat effects.
            var card = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable(); card.Owner = owner;
            Check(card.Pool == character.CardPool && card.VisualCardPool == character.CardPool);
            Check(((NeowGeneratedCard)card.ClonePreservingMutability()).Pool == character.CardPool);
            var rule = new CardRule { Trigger = new() { Event = RuleEvent.CardPlayed, Filter = new() { Cost = 0 } }, Effects = [new() { Kind = EffectKind.GainEnergy, Amount = 2 }, new() { Kind = EffectKind.GainStars, Amount = 3 }, new() { Kind = EffectKind.Block, Amount = 1 }] };
            var form = new CardForm { Cost = new() { Energy = 1 }, Rules = [rule] };
            card.DefinitionPayload = Wire.Encode(new CardDefinition { Name = "费用图标", Type = ForgeCardType.Power, Rarity = ForgeRarity.Rare, Forms = [form, form] });
            string prefix = EnergyIconHelper.GetPrefix(card);
            string icon = $"[img]res://images/packed/sprite_fonts/{prefix}_energy_icon.png[/img]";
            string text = card.RuntimeDescriptionText(true);
            Check(prefix == character.CardPool.EnergyColorName && text.Contains("耗能为0" + icon + "的牌") && text.Contains("获得" + icon + icon) && !text.Contains("本场战斗") && !text.Contains("点星"));
            card.DynamicVars["E2"].BaseValue = 5;
            var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable(); power.Configure(card);
            power.ApplyInternal(owner.Creature, 1, silent: true);
            string description = power.RuntimeDescriptionText(true);
            Check(description.Contains("耗能为0" + icon + "的牌") && description.Contains("获得5点格挡") && !description.Contains("本场战斗"));
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            text = card.RuntimeDescriptionText(true);
            Check(text.Contains("耗能为0" + icon + "的牌") && !text.Contains("本场战斗"));
        }
        Console.WriteLine("PASS all five character card/visual pools, cloned pools, energy icons in card/upgrade/power text and captured listener amounts");
    }
    private static void CheckNativeConversionContracts()
    {
        NativeCardSnapshot Capture<T>() where T : CardModel
        {
            var canonical = ModelDb.Card<T>();
            var native = (CardModel)canonical.ToMutable();
            NativeCardForm Form() => new()
            {
                Cost = new() { Energy = native.EnergyCost.GetWithModifiers(CostModifiers.None),
                    Stars = native.BaseStarCost >= 0 ? native.BaseStarCost : null,
                    EnergyX = native.EnergyCost.CostsX ? true : null, StarsX = native.HasStarCostX ? true : null },
                Variables = native.DynamicVars.ToDictionary(v => v.Key, v => v.Value.BaseValue),
                Keywords = native.Keywords.Select(k => k.ToString()).ToArray(), Tags = native.Tags.Select(t => t.ToString()).ToArray(),
                Target = native.TargetType.ToString()
            };
            var basic = Form(); native.UpgradeInternal(); native.FinalizeUpgradeInternal();
            return new() { Id = canonical.Id.Entry, ClassName = typeof(T).Name, Name = typeof(T).Name,
                Type = canonical.Type.ToString(), Rarity = canonical.Rarity.ToString(), Forms = [basic, Form()] };
        }
        var armaments = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.Armaments>());
        Check(armaments.Status == NativeConversionStatus.EffectsMapped && armaments.Definition!.Forms[1].Immediate[1].Target!.Pick == SelectionMode.All);
        var grit = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.TrueGrit>()).Definition!;
        Check(grit.Forms[0].Immediate[0].Amount!.Value == 7 && grit.Forms[1].Immediate[0].Amount!.Value == 9);
        Check(grit.Forms[0].Immediate[1].Target!.Pick == SelectionMode.Random && grit.Forms[1].Immediate[1].Target!.Pick == SelectionMode.Choose);
        var bash = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.Bash>());
        Check(bash.Status == NativeConversionStatus.Partial && bash.Definition!.Forms[1].Immediate[1].Amount!.Value == 3);
        var feed = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.Feed>()).Definition!;
        Check(feed.Forms[1].Immediate[0].Amount!.Value == 12 && feed.Forms[1].Immediate[0].OnFatal![0].Amount!.Value == 4);
        var gamble = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.RoyalGamble>()).Definition!;
        Check(gamble.Forms[0].Cost.Stars == 5 && gamble.Forms[1].Tags.Contains(ForgeKeyword.Retain));
        var hotfix = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.Hotfix>()).Definition!;
        Check(hotfix.Forms[0].Tags.Contains(ForgeKeyword.Exhaust) && !hotfix.Forms[1].Tags.Contains(ForgeKeyword.Exhaust));
        Check(hotfix.Forms[0].Immediate[0].Until == LifetimeKind.Turn);
        var slam = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.BodySlam>()).Definition!;
        Check(slam.Forms[0].Cost.Energy == 1 && slam.Forms[1].Cost.Energy == 0 && slam.Forms[0].Immediate[0].Amount!.Stat == "block");
        var whirlwind = NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.Whirlwind>());
        Check(whirlwind.Status == NativeConversionStatus.Partial && whirlwind.Definition!.Forms[0].Cost.EnergyX == true);
        Check(NativeCardConversion.Convert(Capture<MegaCrit.Sts2.Core.Models.Cards.Darkness>()).Status == NativeConversionStatus.Unsupported);
        foreach (var definition in new[] { armaments.Definition!, grit, bash.Definition!, feed, gamble, hotfix, slam, whirlwind.Definition! })
        {
            var generated = (NeowGeneratedCard)ModelDb.Card<NeowGeneratedCard>().ToMutable();
            generated.DefinitionPayload = Wire.Encode(definition);
            Check(generated.ActiveForm.Cost.Energy == definition.Forms[0].Cost.Energy);
            generated.UpgradeInternal(); generated.FinalizeUpgradeInternal();
            Check(Wire.Encode(generated.ActiveForm) == Wire.Encode(definition.Forms[1]));
        }
        Console.WriteLine("PASS native-to-protocol mapping preserves upgrade costs, keywords, selections, fatal rewards and generated-card reconstruction; gaps remain explicit");
    }
    private static bool SkipStartingInventory() => false;
    private static void Check(bool value) { if (!value) throw new Exception("Game contract assertion failed."); }
}
