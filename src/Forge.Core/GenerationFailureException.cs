namespace Forge.Core;

/// <summary>A diagnostic produced locally; never contains provider bodies or credentials.</summary>
public sealed class GenerationFailureException(string reason) : FormatException(reason)
{
    public string Reason { get; } = reason;
}
