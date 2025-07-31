using System.Text.RegularExpressions;
using FinMel.Domain.Abstractions;

namespace FinMel.Domain.Entities.Currency;

/// <summary>
/// Represents a currency entity with a strongly-typed identifier.
/// </summary>
public sealed partial class Currency : Entity<CurrencyId>
{
    private static readonly Regex IsoCodeRegex = CurrencyRegex();

    private string _code = string.Empty;
    public required string Code
    {
        get => _code;
        init
        {
            if (!IsoCodeRegex.IsMatch(value))
                throw new ArgumentException("Currency code must be a 3-letter uppercase ISO code.", nameof(Code));
            _code = value;
        }
    }

    public required string Name { get; init; }
    public required string Symbol { get; init; }

    [GeneratedRegex(@"^[A-Z]{3}$", RegexOptions.Compiled)]
    private static partial Regex CurrencyRegex();
}
