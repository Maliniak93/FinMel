using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal sealed class SaveChangesCounter : SaveChangesInterceptor
{
    private int _count;
    private int _failNext;

    public int Count => Volatile.Read(ref _count);

    public void FailNext() => Volatile.Write(ref _failNext, 1);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _count);
        ThrowIfFailureArmed();
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        ThrowIfFailureArmed();
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ThrowIfFailureArmed()
    {
        if (Interlocked.Exchange(ref _failNext, 0) == 1)
        {
            throw new InvalidOperationException("Simulated save failure.");
        }
    }
}
