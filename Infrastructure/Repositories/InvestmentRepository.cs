using Domain.Entities.Common;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class InvestmentRepository : GenericRepository<Investment>, IInvestmentRepository
{
    private readonly ApplicationDbContext _context;

    public InvestmentRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<Investment>> GetAllInTimeRange(string userId, DateTime from, DateTime to) =>
        await _context
            .Set<Investment>()
            .AsNoTracking()
            .Where(u => u.CreatedBy == userId)
            .Where(x => x.IsFinished == false)
            .Where(f => f.StartInvestment <= to)
            .ToListAsync();

}
