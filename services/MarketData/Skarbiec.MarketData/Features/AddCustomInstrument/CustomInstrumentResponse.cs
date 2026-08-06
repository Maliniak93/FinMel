using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

public sealed record CustomInstrumentResponse
{
    public required Guid Id { get; init; }
    public required string Ticker { get; init; }
    public required string Name { get; init; }
    public required PriceSource Source { get; init; }
    public required string QuoteCurrency { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required InstrumentVerificationStatus VerificationStatus { get; init; }
}

public static class CustomInstrumentMappingExtensions
{
    public static CustomInstrumentResponse ToResponse(this Instrument instrument) => new()
    {
        Id = instrument.Id,
        Ticker = instrument.Ticker,
        Name = instrument.Name,
        Source = instrument.Source,
        QuoteCurrency = instrument.QuoteCurrency,
        AssetClass = instrument.AssetClass,
        VerificationStatus = instrument.VerificationStatus,
    };
}
