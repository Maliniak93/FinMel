namespace FinMel.Application.Abstractions.Services;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
