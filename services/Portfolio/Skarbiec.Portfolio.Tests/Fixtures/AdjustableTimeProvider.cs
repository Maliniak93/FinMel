namespace Skarbiec.Portfolio.Tests.Fixtures;

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset? _utcNow;

    public AdjustableTimeProvider SetUtcNow(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
        return this;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow ?? System.GetUtcNow();
}
