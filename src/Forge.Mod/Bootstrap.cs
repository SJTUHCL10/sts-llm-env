using System.Reflection;
using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Forge.Mod;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    private static bool _initialized;
    internal static ForgeRuntime? Runtime { get; private set; }
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        var harmony = new Harmony("neowscompany.v1");
        try
        {
            string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string path = Path.Combine(root, "config.json");
            if (!File.Exists(path)) AtomicStore.Write(path, new ForgeConfig());
            ForgeConfig config;
            try
            {
                config = Wire.Decode<ForgeConfig>(File.ReadAllText(path));
                try { config.Validate(); }
                catch (FormatException ex)
                {
                    // Validation messages are local constants; never log file contents or parser errors.
                    Log.Error("[NeowsCompany] Generation disabled by invalid config: " + ex.Message);
                    config = new ForgeConfig { Enabled = false };
                }
            }
            catch (Exception ex)
            {
                config = new ForgeConfig { Enabled = false };
                Log.Error("[NeowsCompany] Generation disabled by invalid config: " + ex.GetType().Name);
            }
            VerifyCompatibility();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Runtime = new ForgeRuntime(root, config);
            Runtime.Subscribe();
            Log.Info("[NeowsCompany] Initialized for v0.111.0; timing=" + config.GenerationTiming);
        }
        catch (Exception ex)
        {
            harmony.UnpatchAll(harmony.Id);
            // Invalid configuration/version leaves vanilla rewards usable.
            Log.Error("[NeowsCompany] Startup disabled: " + ex.GetType().Name + "; check config.json and game version.");
        }
    }
    internal static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Warn("[NeowsCompany] Callback failed: " + ex.GetType().Name); }
    }

    internal static void VerifyCompatibility()
    {
        if (AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.OrbCmd), "Evoke",
            [typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext), typeof(MegaCrit.Sts2.Core.Entities.Players.Player), typeof(MegaCrit.Sts2.Core.Models.OrbModel), typeof(bool)]) is null)
            throw new MissingMethodException("OrbCmd.Evoke");
        if (AccessTools.PropertySetter(typeof(MegaCrit.Sts2.Core.Models.CardModel), "BaseStarCost") is null)
            throw new MissingMethodException("CardModel.BaseStarCost setter");
        foreach (var (type, name) in new[]
        {
            (typeof(MegaCrit.Sts2.Core.Runs.RunManager), "_startTime"),
            (typeof(MegaCrit.Sts2.Core.Models.CardModel), "_dynamicVars"),
            (typeof(MegaCrit.Sts2.Core.Models.CardModel), "_keywords"),
            (typeof(MegaCrit.Sts2.Core.Localization.LocTable), "_translations"),
            (typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "_cards"),
            (typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "<Options>k__BackingField"),
            (typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "_cardsWereManuallySet"),
            (typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "_currentlyShownScreen")
        })
            if (AccessTools.Field(type, name) is null) throw new MissingFieldException(type.FullName, name);
    }
}
