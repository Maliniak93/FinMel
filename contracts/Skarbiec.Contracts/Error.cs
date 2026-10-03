namespace Skarbiec.Contracts;

/// <summary>Code is Category.Detail, e.g. NotFound.User; the category picks the HTTP status.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
