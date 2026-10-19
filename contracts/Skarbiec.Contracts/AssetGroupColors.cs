namespace Skarbiec.Contracts;

public static class AssetGroupColors
{
    public static readonly IReadOnlyList<string> Keys =
        ["blue", "green", "orange", "purple", "red", "teal", "pink", "amber", "indigo", "lime", "cyan", "brown"];

    public static bool Contains(string? color) =>
        color is not null && Keys.Contains(color, StringComparer.Ordinal);
}
