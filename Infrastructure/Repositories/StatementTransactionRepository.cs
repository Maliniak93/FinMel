using Domain;
using Domain.Entities.Bank;
using Domain.Specifications.TransactionSpecification;
using FinMel.Infrastructure.Persistence;
using System.Data.Entity;

namespace Infrastructure.Repositories;
public class StatementTransactionRepository : GenericRepository<StatementTransaction>, IStatementTransactionRepository
{
    private readonly ApplicationDbContext _context;

    public StatementTransactionRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<StatementTransaction>> GetAllStatementTransactionsWithSpec(string userId, BankStatementsTransactionsSpecification spec) =>
        await _context
            .GetEntityWithSpec<StatementTransaction>(spec)
            .AsNoTracking()
            .Where(u => u.CreatedBy == userId)
            .ToListAsync();
}
