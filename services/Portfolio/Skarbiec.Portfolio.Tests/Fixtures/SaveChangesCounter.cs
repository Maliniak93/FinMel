using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Skarbiec.Portfolio.Tests.Fixtures;

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
