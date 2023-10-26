using Domain.Entities.Bank;
using Domain.Specifications.TransactionSpecification;

namespace Domain;

public interface IStatementTransactionRepository
{
    Task<IReadOnlyCollection<StatementTransaction>> GetAllStatementTransactionsWithSpec(string userId, BankStatementsTransactionsSpecification spec);
}