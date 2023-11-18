using Domain.Entities.Dashboard;
using Domain.Repositories;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class DashboardRepository : GenericRepository<MainDashboard>, IDashboardRepository
{
    private readonly ApplicationDbContext _context;
    public DashboardRepository(ApplicationDbContext context)
        : base(context)
    {
        _context = context;
    }

    public async Task<List<MainDashboard>> GetAllAsync(string userId) =>
        await _context
        .Set<MainDashboard>()
        .AsNoTracking()
        .Where(u => u.CreatedBy == userId)
        .ToListAsync();


    public async Task<MainDashboard> GetUserDashboard(string userId) =>
        await _context
        .Set<MainDashboard>()
        .AsNoTracking()
        .Where(u => u.CreatedBy == userId)
        .FirstOrDefaultAsync();
}
