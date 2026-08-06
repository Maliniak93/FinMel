using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

public sealed record AddCustomInstrumentRequest
{
    public required PriceSource Source { get; init; }

    [Required, MaxLength(30)]
    public required string Ticker { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string QuoteCurrency { get; init; }

    public required AssetClass AssetClass { get; init; }
}
