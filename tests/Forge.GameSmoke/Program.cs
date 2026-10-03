using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Forge.Core;
using Forge.Mod;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
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
        ModelDb.Init([typeof(NeowGeneratedCard)]);
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

        var harmony = new Harmony("neowscompany.tests");
        typeof(Bootstrap).GetMethod("VerifyCompatibility", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        harmony.PatchAll(typeof(Bootstrap).Assembly);
        var original = typeof(MegaCrit.Sts2.Core.Rewards.CardReward).GetMethod("OnSelect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(Harmony.GetPatchInfo(original)!.Prefixes.Any(p => p.owner == harmony.Id));
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("PASS Harmony targets and reverse-patch installation against real game DLL");
        Console.WriteLine("PASS: 5 game API smoke checks (UI/live combat not exercised)");
    }
    private static void Check(bool value) { if (!value) throw new Exception("Game contract assertion failed."); }
}
