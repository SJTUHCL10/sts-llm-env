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
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Forge.Mod;

public sealed class NeowGeneratedCard : CardModel
{
    private static readonly FieldInfo VarsField = AccessTools.Field(typeof(CardModel), "_dynamicVars");
    private static readonly FieldInfo KeywordsField = AccessTools.Field(typeof(CardModel), "_keywords");
    private static readonly FieldInfo TagsField = AccessTools.Field(typeof(CardModel), "_tags");
    private static readonly MethodInfo StarCostSetter = AccessTools.PropertySetter(typeof(CardModel), nameof(BaseStarCost));
    private string _payload = "";
    private CardDefinition _definition = Fallback;
    public static CardDefinition Fallback => new()
    {
        Name = "铸塔护卫", Type = ForgeCardType.Skill, Rarity = ForgeRarity.Common,
        Forms = [new() { Cost = new() { Energy = 1 }, Effects = [new() { Kind = EffectKind.Block, Amount = 5 }] },
                 new() { Cost = new() { Energy = 1 }, Effects = [new() { Kind = EffectKind.Block, Amount = 8 }] }]
    };
    public CardDefinition Definition => _definition;
    public CardForm ActiveForm => _definition.Form(IsUpgraded);
    // Runtime identity belongs to persistence, never to the LLM card definition.
    [SavedProperty]
    public string InstanceKey { get; set; } = Guid.NewGuid().ToString("N");
    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        InstanceKey = Guid.NewGuid().ToString("N");
    }
    [SavedProperty]
    public string DefinitionPayload
    {
        get => _payload;
        set
        {
            AssertMutable();
            _definition = CardValidator.Validate(Wire.Decode<CardDefinition>(value));
            _payload = Wire.Encode(_definition);
            RebuildForm(false);
        }
    }
    public NeowGeneratedCard() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self, shouldShowInCardLibrary: false) { }
    protected override int CanonicalEnergyCost => HasEnergyCostX ? 0 : ActiveForm.Cost.Energy;
    protected override bool HasEnergyCostX => ActiveForm.Cost.EnergyX == true;
    public override bool HasStarCostX => ActiveForm.Cost.StarsX == true;
    public override int CanonicalStarCost => HasStarCostX ? 0 : ActiveForm.Cost.Stars ?? -1;
    public override string Title => _definition.Name + (IsUpgraded ? "+" : "");
    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();
    public override CardType Type => _definition.Type switch { ForgeCardType.Attack => CardType.Attack, ForgeCardType.Power => CardType.Power, _ => CardType.Skill };
    public override CardRarity Rarity => _definition.Rarity switch { ForgeRarity.Rare => CardRarity.Rare, ForgeRarity.Uncommon => CardRarity.Uncommon, _ => CardRarity.Common };
    public override TargetType TargetType => ActiveForm.Immediate.Any(e => e.Target?.Ref == "enemy") ? TargetType.AnyEnemy
        : ActiveForm.Immediate.Any(e => e.Target?.Ref == "all_enemies") ? TargetType.AllEnemies : TargetType.Self;
    public override bool GainsBlock => ActiveForm.AllEffects.Any(e => e.Kind == EffectKind.Block);
    public override string PortraitPath => MissingPortraitPath;
    public override string BetaPortraitPath => PortraitPath;
    public override IEnumerable<CardKeyword> CanonicalKeywords => ActiveForm.Tags.Select(NativeKeyword);
    protected override HashSet<CardTag> CanonicalTags => ActiveForm.AllEffects.Any(e => e.Actor == "osty") ? [CardTag.OstyAttack] : [];
    internal static CardKeyword NativeKeyword(ForgeKeyword keyword) => Enum.Parse<CardKeyword>(keyword.ToString());
    protected override IEnumerable<DynamicVar> CanonicalVars => ActiveForm.AllEffects.Select((e, i) => e.Kind switch
    {
        EffectKind.Damage => (DynamicVar)new GeneratedDamageVar($"E{i}", e.Amount?.Value ?? 0, e, i),
        EffectKind.Block => new GeneratedBlockVar($"E{i}", e.Amount?.Value ?? 0, i, i >= ActiveForm.Immediate.Length),
        _ => new GeneratedAmountVar($"E{i}", e.Amount?.Value ?? 0, i)
    });
    private void RebuildForm(bool preserveCost)
    {
        CardEnergyCost? oldCost = preserveCost ? EnergyCost : null;
        MockSetEnergyCost(new CardEnergyCost(this, CanonicalEnergyCost, HasEnergyCostX));
        if (oldCost is not null && oldCost.CostsX == HasEnergyCostX)
        {
            var copied = oldCost.Clone(this);
            copied.UpgradeBy(CanonicalEnergyCost - copied.GetWithModifiers(CostModifiers.None));
            copied.SetCustomBaseCost(CanonicalEnergyCost);
            MockSetEnergyCost(copied);
        }
        StarCostSetter.Invoke(this, [CanonicalStarCost]);
        var vars = new DynamicVarSet(CanonicalVars);
        vars.InitializeWithOwner(this);
        VarsField.SetValue(this, vars);
        KeywordsField.SetValue(this, null);
        TagsField.SetValue(this, null);
    }
    internal static IEnumerable<IHoverTip> EffectHoverTips(CardForm form)
    {
        foreach (var id in form.AllEffects.Where(e => e.Power is not null).Select(e => e.Power!).Distinct())
            yield return HoverTipFactory.FromPower(NativeMechanics.Power(id));
        foreach (var id in form.AllEffects.Where(e => e.Card?.Id is not null).Select(e => e.Card!.Id!).Distinct())
            yield return HoverTipFactory.FromCard(NativeMechanics.Card(id));
        foreach (var id in form.AllEffects.Where(e => e.Orb is not null && e.Orb != "random").Select(e => e.Orb!).Distinct())
            foreach (var tip in NativeMechanics.OrbTips(id)) yield return tip;
        if (form.AllEffects.Any(e => e.Kind == EffectKind.Forge)) foreach (var tip in HoverTipFactory.FromForge()) yield return tip;
        if (form.AllEffects.Any(e => e.Kind == EffectKind.Summon)) yield return HoverTipFactory.Static(StaticHoverTip.SummonStatic);
    }
    protected override IEnumerable<IHoverTip> ExtraHoverTips => EffectHoverTips(ActiveForm);
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var context = new GeneratedExecutionContext(this, choiceContext, cardPlay);
        await GeneratedEffectExecutor.ExecuteGroup(context, ActiveForm.Immediate, 0);
        if (ActiveForm.Listeners.Length > 0 && !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsEnding)
        {
            var power = (GeneratedEffectPower)ModelDb.Power<GeneratedEffectPower>().ToMutable();
            power.Configure(this, cardPlay);
            await PowerCmd.Apply(choiceContext, power, Owner.Creature, 1, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade()
    {
        var old = _definition.Forms[0];
        var local = GetKeywordsWithSources(KeywordSources.Local).ToHashSet();
        var extras = local.Except(old.Tags.Select(NativeKeyword)).ToArray();
        var removed = old.Tags.Select(NativeKeyword).Except(local).ToArray();
        RebuildForm(true);
        foreach (var keyword in extras) AddKeyword(keyword);
        foreach (var keyword in removed) RemoveKeyword(keyword);
        for (int i = 0; i < ActiveForm.AllEffects.Length; i++)
            if (i >= old.AllEffects.Length || Wire.Encode(old.AllEffects[i]) != Wire.Encode(ActiveForm.AllEffects[i]))
                ((IGeneratedPreviewVar)DynamicVars[$"E{i}"]).SetUpgradeHighlight(true);
    }
    protected override void AfterDowngraded() => RebuildForm(true);
    internal LocString RuntimeDescription()
    {
        bool chinese = LocManager.Instance.Language is "zhs" or "zht";
        string prefix = EnergyIconHelper.GetPrefix(this);
        string key = "NEOWS_COMPANY." + AtomicStore.Key(_payload + chinese + IsUpgraded + prefix) + ".description";
        string description = RuntimeDescriptionText(chinese);
        LocManager.Instance.GetTable("cards").MergeWith(new Dictionary<string, string> { [key] = description });
        return new LocString("cards", key);
    }
    internal string RuntimeDescriptionText(bool chinese) => CardText.Render(ActiveForm, chinese,
        i => ActiveForm.AllEffects[i].Amount?.Value is null && !GeneratedEffectPreview.CanResolve(this, i) ? CardText.Number(ActiveForm.AllEffects[i].Amount, chinese, resource: (kind, value) => CardText.ResourceText(kind, value, EnergyIconHelper.GetPrefix(this)))
            : ActiveForm.AllEffects[i].Kind is EffectKind.GainEnergy or EffectKind.GainStars or EffectKind.SetCost or EffectKind.OrbSlots ? DynamicVars[$"E{i}"].ToHighlightedString(false) : $"{{E{i}:diff()}}",
        _definition.Type, (kind, value) => CardText.ResourceText(kind, value, EnergyIconHelper.GetPrefix(this)));
    internal LocString RuntimeTitle()
    {
        string key = "NEOWS_COMPANY." + AtomicStore.Key(_payload) + ".title";
        LocManager.Instance.GetTable("cards").MergeWith(new Dictionary<string, string> { [key] = _definition.Name });
        return new LocString("cards", key);
    }
    internal bool[] BeginUpgradePreview()
    {
        var previous = DynamicVars.Values.Select(v => v.WasJustUpgraded).ToArray();
        var before = _definition.Forms[0].AllEffects;
        for (int i = 0; i < ActiveForm.AllEffects.Length; i++)
            ((IGeneratedPreviewVar)DynamicVars[$"E{i}"]).SetUpgradeHighlight(IsUpgraded && (i >= before.Length || Wire.Encode(before[i]) != Wire.Encode(ActiveForm.AllEffects[i])));
        return previous;
    }
    internal void EndUpgradePreview(bool[] previous)
    {
        for (int i = 0; i < previous.Length; i++) ((IGeneratedPreviewVar)DynamicVars[$"E{i}"]).SetUpgradeHighlight(previous[i]);
    }
}
[HarmonyPatch(typeof(CardModel), nameof(CardModel.GetDescriptionForUpgradePreview))]
internal static class GeneratedUpgradePreviewPatch
{
    private static void Prefix(CardModel __instance, out bool[]? __state) => __state = __instance is NeowGeneratedCard card ? card.BeginUpgradePreview() : null;
    private static void Finalizer(CardModel __instance, bool[]? __state) { if (__instance is NeowGeneratedCard card && __state is not null) card.EndUpgradePreview(__state); }
}
[HarmonyPatch(typeof(CardModel), nameof(CardModel.Description), MethodType.Getter)]
internal static class GeneratedDescriptionPatch
{
    private static void Postfix(CardModel __instance, ref LocString __result) { if (__instance is NeowGeneratedCard card) __result = card.RuntimeDescription(); }
}
[HarmonyPatch(typeof(CardModel), nameof(CardModel.TitleLocString), MethodType.Getter)]
internal static class GeneratedTitlePatch
{
    private static void Postfix(CardModel __instance, ref LocString __result) { if (__instance is NeowGeneratedCard card) __result = card.RuntimeTitle(); }
}
