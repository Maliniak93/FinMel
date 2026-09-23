namespace Skarbiec.MarketData.Data;

/// <summary>
/// The currency catalog <see cref="Sources.FxSyncJob"/> syncs against (spec-04, ADR-023). Global
/// reference data, not user-owned. PLN is the fixed base currency (ADR-008), so there is no pivot or
/// fallback-rate column: every non-PLN row gets a daily <c>&lt;CODE&gt;PLN</c> <see cref="FxRate"/>.
/// Deliberately wider than the user-facing <c>SupportedCurrencies</c> set, and backend-only.
/// </summary>
public sealed class Currency
{
    /// <summary>ISO 4217 code, uppercase — the primary key.</summary>
    public required string Code { get; init; }

    public required string Name { get; set; }
    public required string Symbol { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public int DisplayOrder { get; set; }
}
