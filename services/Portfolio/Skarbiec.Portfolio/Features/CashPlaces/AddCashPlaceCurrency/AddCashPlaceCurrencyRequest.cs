using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.CashPlaces.AddCashPlaceCurrency;

public sealed record AddCashPlaceCurrencyRequest
{
    [Required, SupportedCurrency]
    public required string Currency { get; init; }
}
