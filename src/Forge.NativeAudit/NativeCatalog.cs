using System.Reflection;
using System.Runtime.CompilerServices;
using Forge.Core;

namespace Forge.NativeAudit;

internal static class NativeCatalog
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Write(string game, string output, string language, string? filter)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = true;
        // Outside a run the native formatter otherwise logs a missing energy-prefix warning via Godot.
        var headless = new HarmonyLib.Harmony("neowscompany.nativeaudit.headless");
        headless.Patch(typeof(MegaCrit.Sts2.Core.Runs.RunManager).GetMethod("GetLocalCharacterEnergyIconPrefix")!,
            prefix: new HarmonyLib.HarmonyMethod(typeof(NativeCatalog).GetMethod(nameof(EnergyPrefix), BindingFlags.Static | BindingFlags.NonPublic)!));
        InitializeLocalization(PckLocalization.Read(Path.Combine(game, "SlayTheSpire2.pck"), language), language);
        var assembly = typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly;
        var registry = assembly.GetTypes().Single(t => t.Name == "AbstractModelSubtypes");
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var types = (IEnumerable<Type>)(registry.GetProperty("All", flags)?.GetValue(null)
            ?? registry.GetField("All", flags)!.GetValue(null))!;
        MegaCrit.Sts2.Core.Models.ModelDb.Init(types.ToArray());
        var cards = MegaCrit.Sts2.Core.Models.ModelDb.All.OfType<MegaCrit.Sts2.Core.Models.CardModel>()
            .Where(c => c.GetType().Namespace == "MegaCrit.Sts2.Core.Models.Cards")
            .Where(c => filter is null || c.Id.Entry.Equals(filter, StringComparison.OrdinalIgnoreCase)
                || c.GetType().Name.Equals(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Id.Entry).ToArray();
        if (cards.Length == 0) throw new ArgumentException("No native card matches the supplied ID/class.");
        var entries = cards.Select(Capture).ToArray();
        NativeAuditReport.Write(Path.GetFullPath(output), language, entries);
        foreach (var group in entries.GroupBy(e => e.Conversion.Status)) Console.WriteLine($"{group.Key}: {group.Count()}");
        Console.WriteLine($"Native audit: {cards.Length} models. Report: {Path.GetFullPath(Path.Combine(output, "report.md"))}");
        // Missing text is visible in the report. A mapping/capture error is a failed audit.
        return entries.Any(e => e.Conversion.Status == NativeConversionStatus.Error
            || e.Conversion.Definition is not null && e.Source!.Forms.Any(f => f.TextError is not null)) ? 1 : 0;
    }

    private static bool EnergyPrefix(ref string __result) { __result = "character"; return false; }

    private static NativeAuditEntry Capture(MegaCrit.Sts2.Core.Models.CardModel canonical)
    {
        try
        {
            var card = (MegaCrit.Sts2.Core.Models.CardModel)canonical.ToMutable();
            // Materialize variables/cost/keywords before OnUpgrade, exactly as the game does.
            var before = CaptureForm(card);
            bool upgradable = card.IsUpgradable;
            if (upgradable) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            var snapshot = new NativeCardSnapshot
            {
                Id = canonical.Id.Entry, ClassName = canonical.GetType().Name,
                Name = canonical.Title, Type = canonical.Type.ToString(), Rarity = canonical.Rarity.ToString(),
                Pool = Pool(canonical), MultiplayerOnly = canonical.MultiplayerConstraint.ToString() == "MultiplayerOnly",
                Upgradable = upgradable, Forms = [before, CaptureForm(card)]
            };
            return new(snapshot.Id, snapshot.ClassName, snapshot, NativeCardConversion.Convert(snapshot));
        }
        catch (Exception ex)
        {
            return new(canonical.Id.Entry, canonical.GetType().Name, null,
                new(NativeConversionStatus.Error, null, ["原生快照失败：" + ex.GetBaseException().GetType().Name]));
        }
    }

    private static string? Pool(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        try { return card.Pool.Id.Entry; }
        catch (InvalidProgramException) { return null; } // Deprecated models may have no pool.
    }

    private static NativeCardForm CaptureForm(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        var values = card.DynamicVars.ToDictionary(v => v.Key, v => v.Value.BaseValue);
        int energy = card.EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.None);
        string? description = null, textError = null;
        try { description = card.GetDescriptionForPile(MegaCrit.Sts2.Core.Entities.Cards.PileType.Deck); }
        catch (Exception ex) { textError = ex.GetBaseException().GetType().Name; }
        return new()
        {
            Cost = new() { Energy = energy, EnergyX = card.EnergyCost.CostsX ? true : null,
                Stars = card.BaseStarCost >= 0 ? card.BaseStarCost : null, StarsX = card.HasStarCostX ? true : null },
            Variables = values, Keywords = card.Keywords.Select(k => k.ToString()).Order().ToArray(),
            Tags = card.Tags.Select(t => t.ToString()).Order().ToArray(), Target = card.TargetType.ToString(),
            Description = description, TextError = textError
        };
    }

    private static void InitializeLocalization(Dictionary<string, Dictionary<string, string>> tables, string language)
    {
        // Headless initialization: use the game's formatters, without Godot assets, saves or a running game.
        var type = typeof(MegaCrit.Sts2.Core.Localization.LocManager);
        var localization = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_tables", PrivateInstance)!.SetValue(localization, tables.ToDictionary(p => p.Key,
            p => new MegaCrit.Sts2.Core.Localization.LocTable(p.Key, p.Value)));
        type.GetProperty("Language")!.GetSetMethod(true)!.Invoke(localization, [language]);
        type.GetProperty("CultureInfo")!.GetSetMethod(true)!.Invoke(localization, [System.Globalization.CultureInfo.InvariantCulture]);
        type.GetMethod("LoadLocFormatters", PrivateInstance)!.Invoke(localization, null);
        type.GetProperty("Instance")!.GetSetMethod(true)!.Invoke(null, [localization]);
    }
}
