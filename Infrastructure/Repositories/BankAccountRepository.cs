using Domain.Entities.Bank;
using Domain.Entities.Common;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class BankAccountRepository : GenericRepository<BankAccount>, IBankAccountRepository
{
    private readonly ApplicationDbContext _context;

    public BankAccountRepository(ApplicationDbContext context)
        : base(context)
    {
        _context = context;
    }

    public async Task<bool> IsAcountNumberUniqueAsync(string accountNumber) =>
        !await _context
        .Set<BankAccount>()
        .AnyAsync(b => b.AccountNumber == accountNumber);

    public async Task<bool> IsCurrencyExistAsync(int currencyId) =>
        await _context
        .Set<Currency>()
        .AnyAsync(c => c.Id == currencyId);

    public async Task<bool> IsFirstBankAccount(string id) =>
        !await _context.Set<BankAccount>().AnyAsync(u => u.CreatedBy == id);


    public async Task<BankAccount> GetByIdAsync(int id, string userId, bool asNoTracking = false)
    {
        var query = _context
        .Set<BankAccount>()
        .Include(x => x.Currency)
        .Include(x => x.BankStatements)
        .Include(x => x.History)
        .Where(u => u.CreatedBy == userId)
        .Where(i => i.Id == id);

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync();
    }

    public async Task<List<BankAccount>> GetAllAsync(string userId) =>
        await _context.Set<BankAccount>()
           .AsNoTracking()
           .Include(x => x.Currency)
           .Include(x => x.History)
           .Where(u => u.CreatedBy == userId)
           .ToListAsync();

    public async Task<IEnumerable<BankAccount>> GetAllWithStatementsAndTransactionsAsync(string userId) =>
        await _context.Set<BankAccount>()
           .AsNoTracking()
           .Include(x => x.Currency)
           .Include(x => x.BankStatements)
           .ThenInclude(x => x.StatementTransactions)
           .ThenInclude(x => x.TransactionCode)
           .Include(x => x.History)
           .Where(u => u.CreatedBy == userId)
           .ToListAsync();

    public async Task<BankAccount> GetByAccountNumberAsync(string accountNumber, string userId, bool asNoTracking)
    {
        var query = _context
            .Set<BankAccount>()
            .Where(u => u.CreatedBy == userId)
            .Where(i => i.AccountNumber == accountNumber);

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync();
    }
}
