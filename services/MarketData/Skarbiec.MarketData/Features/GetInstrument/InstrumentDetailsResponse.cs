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
    InstrumentVerificationStatus VerificationStatus);

public static class InstrumentDetailsMappingExtensions
{
    public static InstrumentDetailsResponse ToDetailsResponse(this Instrument instrument) => new(
        instrument.Id,
        instrument.Ticker,
        instrument.Name,
        instrument.AssetClass,
        instrument.QuoteCurrency,
        instrument.Source,
        instrument.VerificationStatus);
}
