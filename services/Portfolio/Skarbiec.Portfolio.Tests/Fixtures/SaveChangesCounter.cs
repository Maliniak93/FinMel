using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Counts <c>SaveChanges</c>/<c>SaveChangesAsync</c> calls on every DbContext it is registered with.
/// Lets an outbox fact prove "all removals and outbox rows commit in one save" (ADR-012) instead of
/// only proving that both eventually exist. Register it once per provider with
/// <c>services.ConfigureDbContext&lt;TContext&gt;(o =&gt; o.AddInterceptors(counter))</c>, then
/// <see cref="Reset"/> right before the act so arrange-step saves are not counted.
/// </summary>
internal sealed class SaveChangesCounter : SaveChangesInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _count);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
