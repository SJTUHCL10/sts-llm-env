using System.Reflection;
using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Forge.Mod;

public sealed class NeowGeneratedCard : CardModel
{
    private static readonly FieldInfo VarsField = AccessTools.Field(typeof(CardModel), "_dynamicVars");
    private static readonly FieldInfo KeywordsField = AccessTools.Field(typeof(CardModel), "_keywords");
    private static readonly MethodInfo StarCostSetter = AccessTools.PropertySetter(typeof(CardModel), nameof(BaseStarCost));
    private string _payload = "";
    private CardDefinition _definition = Fallback;
    public static CardDefinition Fallback => new()
    {
        Name = "铸塔护卫", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Common, Cost = 1,
        Effects = [new() { Kind = EffectKind.Block, Target = EffectTarget.Self, Amount = 5, UpgradeAmount = 3 }]
    };
    public CardDefinition Definition => _definition;

    // SavedProperty is handled by the game's native card serializer (deck, duplicates and combat clones).
    [SavedProperty]
    public string DefinitionPayload
    {
        get => _payload;
        set
        {
            AssertMutable();
            var definition = CardValidator.Validate(Wire.Decode<CardDefinition>(value));
            _definition = definition;
            _payload = Wire.Encode(definition);
            // Mutable clones have already initialized the canonical fallback's caches.
            MockSetEnergyCost(new CardEnergyCost(this, definition.Cost, false));
            StarCostSetter.Invoke(this, [definition.StarCost]);
            var vars = new DynamicVarSet(CanonicalVars);
            vars.InitializeWithOwner(this);
            VarsField.SetValue(this, vars);
            KeywordsField.SetValue(this, null);
        }
    }

