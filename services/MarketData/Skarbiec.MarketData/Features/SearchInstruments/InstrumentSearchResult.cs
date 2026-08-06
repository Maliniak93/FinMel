using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.SearchInstruments;

public sealed record InstrumentSearchResult(
    Guid Id,
    string Ticker,
    string Name,
    AssetClass AssetClass,
    string QuoteCurrency,
    InstrumentVerificationStatus VerificationStatus,
    decimal? LastPrice,
    DateOnly? LastPriceDate);
