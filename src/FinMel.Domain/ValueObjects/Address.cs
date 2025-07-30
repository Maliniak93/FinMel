namespace FinMel.Domain.ValueObjects
{
    public sealed class Address : IEquatable<Address>
    {
        public required string Street { get; init; }
        public required string City { get; init; }
        public required string State { get; init; }
        public required string PostalCode { get; init; }
        public required string Country { get; init; }

        public override bool Equals(object? obj) => obj is Address other && Equals(other);

        public bool Equals(Address? other)
        {
            return other is not null &&
                   Street == other.Street &&
                   City == other.City &&
                   State == other.State &&
                   PostalCode == other.PostalCode &&
                   Country == other.Country;
        }

        public override int GetHashCode() => HashCode.Combine(Street, City, State, PostalCode, Country);

        public static bool operator ==(Address? left, Address? right) => Equals(left, right);
        public static bool operator !=(Address? left, Address? right) => !Equals(left, right);
    }
}
