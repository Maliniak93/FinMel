namespace Skarbiec.Contracts;

/// <summary>A machine-readable failure carried by a <see cref="Result"/>/<see cref="Result{TValue}"/> (ADR-017).</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
