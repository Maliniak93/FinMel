namespace FinMel.Domain.Abstractions;

/// <summary>
/// Represents an error that can occur during operations.
/// </summary>
public abstract class Error : IEquatable<Error>
{
    public static readonly Error None = new NoneError();
    public static readonly Error NullValue = new NullValueError();

    protected Error(string code, string name, string description)
    {
        Code = code;
        Name = name;
        Description = description;
    }

    public string Code { get; }
    public string Name { get; }
    public string Description { get; }

    public static implicit operator Result(Error error) => Result.Failure(error);

    public static bool operator ==(Error? a, Error? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Equals(b);
    }

    public static bool operator !=(Error? a, Error? b) => !(a == b);

    public virtual bool Equals(Error? other)
    {
        if (other is null)
        {
            return false;
        }

        return Code == other.Code && Name == other.Name && Description == other.Description;
    }

    public override bool Equals(object? obj) => obj is Error error && Equals(error);

    public override int GetHashCode() => HashCode.Combine(Code, Name, Description);

    public override string ToString() => $"{Name}: {Description}";

    private sealed class NoneError : Error
    {
        public NoneError() : base(string.Empty, string.Empty, string.Empty) { }
    }

    private sealed class NullValueError : Error
    {
        public NullValueError() : base("Error.NullValue", "Null value", "Null value was provided") { }
    }
}
