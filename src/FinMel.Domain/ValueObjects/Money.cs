using FinMel.Domain.Abstractions;

namespace FinMel.Domain.ValueObjects
{
    /// <summary>
    /// Represents a monetary value with currency support.
    /// </summary>
    public sealed record Money(decimal Value, CurrencyId CurrencyId) : ValueObject
    {
        public static Money operator +(Money a, Money b)
        {
            if (a.CurrencyId != b.CurrencyId)
                throw new InvalidOperationException("Cannot add Money with different currencies.");
            return new Money(a.Value + b.Value, a.CurrencyId);
        }

        public static Money operator -(Money a, Money b)
        {
            if (a.CurrencyId != b.CurrencyId)
                throw new InvalidOperationException("Cannot subtract Money with different currencies.");
            return new Money(a.Value - b.Value, a.CurrencyId);
        }

        public override string ToString() => $"{Value} {CurrencyId}";
    }
}
