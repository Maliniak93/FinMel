using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetInstrument;

public sealed record InstrumentDetailsResponse(
    Guid Id,
    string Ticker,
    string Name,
    AssetClass AssetClass,
    string QuoteCurrency,
    PriceSource Source,
    InstrumentVerificationStatus VerificationStatus,
    decimal? LastPrice,
    DateOnly? LastPriceDate);

public static class InstrumentDetailsMappingExtensions
{
    public static InstrumentDetailsResponse ToDetailsResponse(this Instrument instrument, PriceQuote? latestQuote) => new(
        instrument.Id,
        instrument.Ticker,
        instrument.Name,
        instrument.AssetClass,
        instrument.QuoteCurrency,
        instrument.Source,
        instrument.VerificationStatus,
        latestQuote?.Close,
        latestQuote?.Date);
}
