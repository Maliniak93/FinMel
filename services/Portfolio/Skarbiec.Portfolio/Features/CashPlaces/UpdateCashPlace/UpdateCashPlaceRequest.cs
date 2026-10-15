using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;

public sealed record UpdateCashPlaceRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }
}
