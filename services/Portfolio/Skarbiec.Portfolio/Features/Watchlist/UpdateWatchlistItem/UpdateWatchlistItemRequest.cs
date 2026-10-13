using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;

/// <summary>Replaces both fields; null clears one.</summary>
public sealed record UpdateWatchlistItemRequest
{
    [MaxLength(500)]
    public string? Note { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public decimal? TargetPrice { get; init; }
}
