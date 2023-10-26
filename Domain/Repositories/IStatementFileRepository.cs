using Domain.Entities.Files;
using Domain.Repositories;

namespace Domain;

public interface IStatementFileRepository : IGenericRepository<StatementFile>
{
    Task<bool> IsStatementFileUniqueAsync(string fileName, string userId);
}