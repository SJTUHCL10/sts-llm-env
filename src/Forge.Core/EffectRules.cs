namespace Forge.Core;

public static class EffectRules
{
    public static bool IsEvent(EffectTrigger trigger) => trigger is EffectTrigger.CardPlayed or EffectTrigger.AttackPlayed
        or EffectTrigger.SkillPlayed or EffectTrigger.CardDrawn or EffectTrigger.CardExhausted;
    public static int MaximumAmount(CardEffect effect) => effect.Amount + effect.UpgradeAmount + effect.ScalingAmount * effect.ScalingCap;
    public static int ResolveAmount(CardEffect effect, int baseAmount, int units) =>
        checked(baseAmount + effect.ScalingAmount * (effect.ScalingCap == 0 ? Math.Max(0, units) : Math.Clamp(units, 0, effect.ScalingCap)));
    // Combat-long effects are valued over six turns. Conditions do not discount worst-case payoff.
    public static int BudgetActivations(CardEffect effect) => effect.Trigger == EffectTrigger.OnPlay ? 1
        : (effect.Duration == 0 ? 6 : effect.Duration) * (IsEvent(effect.Trigger) ? effect.MaxPerTurn : 1);
}

public sealed record EffectTriggerState
{
    public required int[] Remaining { get; init; }
    public required int[] Activations { get; init; }
}

// Pure lifecycle logic shared by the game adapter and core tests. Consume BEFORE executing an async payoff.
public static class EffectTriggerRuntime
{
    public static EffectTriggerState Create(CardDefinition definition) => new()
    {
        Remaining = definition.Effects.Select(e => e.Trigger == EffectTrigger.OnPlay ? 0 : e.Duration == 0 ? -1 : e.Duration).ToArray(),
        Activations = new int[definition.Effects.Length]
    };

    public static void Validate(CardDefinition definition, EffectTriggerState state)
    {
        if (state.Remaining is null || state.Activations is null || state.Remaining.Length != definition.Effects.Length
            || state.Activations.Length != definition.Effects.Length) throw new FormatException("Invalid trigger state length.");
        for (int i = 0; i < state.Remaining.Length; i++)
        {
            var effect = definition.Effects[i];
            int maximum = effect.Trigger == EffectTrigger.OnPlay ? 0 : effect.Duration;
            if (effect.Duration == 0 ? state.Remaining[i] != -1 : state.Remaining[i] < 0 || state.Remaining[i] > maximum)
                throw new FormatException("Invalid remaining trigger lifetime.");
            if (state.Activations[i] < 0 || effect.MaxPerTurn > 0 && state.Activations[i] > effect.MaxPerTurn)
                throw new FormatException("Invalid trigger activation count.");
        }
    }

    public static void BeginTurn(EffectTriggerState state) => Array.Clear(state.Activations);
    public static bool TryConsume(CardDefinition definition, EffectTriggerState state, int index, EffectTrigger trigger)
    {
        var effect = definition.Effects[index];
        if (effect.Trigger == EffectTrigger.OnPlay || effect.Trigger != trigger || state.Remaining[index] == 0
            || effect.MaxPerTurn > 0 && state.Activations[index] >= effect.MaxPerTurn) return false;
        if (state.Activations[index] < int.MaxValue) state.Activations[index]++;
        if (effect.Trigger is EffectTrigger.NextTurnStart or EffectTrigger.TurnStart && state.Remaining[index] > 0)
            state.Remaining[index]--;
        return true;
    }

    public static void EndTurn(CardDefinition definition, EffectTriggerState state)
    {
        for (int i = 0; i < definition.Effects.Length; i++)
            if (definition.Effects[i].Trigger is not (EffectTrigger.NextTurnStart or EffectTrigger.TurnStart)
                && state.Remaining[i] > 0) state.Remaining[i]--;
    }
    public static bool IsExpired(EffectTriggerState state) => state.Remaining.All(n => n == 0);
}