    public NeowGeneratedCard() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self, shouldShowInCardLibrary: false) { }
    protected override int CanonicalEnergyCost => _definition.Cost;
    public override int CanonicalStarCost => _definition.StarCost;
    public override string Title => _definition.Name + (IsUpgraded ? "+" : "");
    // Borrow only visual metadata; do not insert fallback models into any original card pool.
    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();
    public override CardType Type => _definition.Type switch
    { ForgeCardType.Attack => CardType.Attack, ForgeCardType.Power => CardType.Power, _ => CardType.Skill };
    public override CardRarity Rarity => _definition.Rarity switch
    { ForgeRarity.Rare => CardRarity.Rare, ForgeRarity.Uncommon => CardRarity.Uncommon, _ => CardRarity.Common };
    public override TargetType TargetType => _definition.Effects.Any(e => e.Target == EffectTarget.Enemy) ? TargetType.AnyEnemy
        : _definition.Effects.Any(e => e.Target == EffectTarget.AllEnemies) ? TargetType.AllEnemies : TargetType.Self;
    public override bool GainsBlock => _definition.Effects.Any(e => e.Kind == EffectKind.Block);
    public override string PortraitPath => MissingPortraitPath;
    public override string BetaPortraitPath => PortraitPath;
    public override IEnumerable<CardKeyword> CanonicalKeywords => _definition.Keywords.Select(k => k switch
    { ForgeKeyword.Exhaust => CardKeyword.Exhaust, ForgeKeyword.Ethereal => CardKeyword.Ethereal,
      ForgeKeyword.Innate => CardKeyword.Innate, ForgeKeyword.Retain => CardKeyword.Retain, _ => throw new InvalidOperationException() });

    protected override IEnumerable<DynamicVar> CanonicalVars => _definition.Effects.Select((e, i) => e.Kind switch
    {
        EffectKind.Damage => (DynamicVar)new GeneratedDamageVar($"E{i}", e.Amount, e, i),
        EffectKind.Block => new GeneratedBlockVar($"E{i}", e.Amount, i),
        _ => new GeneratedAmountVar($"E{i}", e.Amount, i)
    });

    internal static IEnumerable<IHoverTip> EffectHoverTips(CardDefinition definition) => definition.Effects
        .SelectMany(e => new[] { e.Kind,
            e.Condition == EffectCondition.TargetWeak ? EffectKind.Weak : e.Condition == EffectCondition.TargetVulnerable ? EffectKind.Vulnerable : e.Kind,
            e.Scaling == EffectScaling.TargetPoison ? EffectKind.Poison : e.Kind }).Distinct().SelectMany(k => k switch
    {
        EffectKind.Strength => new[] { HoverTipFactory.FromPower<StrengthPower>() },
        EffectKind.Dexterity => new[] { HoverTipFactory.FromPower<DexterityPower>() },
        EffectKind.Weak => new[] { HoverTipFactory.FromPower<WeakPower>() },
        EffectKind.Vulnerable => new[] { HoverTipFactory.FromPower<VulnerablePower>() },
        EffectKind.Poison => new[] { HoverTipFactory.FromPower<PoisonPower>() }, _ => Array.Empty<IHoverTip>()
    });

    protected override IEnumerable<IHoverTip> ExtraHoverTips => EffectHoverTips(_definition);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var combat = Owner.Creature.CombatState!;
        for (int i = 0; i < _definition.Effects.Length; i++)
            if (_definition.Effects[i].Trigger == EffectTrigger.OnPlay)
                await GeneratedEffectExecutor.Execute(this, _definition.Effects[i], DynamicVars[$"E{i}"].BaseValue,
                    combat, choiceContext, cardPlay);
        if (_definition.Effects.Any(e => e.Trigger != EffectTrigger.OnPlay))
        {
            var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable();
            power.Configure(this);
            await PowerCmd.Apply(choiceContext, power, Owner.Creature, 1, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade()
    {
        for (int i = 0; i < _definition.Effects.Length; i++)
            if (_definition.Effects[i].UpgradeAmount != 0) DynamicVars[$"E{i}"].UpgradeValueBy(_definition.Effects[i].UpgradeAmount);
        if (_definition.UpgradeCost != 0) EnergyCost.UpgradeBy(-_definition.UpgradeCost);
        if (_definition.UpgradeStarCost != 0) UpgradeStarCostBy(-_definition.UpgradeStarCost);
    }

    protected override void AfterDowngraded()
    {
        // Native downgrade restores the shared canonical model's variables; restore this instance's definition.
        var vars = new DynamicVarSet(CanonicalVars);
        vars.InitializeWithOwner(this);
        VarsField.SetValue(this, vars);
        KeywordsField.SetValue(this, null);
        StarCostSetter.Invoke(this, [_definition.StarCost]);
    }

    internal LocString RuntimeDescription()
    {
        bool chinese = LocManager.Instance.Language is "zhs" or "zht";
        bool scaledPreview = CombatState is not null && (Pile?.Type is PileType.Hand or PileType.Play || UpgradePreviewType == CardUpgradePreviewType.Combat);
        string key = "NEOWS_COMPANY." + AtomicStore.Key(_payload + chinese + scaledPreview) + ".description";
        var table = LocManager.Instance.GetTable("cards");
        var translations = (Dictionary<string, string>)AccessTools.Field(typeof(LocTable), "_translations").GetValue(table)!;
        if (!translations.ContainsKey(key)) translations[key] = RenderTemplate(chinese, scaledPreview);
        return new LocString("cards", key);
    }

    internal LocString RuntimeTitle()
    {
        string key = "NEOWS_COMPANY." + AtomicStore.Key(_payload) + ".title";
        var table = LocManager.Instance.GetTable("cards");
        var translations = (Dictionary<string, string>)AccessTools.Field(typeof(LocTable), "_translations").GetValue(table)!;
        translations.TryAdd(key, _definition.Name);
        return new LocString("cards", key);
    }

    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        for (int i = 0; i < _definition.Effects.Length; i++)
        {
            var effect = _definition.Effects[i];
            if (effect.Kind is not (EffectKind.Stars or EffectKind.Energy)) continue;
            var variable = DynamicVars[$"E{i}"];
            string path = effect.Kind == EffectKind.Stars ? "res://images/packed/sprite_fonts/star_icon.png"
                : $"res://images/packed/sprite_fonts/{EnergyIconHelper.GetPrefix(this)}_energy_icon.png";
            description.Add($"R{i}", CardText.ResourceIcons((int)variable.PreviewValue, variable.ToHighlightedString(false), path));
        }
    }

    internal bool[] BeginUpgradePreview()
    {
        var previous = DynamicVars.Values.Select(v => v.WasJustUpgraded).ToArray();
        for (int i = 0; i < _definition.Effects.Length; i++)
            ((IGeneratedPreviewVar)DynamicVars[$"E{i}"]).SetUpgradeHighlight(IsUpgraded && _definition.Effects[i].UpgradeAmount > 0);
        return previous;
    }

    internal void EndUpgradePreview(bool[] previous)
    {
        for (int i = 0; i < previous.Length; i++)
            ((IGeneratedPreviewVar)DynamicVars[$"E{i}"]).SetUpgradeHighlight(previous[i]);
    }

    private string RenderTemplate(bool chinese, bool scaledPreview) => CardText.Render(_definition, chinese,
        i => _definition.Effects[i].Kind is EffectKind.Stars or EffectKind.Energy ? $"{{R{i}}}" : $"{{E{i}:diff()}}",
        resourceIcons: true, scaledPreview: scaledPreview);
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.GetDescriptionForUpgradePreview))]
internal static class GeneratedUpgradePreviewPatch
{
    private static void Prefix(CardModel __instance, out bool[]? __state) =>
        __state = __instance is NeowGeneratedCard card ? card.BeginUpgradePreview() : null;
    private static void Finalizer(CardModel __instance, bool[]? __state)
    {
        if (__instance is NeowGeneratedCard card && __state is not null) card.EndUpgradePreview(__state);
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Description), MethodType.Getter)]
internal static class GeneratedDescriptionPatch
{
    private static void Postfix(CardModel __instance, ref LocString __result)
    { if (__instance is NeowGeneratedCard card) __result = card.RuntimeDescription(); }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.TitleLocString), MethodType.Getter)]
internal static class GeneratedTitlePatch
{
    private static void Postfix(CardModel __instance, ref LocString __result)
    { if (__instance is NeowGeneratedCard card) __result = card.RuntimeTitle(); }
}
