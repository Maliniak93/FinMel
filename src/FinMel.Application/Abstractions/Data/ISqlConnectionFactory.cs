using System.Data.Common;

namespace FinMel.Application.Abstractions.Data;

public interface ISqlConnectionFactory
{
    ValueTask<DbConnection> CreateConnectionAsync();
}
