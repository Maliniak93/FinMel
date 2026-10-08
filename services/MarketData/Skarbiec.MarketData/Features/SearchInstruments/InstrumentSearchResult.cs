using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.SearchInstruments;

/// <summary>Id and VerificationStatus are null for a provider listing not yet in MarketData; adding it creates the instrument.</summary>
public sealed record InstrumentSearchResult(
    Guid? Id,
    string Ticker,
    string Name,
    AssetClass AssetClass,
    string QuoteCurrency,
    string? Exchange,
    InstrumentVerificationStatus? VerificationStatus,
    decimal? LastPrice,
    DateOnly? LastPriceDate);
