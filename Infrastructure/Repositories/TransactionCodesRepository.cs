using Domain.Entities.Bank;
using Domain.Repositories;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class TransactionCodesRepository : GenericRepository<TransactionCode>, ITransactionCodeRepository
{
    private readonly ApplicationDbContext _context;

    public TransactionCodesRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<TransactionCode> GetByIdAsync(int id, string userId, bool asNoTracking = false)
    {
        var query = _context
            .Set<TransactionCode>()
            .Where(i => i.Id == id);

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync();
    }

    public async Task<List<TransactionCode>> GetAllAsync(string userId) =>
        await _context
        .Set<TransactionCode>()
        .ToListAsync();
}
