using FinMel.Domain.Abstractions;
using FinMel.Domain.Entities.Currency;

namespace FinMel.Domain.Entities.ExchangeRate;

public sealed class ExchangeRate : Entity<ExchangeRateId>
{
    public required CurrencyId CurrencyId { get; init; }
    public required DateTime Date { get; init; }
    public required decimal RateToBase { get; init; }
}
