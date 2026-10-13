using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;

public sealed record AddWatchlistItemRequest
{
    public required Guid InstrumentId { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }

    /// <summary>In the instrument's quote currency.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public decimal? TargetPrice { get; init; }
}
