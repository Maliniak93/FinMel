using Domain.Entities.File;

namespace Domain.Repositories;

public interface IStatementFileRepository : IGenericRepository<StatementFile>
{
    Task<bool> IsStatementFileUniqueAsync(string fileName, string userId);
}