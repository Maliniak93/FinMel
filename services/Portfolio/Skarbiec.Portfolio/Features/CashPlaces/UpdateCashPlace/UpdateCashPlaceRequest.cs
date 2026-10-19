using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;

public sealed record UpdateCashPlaceRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>The group every new balance of the place takes, replaced as sent; existing balances keep theirs.</summary>
    public Guid? GroupId { get; init; }
}
