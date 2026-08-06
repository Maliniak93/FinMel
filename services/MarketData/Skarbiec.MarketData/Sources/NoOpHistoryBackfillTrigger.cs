namespace Skarbiec.MarketData.Sources;

/// <summary>
/// <see cref="IHistoryBackfillTrigger"/> registered instead of <see cref="QuartzHistoryBackfillTrigger"/>
/// when <c>Testing:DisableBackgroundJobs</c> is set (see <see cref="HistoryBackfillJobExtensions"/>).
/// Request-path callers (Features/AddCustomInstrument, T2.8) need the interface resolvable under
/// <c>SkarbiecApiFactory</c>-based HTTP slice tests even though there's no live Quartz scheduler
/// there to enqueue onto — this just discards the request.
/// </summary>
public sealed class NoOpHistoryBackfillTrigger : IHistoryBackfillTrigger
{
    public Task EnqueueAsync(Guid instrumentId, CancellationToken cancellationToken) => Task.CompletedTask;
}
