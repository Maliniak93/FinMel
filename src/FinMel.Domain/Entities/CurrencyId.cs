using System;

namespace FinMel.Domain.Entities
{
    /// <summary>
    /// Strongly-typed identifier for Currency entity.
    /// </summary>
    public readonly struct CurrencyId : IEquatable<CurrencyId>
    {
        public int Value { get; }

        public CurrencyId(int value)
        {
            Value = value;
        }

        public static CurrencyId New(int value) => new CurrencyId(value);

        public bool Equals(CurrencyId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is CurrencyId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(CurrencyId left, CurrencyId right) => left.Equals(right);
        public static bool operator !=(CurrencyId left, CurrencyId right) => !(left == right);
    }
}
