namespace Skarbiec.MarketData.Sources;

public sealed class InstrumentSearchOptions
{
    public const string SectionName = "InstrumentSearch";

    // The binder appends configured entries to these defaults, so adding an exchange is one config entry.
    public List<InstrumentExchange> Exchanges { get; set; } =
    [
        new() { ProviderCode = "WSE", Suffix = ".WA", Name = "GPW", Currency = "PLN" },
        new() { ProviderCode = "GER", Suffix = ".DE", Name = "Xetra", Currency = "EUR" },
    ];

    public InstrumentExchange? FindBySuffix(string ticker) =>
        Exchanges.FirstOrDefault(e => ticker.EndsWith(e.Suffix, StringComparison.OrdinalIgnoreCase) && ticker.Length > e.Suffix.Length);

    public InstrumentExchange? FindByProviderCode(string? providerCode) =>
        Exchanges.FirstOrDefault(e => string.Equals(e.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));

    public bool IsValid() =>
        Exchanges.Count > 0
        && Exchanges.All(e =>
            !string.IsNullOrWhiteSpace(e.ProviderCode)
            && e.Suffix.Length > 1
            && e.Suffix[0] == '.'
            && !string.IsNullOrWhiteSpace(e.Name)
            && e.Name.Length <= 20
            && e.Currency.Length == 3)
        && Exchanges.Select(e => e.Suffix.ToUpperInvariant()).Distinct().Count() == Exchanges.Count;
}

/// <summary>A listing venue: ProviderCode is Yahoo's exchange code, Suffix the ticker suffix it uses (".WA").</summary>
public sealed class InstrumentExchange
{
    public string ProviderCode { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
}
