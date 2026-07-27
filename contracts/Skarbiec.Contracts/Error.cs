namespace Skarbiec.Contracts;

/// <summary>
/// A machine-readable failure carried by a <see cref="Result"/>/<see cref="Result{TValue}"/> (ADR-017).
/// <paramref name="Code"/> follows the "&lt;Category&gt;.&lt;Detail&gt;" convention (e.g. "NotFound.User",
/// "Conflict.DuplicateEmail") — <c>Skarbiec.ServiceDefaults</c>'s ProblemDetails mapping helper (T0.3)
/// reads the category to pick the HTTP status.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
