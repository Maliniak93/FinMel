using Domain.Entities.Bank;
using Domain.Specifications.TransactionSpecification;

namespace Domain.Repositories;

public interface IStatementTransactionRepository : IGenericRepository<StatementTransaction>
{
    Task<StatementTransaction> GetTransactionById(int id, string userId);
    Task<IReadOnlyCollection<StatementTransaction>> GetStatementByIdTransactionsWithSpec(int id, string userId, BankStatementsTransactionsSpecification spec);
    Task<IReadOnlyCollection<StatementTransaction>> GetStatementsTransactionsInTimeRange(string userId, DateTime from, DateTime to, int bankAccountId);
    Task<IReadOnlyCollection<StatementTransaction>> GetAllStatementTransactionsWithSpec(string userId, BankStatementsTransactionsSpecification spec);
}
