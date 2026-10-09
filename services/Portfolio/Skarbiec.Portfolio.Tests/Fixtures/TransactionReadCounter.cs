using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal sealed class TransactionReadCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        CountIfTransactionsRead(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        CountIfTransactionsRead(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void CountIfTransactionsRead(DbCommand command)
    {
        var text = command.CommandText.TrimStart();
        if (text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && text.Contains("\"Transactions\"", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _count);
        }
    }
}
