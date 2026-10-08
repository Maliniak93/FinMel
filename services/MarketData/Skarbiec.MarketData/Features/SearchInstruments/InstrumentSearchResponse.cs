namespace Skarbiec.MarketData.Features.SearchInstruments;

/// <summary>ProviderUnavailable: the provider search failed, so Results holds only instruments already in MarketData.</summary>
public sealed record InstrumentSearchResponse(IReadOnlyList<InstrumentSearchResult> Results, bool ProviderUnavailable);
