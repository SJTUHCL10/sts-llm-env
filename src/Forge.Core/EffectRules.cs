namespace Forge.Core;

public static class EffectRules
{
    public static int Evaluate(NumberExpression? value, Func<NumberExpression, int> read, int fallback = 0)
    {
        if (value is null) return fallback;
        if (value.Value is int literal) return literal;
        if (value.Stat is not null) return read(value);
        if (value.Add is { } add) return add.Aggregate(0, (total, node) => checked(total + Evaluate(node, read)));
        if (value.Sub is { } sub) return checked(Evaluate(sub[0], read) - Evaluate(sub[1], read));
        if (value.Mul is { } mul) return mul.Aggregate(1, (total, node) => checked(total * Evaluate(node, read)));
        var div = value.Div ?? throw new FormatException("Invalid expression.");
        int denominator = Evaluate(div[1], read);
        if (denominator == 0) throw new InvalidOperationException("Dynamic division by zero.");
        return checked((int)decimal.Floor((decimal)Evaluate(div[0], read) / denominator));
    }
    public static bool Matches(EffectCondition? condition, Func<NumberExpression, int> read)
    {
        if (condition is null) return true;
        if (condition.All is { } all) return all.All(c => Matches(c, read));
        if (condition.Any is { } any) return any.Any(c => Matches(c, read));
        if (condition.Not is { } not) return !Matches(not, read);
        int left = Evaluate(condition.Left, read), right = Evaluate(condition.Right, read);
        return condition.Op switch
        {
            Comparison.Eq => left == right, Comparison.Ne => left != right,
            Comparison.Gt => left > right, Comparison.Ge => left >= right,
            Comparison.Lt => left < right, Comparison.Le => left <= right,
            _ => throw new FormatException("Invalid comparison.")
        };
    }
    public static bool MatchesOccurrence(EventOccurrence? occurrence, int ordinal) => occurrence is null
        || ordinal > 0 && (occurrence.First is int first ? ordinal <= first
            : occurrence.Nth is int nth ? ordinal == nth : ordinal % occurrence.Every!.Value == 0);
}

public sealed record EffectTriggerState
{
    public required int[] Remaining { get; init; }
    public required int[] Activations { get; init; }
}

public static class EffectTriggerRuntime
{
    public static EffectTriggerState Create(CardForm form) => new()
    {
        Remaining = form.Listeners.Select(r => r.Lifetime == LifetimeKind.Combat ? -1 : r.Lifetime == LifetimeKind.NextTurn ? 1 : r.Turns ?? 1).ToArray(),
        Activations = new int[form.Listeners.Length]
    };
    public static void Validate(CardForm form, EffectTriggerState state)
    {
        if (state.Remaining is null || state.Activations is null || state.Remaining.Length != form.Listeners.Length || state.Activations.Length != form.Listeners.Length)
            throw new FormatException("Invalid rule state arrays.");
        var initial = Create(form);
        for (int i = 0; i < initial.Remaining.Length; i++)
            if ((initial.Remaining[i] == -1 ? state.Remaining[i] != -1 : state.Remaining[i] < 0 || state.Remaining[i] > initial.Remaining[i])
                || state.Activations[i] < 0 || form.Listeners[i].Trigger.Limit is { } quota && state.Activations[i] > quota.Count)
                throw new FormatException("Invalid rule counters/lifetime.");
    }
    public static void BeginTurn(CardForm form, EffectTriggerState state)
    {
        for (int i = 0; i < form.Listeners.Length; i++)
            if (form.Listeners[i].Trigger.Limit?.Within != CounterScope.Combat) state.Activations[i] = 0;
    }
    // Quotas count satisfied rules, once for the complete group, before awaiting any payoff.
    public static bool TryConsume(CardForm form, EffectTriggerState state, int index, RuleEvent fired, int ordinal = 1, bool conditionMatches = true)
    {
        var rule = form.Listeners[index];
        if (rule.Trigger.Event != fired || state.Remaining[index] == 0) return false;
        if (fired == RuleEvent.TurnStart && state.Remaining[index] > 0) state.Remaining[index]--;
        if (!conditionMatches || !EffectRules.MatchesOccurrence(rule.Trigger.Occurrence, ordinal)
            || rule.Trigger.Limit is { } limit && state.Activations[index] >= limit.Count) return false;
        if (state.Activations[index] < int.MaxValue) state.Activations[index]++;
        return true;
    }
    public static void EndTurn(CardForm form, EffectTriggerState state)
    {
        for (int i = 0; i < form.Listeners.Length; i++)
            if (form.Listeners[i].Lifetime == LifetimeKind.Turn && form.Listeners[i].Trigger.Event != RuleEvent.TurnStart && state.Remaining[i] > 0) state.Remaining[i]--;
    }
    public static bool IsExpired(EffectTriggerState state) => state.Remaining.All(n => n == 0);
}
