using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

/// <summary>The price source is derived from AssetClass, never asked for.</summary>
public sealed record AddCustomInstrumentRequest
{
    [Required, MaxLength(30)]
    public required string Ticker { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string QuoteCurrency { get; init; }

    public required AssetClass AssetClass { get; init; }

    /// <summary>When the provider is unreachable, create the instrument Unverified instead of failing with 503.</summary>
    public bool AllowUnverified { get; init; }
}
