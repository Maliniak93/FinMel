using Domain;
using Domain.Entities.Bank;
using Domain.Specifications.TransactionSpecification;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class StatementTransactionRepository : GenericRepository<StatementTransaction>, IStatementTransactionRepository
{
    private readonly ApplicationDbContext _context;

    public StatementTransactionRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<StatementTransaction>> GetStatementByIdTransactionsWithSpec(int id, string userId, BankStatementsTransactionsSpecification spec)
    {
        var query = _context
            .GetEntityWithSpec<StatementTransaction>(spec)
            .AsNoTracking()
            .Include(x => x.TransactionCode)
            .Where(u => u.CreatedBy == userId)
            .Where(i => i.BankStatementId == id);

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyCollection<StatementTransaction>> GetStatementsTransactionsInTimeRange(string userId, DateTime from, DateTime to, int bankAccountId)
    {
        var query = _context
            .Set<StatementTransaction>()
            .AsNoTracking()
            .Include(s => s.BankStatement)
            .Include(c => c.TransactionCode)
            .Where(u => u.CreatedBy == userId)
            .Where(f => f.DashboardDate >= from && f.DashboardDate <= to)
            .Where(a => a.BankStatement.BankAccountId == bankAccountId)
            .OrderByDescending(d => d.TransactionDate)
            .ThenByDescending(i => i.Id);

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyCollection<StatementTransaction>> GetAllStatementTransactionsWithSpec(string userId, BankStatementsTransactionsSpecification spec)
    {
        var query = _context
            .GetEntityWithSpec<StatementTransaction>(spec)
            .AsNoTracking()
            .Where(u => u.CreatedBy == userId);

        return await query.ToListAsync();
    }

}
