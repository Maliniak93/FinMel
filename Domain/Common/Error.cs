namespace Domain.Common;
public class Error : ValueObject
{
    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }

    public static Error None => new Error(string.Empty, string.Empty);

    public static implicit operator string(Error error) => error?.Code ?? string.Empty;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
    }
}
