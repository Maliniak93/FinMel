using Domain.Entities.Dashboard;

namespace Domain.Repositories;
public interface IDashboardRepository : IGenericRepository<MainDashboard>
{
    Task<MainDashboard> GetUserDashboard(string userId);
}
