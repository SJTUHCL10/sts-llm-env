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
    private int _upgradePreviewDepth;
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
        _upgradePreviewDepth = 0;
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
    public override CardPoolModel Pool => (IsMutable ? Owner?.Character.CardPool : null) ?? ModelDb.CardPool<TokenCardPool>();
    public override CardType Type => _definition.Type switch { ForgeCardType.Attack => CardType.Attack, ForgeCardType.Power => CardType.Power, _ => CardType.Skill };
    public override CardRarity Rarity => _definition.Rarity switch { ForgeRarity.Rare => CardRarity.Rare, ForgeRarity.Uncommon => CardRarity.Uncommon, _ => CardRarity.Common };
    public override TargetType TargetType => ActiveForm.ImmediateEffects.Any(e => e.Target?.Ref == "enemy") ? TargetType.AnyEnemy
        : ActiveForm.ImmediateEffects.Any(e => e.Target?.Ref == "all_enemies") ? TargetType.AllEnemies : TargetType.Self;
    // Listener block is produced by a power, not by playing this card.
    public override bool GainsBlock => ActiveForm.ImmediateEffects.Any(e => e.Kind == EffectKind.Block);
    public override string PortraitPath => MissingPortraitPath;
    public override string BetaPortraitPath => PortraitPath;
    public override IEnumerable<CardKeyword> CanonicalKeywords => ActiveForm.Tags.Select(NativeKeyword);
    protected override HashSet<CardTag> CanonicalTags => ActiveForm.AllEffects.Any(e => e.Actor == "osty") ? [CardTag.OstyAttack] : [];
    internal static CardKeyword NativeKeyword(ForgeKeyword keyword) => Enum.Parse<CardKeyword>(keyword.ToString());
    protected override IEnumerable<DynamicVar> CanonicalVars => CreateVars();
    private IEnumerable<DynamicVar> CreateVars()
    {
        for (int i = 0; i < ActiveForm.AllEffects.Length; i++)
        {
            var e = ActiveForm.AllEffects[i];
            yield return e.Kind switch
            {
                EffectKind.Damage => (DynamicVar)new GeneratedDamageVar($"E{i}", e.Amount?.Value ?? 0, e, i),
                EffectKind.Block => new GeneratedBlockVar($"E{i}", e.Amount?.Value ?? 0, i, i >= ActiveForm.ImmediateEffects.Length),
                _ => new GeneratedAmountVar($"E{i}", e.Amount?.Value ?? 0, i)
            };
            if (e.Kind is EffectKind.CreateCard or EffectKind.Copy) yield return new GeneratedCountVar($"C{i}", e.Count, i);
            if (e.Repeat is not null) yield return new GeneratedCountVar($"R{i}", e.Repeat, i);
            if (e.Target?.Pile is not null && e.Target.Pick is not (SelectionMode.All or SelectionMode.Any)) yield return new GeneratedCountVar($"T{i}", e.Target.Count, i);
        }
        for (int i = 0; i < ActiveForm.Listeners.Length; i++)
            if (ActiveForm.Listeners[i].Trigger.Limit is { } limit) yield return new GeneratedRuleLimitVar($"L{i}", limit.Count);
    }
    internal static NumberExpression? NumericSlot(CardEffect effect, char slot) => slot switch
    {
        'C' => effect.Count, 'R' => effect.Repeat, 'T' => effect.Target?.Count, _ => effect.Amount
    };
    private bool ChangedNumericSlot(string name)
    {
        int index = int.Parse(name[1..], System.Globalization.CultureInfo.InvariantCulture);
        if (name[0] == 'L')
            return index >= _definition.Forms[0].Listeners.Length
                || _definition.Forms[0].Listeners[index].Trigger.Limit?.Count != ActiveForm.Listeners[index].Trigger.Limit?.Count;
        var before = _definition.Forms[0].AllEffects;
        if (index >= before.Length) return true;
        var current = ActiveForm.AllEffects[index];
        var old = before[index];
        var left = NumericSlot(old, name[0]); var right = NumericSlot(current, name[0]);
        return old.Kind != current.Kind || Wire.Encode(left ?? (NumberExpression)(name[0] == 'E' ? 0 : 1))
            != Wire.Encode(right ?? (NumberExpression)(name[0] == 'E' ? 0 : 1));
    }
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
        foreach (var id in form.AllEffects.Where(e => e.Power is not null).Select(e => e.Power!).Concat(form.Listeners.Where(r => r.Trigger.Where?.Power is not null).Select(r => r.Trigger.Where!.Power!)).Distinct())
            yield return HoverTipFactory.FromPower(NativeMechanics.Power(id));
        foreach (var (id, upgraded) in form.AllEffects.Where(e => e.Card?.Id is not null).Select(e => (e.Card!.Id!, e.Card.Upgraded == true)).Distinct())
            yield return HoverTipFactory.FromCard(NativeMechanics.Card(id), upgraded);
        foreach (var id in form.AllEffects.Where(e => e.Orb is not null && e.Orb != "random").Select(e => e.Orb!).Concat(form.Listeners.Where(r => r.Trigger.Where?.Orb is not null).Select(r => r.Trigger.Where!.Orb!)).Distinct())
            foreach (var tip in NativeMechanics.OrbTips(id)) yield return tip;
        if (form.AllEffects.Any(e => e.Kind == EffectKind.Forge) || form.Listeners.Any(r => r.Trigger.Event == RuleEvent.Forged)) foreach (var tip in HoverTipFactory.FromForge()) yield return tip;
        if (form.AllEffects.Any(e => e.Kind == EffectKind.Summon)) yield return HoverTipFactory.Static(StaticHoverTip.SummonStatic);
        if (form.AllEffects.Any(e => e.OnFatal is not null)) yield return HoverTipFactory.Static(StaticHoverTip.Fatal);
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
        foreach (var variable in DynamicVars)
            ((IGeneratedPreviewVar)variable.Value).SetUpgradeHighlight(ChangedNumericSlot(variable.Key));
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
        i => ActiveForm.AllEffects[i].Amount?.Value is null && !GeneratedEffectPreview.CanResolve(this, i) ? FormulaText(i, 'E', ActiveForm.AllEffects[i].Amount, chinese, 1)
            : ActiveForm.AllEffects[i].Kind == EffectKind.Draw ? DrawAmountText(DynamicVars[$"E{i}"])
            : ActiveForm.AllEffects[i].Kind is EffectKind.GainEnergy or EffectKind.GainStars or EffectKind.SetCost or EffectKind.OrbSlots ? DynamicVars[$"E{i}"].ToHighlightedString(false) : $"{{E{i}:diff()}}",
        _definition.Type, (kind, value) => CardText.ResourceText(kind, value, EnergyIconHelper.GetPrefix(this)),
        (i, slot, value, fallback) => value?.Value is null && value is not null && !GeneratedEffectPreview.CanResolve(this, i)
            ? FormulaText(i, slot == "count" ? 'C' : slot == "repeat" ? 'R' : 'T', value, chinese, fallback)
            : $"{{{(slot == "count" ? 'C' : slot == "repeat" ? 'R' : 'T')}{i}:diff()}}",
        limit: (i, _) => $"{{L{i}:diff()}}",
        upgradeBaseline: IsUpgraded && _upgradePreviewDepth > 0 ? _definition.Forms[0] : null);
    private string FormulaText(int index, char slot, NumberExpression? value, bool chinese, int fallback)
    {
        string Resource(string kind, string amount) => CardText.ResourceText(kind, amount, EnergyIconHelper.GetPrefix(this));
        if (!IsUpgraded || !DynamicVars[$"{slot}{index}"].WasJustUpgraded)
            return CardText.Number(value, chinese, fallback, Resource, ActiveForm.Cost);
        var before = _definition.Forms[0].AllEffects;
        var baseline = index < before.Length && before[index].Kind == ActiveForm.AllEffects[index].Kind ? NumericSlot(before[index], slot) : null;
        return CardText.NumberUpgrade(value, baseline, chinese, fallback, Resource, ActiveForm.Cost);
    }
    private static string DrawAmountText(DynamicVar variable)
    {
        decimal value = decimal.Ceiling(Math.Max(0, variable.PreviewValue));
        decimal baseline = decimal.Ceiling(Math.Max(0, variable.EnchantedValue));
        return MegaCrit.Sts2.Core.TextEffects.StsTextUtilities.HighlightChangeText(
            value.ToString(System.Globalization.CultureInfo.InvariantCulture), variable.WasJustUpgraded ? 1 : value.CompareTo(baseline));
    }
    internal LocString RuntimeTitle()
    {
        string key = "NEOWS_COMPANY." + AtomicStore.Key(_payload) + ".title";
        LocManager.Instance.GetTable("cards").MergeWith(new Dictionary<string, string> { [key] = _definition.Name });
        return new LocString("cards", key);
    }
    internal bool[] BeginUpgradePreview()
    {
        var previous = DynamicVars.Values.Select(v => v.WasJustUpgraded).ToArray();
        _upgradePreviewDepth++;
        foreach (var variable in DynamicVars)
            ((IGeneratedPreviewVar)variable.Value).SetUpgradeHighlight(IsUpgraded && ChangedNumericSlot(variable.Key));
        return previous;
    }
    internal void EndUpgradePreview(bool[] previous)
    {
        _upgradePreviewDepth--;
        int index = 0;
        foreach (var variable in DynamicVars.Values) ((IGeneratedPreviewVar)variable).SetUpgradeHighlight(previous[index++]);
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
