using System;

namespace FinMel.Domain.Entities.ExchangeRate;

public sealed class ExchangeRateId : IEquatable<ExchangeRateId>
{
    public Guid Value { get; }

    public ExchangeRateId(Guid value)
    {
        Value = value;
    }

    public static ExchangeRateId New() => new(Guid.NewGuid());

    public bool Equals(ExchangeRateId? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is ExchangeRateId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();

    public static bool operator ==(ExchangeRateId left, ExchangeRateId right) => left.Equals(right);
    public static bool operator !=(ExchangeRateId left, ExchangeRateId right) => !(left == right);
}
