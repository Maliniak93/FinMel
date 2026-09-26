namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// A <see cref="TimeProvider"/> that answers the real clock until a test pins it with
/// <see cref="SetUtcNow"/>. Registered by <see cref="PortfolioApiFactory"/> in place of
/// <see cref="TimeProvider.System"/>, so a fact that depends on "today" (a term deposit's
/// Active/Due status, evaluated as the Europe/Warsaw date) pins it while every other fact keeps
/// running on real time.
/// </summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset? _utcNow;

    /// <summary>Pins "now" to <paramref name="utcNow"/> for every later read.</summary>
    public AdjustableTimeProvider SetUtcNow(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
        return this;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow ?? System.GetUtcNow();
}
