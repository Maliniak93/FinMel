using Domain.Entities.Dashboard;

namespace Domain.Repositories;
public interface IDashboardRepository : IGenericRepository<MainDashboard>
{
    Task<List<MainDashboard>> GetAllAsync(string userId);
    Task<MainDashboard> GetUserDashboard(string userId);
}
