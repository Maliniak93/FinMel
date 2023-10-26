using Domain.Entities.Bank;
using Domain.Repositories;
using Domain.Specifications.StatementSpecification;

namespace Domain;

public interface IBankStatementRepository : IGenericRepository<BankStatement>
{
    Task<BankStatement> GetByIdAsync(int id, string userId, bool asNoTracking);
    Task<IReadOnlyCollection<BankStatement>> GetAllStatementsWithSpec(string userId, BankStatementsSpecification spec);
}