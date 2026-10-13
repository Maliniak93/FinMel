using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class WatchlistItem : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid InstrumentId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required string Ticker { get; init; }
    public required string Name { get; init; }
    public string? Exchange { get; init; }
    public required string QuoteCurrency { get; init; }
    public string? Note { get; set; }
    public decimal? TargetPrice { get; set; }
    public required DateTimeOffset AddedAtUtc { get; init; }
}
