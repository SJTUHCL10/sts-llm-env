using System.Reflection;
using Forge.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
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
            var vars = new DynamicVarSet(CanonicalVars);
            vars.InitializeWithOwner(this);
            VarsField.SetValue(this, vars);
            KeywordsField.SetValue(this, null);
        }
    }

    public NeowGeneratedCard() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self, shouldShowInCardLibrary: false) { }
    protected override int CanonicalEnergyCost => _definition.Cost;
    public override string Title => _definition.Name + (IsUpgraded ? "+" : "");
    // Borrow only visual metadata; do not insert fallback models into any original card pool.
    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();
    public override CardType Type => _definition.Type == ForgeCardType.Attack ? CardType.Attack : CardType.Skill;
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
        EffectKind.Damage => (DynamicVar)new DamageVar($"E{i}", e.Amount, ValueProp.Move),
        EffectKind.Block => new BlockVar($"E{i}", e.Amount, ValueProp.Move),
        _ => new DynamicVar($"E{i}", e.Amount)
    });

    protected override IEnumerable<IHoverTip> ExtraHoverTips => _definition.Effects.Select(e => e.Kind).Distinct().SelectMany(k => k switch
    {
        EffectKind.Strength => new[] { HoverTipFactory.FromPower<StrengthPower>() },
        EffectKind.Dexterity => new[] { HoverTipFactory.FromPower<DexterityPower>() },
        EffectKind.Weak => new[] { HoverTipFactory.FromPower<WeakPower>() },
        EffectKind.Vulnerable => new[] { HoverTipFactory.FromPower<VulnerablePower>() },
        EffectKind.Poison => new[] { HoverTipFactory.FromPower<PoisonPower>() }, _ => Array.Empty<IHoverTip>()
    });

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        for (int i = 0; i < _definition.Effects.Length; i++)
        {
            var effect = _definition.Effects[i];
            var variable = DynamicVars[$"E{i}"];
            Creature[] targets = effect.Target switch
            {
                EffectTarget.Self => [Owner.Creature],
                EffectTarget.Enemy => cardPlay.Target is { IsAlive: true } enemy ? [enemy] : [],
                EffectTarget.AllEnemies => CombatState!.Enemies.Where(e => e.IsAlive).ToArray(),
                _ => throw new InvalidOperationException()
            };
            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    if (targets.Length > 0)
                    {
                        var attack = DamageCmd.Attack(variable.BaseValue).FromCard(this, cardPlay);
                        if (effect.Target == EffectTarget.AllEnemies) attack.TargetingAllOpponents(CombatState!);
                        else attack.Targeting(targets[0]);
                        await attack.WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(choiceContext);
                    }
                    break;
                case EffectKind.Block: await CreatureCmd.GainBlock(Owner.Creature, (BlockVar)variable, cardPlay); break;
                case EffectKind.Draw: await CardPileCmd.Draw(choiceContext, variable.BaseValue, Owner); break;
                case EffectKind.Energy: await PlayerCmd.GainEnergy(variable.BaseValue, Owner); break;
                default:
                    foreach (var target in targets)
                        await ApplyPower(choiceContext, effect.Kind, target, variable.BaseValue);
                    break;
            }
        }
    }

    private Task ApplyPower(PlayerChoiceContext choice, EffectKind kind, Creature target, decimal amount) => kind switch
    {
        EffectKind.Strength => PowerCmd.Apply<StrengthPower>(choice, target, amount, Owner.Creature, this),
        EffectKind.Dexterity => PowerCmd.Apply<DexterityPower>(choice, target, amount, Owner.Creature, this),
        EffectKind.Weak => PowerCmd.Apply<WeakPower>(choice, target, amount, Owner.Creature, this),
        EffectKind.Vulnerable => PowerCmd.Apply<VulnerablePower>(choice, target, amount, Owner.Creature, this),
        EffectKind.Poison => PowerCmd.Apply<PoisonPower>(choice, target, amount, Owner.Creature, this),
        _ => throw new InvalidOperationException()
    };
    protected override void OnUpgrade()
    {
        for (int i = 0; i < _definition.Effects.Length; i++) DynamicVars[$"E{i}"].UpgradeValueBy(_definition.Effects[i].UpgradeAmount);
    }

    protected override void AfterDowngraded()
    {
        // Native downgrade restores the shared canonical model's variables; restore this instance's definition.
        var vars = new DynamicVarSet(CanonicalVars);
        vars.InitializeWithOwner(this);
        VarsField.SetValue(this, vars);
        KeywordsField.SetValue(this, null);
    }

    internal LocString RuntimeDescription()
    {
        bool chinese = LocManager.Instance.Language is "zhs" or "zht";
        string key = "LLM_SPIRE_FORGE." + AtomicStore.Key(_payload + chinese) + ".description";
        var table = LocManager.Instance.GetTable("cards");
        var translations = (Dictionary<string, string>)AccessTools.Field(typeof(LocTable), "_translations").GetValue(table)!;
        if (!translations.ContainsKey(key)) translations[key] = RenderTemplate(chinese);
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

    private string RenderTemplate(bool chinese) => string.Join("\n", _definition.Effects.Select((e, i) =>
    {
        string n = $"{{E{i}:diff()}}";
        string target = e.Target == EffectTarget.AllEnemies ? (chinese ? "所有敌人" : "ALL enemies") : (chinese ? "目标敌人" : "the enemy");
        if (chinese) return e.Kind switch
        {
            EffectKind.Damage => $"对{target}造成 {n} 点伤害。", EffectKind.Block => $"获得 {n} 点[gold]格挡[/gold]。",
            EffectKind.Draw => $"抽 {n} 张牌。", EffectKind.Energy => $"获得 {n} 点能量。",
            EffectKind.Strength => $"获得 {n} 层[gold]力量[/gold]。", EffectKind.Dexterity => $"获得 {n} 层[gold]敏捷[/gold]。",
            EffectKind.Weak => $"给予{target} {n} 层[gold]虚弱[/gold]。", EffectKind.Vulnerable => $"给予{target} {n} 层[gold]易伤[/gold]。",
            EffectKind.Poison => $"给予{target} {n} 层[gold]中毒[/gold]。", _ => throw new InvalidOperationException()
        };
        return e.Kind switch
        {
            EffectKind.Damage => $"Deal {n} damage to {target}.", EffectKind.Block => $"Gain {n} [gold]Block[/gold].",
            EffectKind.Draw => $"Draw {n} cards.", EffectKind.Energy => $"Gain {n} Energy.",
            EffectKind.Strength => $"Gain {n} [gold]Strength[/gold].", EffectKind.Dexterity => $"Gain {n} [gold]Dexterity[/gold].",
            EffectKind.Weak => $"Apply {n} [gold]Weak[/gold] to {target}.", EffectKind.Vulnerable => $"Apply {n} [gold]Vulnerable[/gold] to {target}.",
            EffectKind.Poison => $"Apply {n} [gold]Poison[/gold] to {target}.", _ => throw new InvalidOperationException()
        };
    }));
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
