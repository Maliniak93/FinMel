using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

/// <summary>
/// Adds a user-supplied instrument to the shared dictionary, verified inline against its provider
/// before the row is created (ADR-018, M1.6). <c>Source</c> is no longer part of the request — it's
/// derived from <see cref="AssetClass"/> (<see cref="Data.AssetClassPriceSourceMapping"/>), so a
/// caller can't accidentally check a crypto id against Stooq or vice versa.
/// </summary>
public sealed record AddCustomInstrumentRequest
{
    [Required, MaxLength(30)]
    public required string Ticker { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string QuoteCurrency { get; init; }

    public required AssetClass AssetClass { get; init; }

    /// <summary>Opt-in per ADR-018: when the provider can't be reached during verification, the
    /// default is to fail closed (503, no row created). Setting this to <c>true</c> creates the
    /// instrument <see cref="Data.InstrumentVerificationStatus.Unverified"/> instead — exactly T2.8's
    /// original always-Unverified flow — and the one-off backfill job resolves it to Verified/Failed
    /// later, off the request path.</summary>
    public bool AllowUnverified { get; init; }
}
