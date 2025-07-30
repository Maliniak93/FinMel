namespace FinMel.Domain.ValueObjects
{
    /// <summary>
    /// Represents a physical address as a value object.
    /// Immutable and validated on creation.
    /// </summary>
    public sealed record Address(
        string Street,
        string City,
        string State,
        string PostalCode,
        string Country
    );
}
