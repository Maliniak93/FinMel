using Domain;
using Domain.Entities.Bank;
using Domain.Specifications.StatementSpecification;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class BankStatementRepository : GenericRepository<BankStatement>, IBankStatementRepository
{
    private readonly ApplicationDbContext _context;

    public BankStatementRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }
    public async Task<BankStatement> GetByIdAsync(int id, string userId, bool asNoTracking = false)
    {
        var query = _context
            .Set<BankStatement>()
            .Include(t => t.StatementTransactions)
            .Where(u => u.CreatedBy == userId)
            .Where(i => i.Id == id);

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync();
    }

    public async Task<IReadOnlyCollection<BankStatement>> GetAllStatementsWithSpec(string userId, BankStatementsSpecification spec) =>
        await _context
            .GetEntityWithSpec<BankStatement>(spec)
            .AsNoTracking()
            .Include(b => b.BankAccount)
            .Where(u => u.CreatedBy == userId)
            .ToListAsync();
}
