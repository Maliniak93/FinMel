using Domain.Entities.Common;

namespace Domain.Repositories;
public interface IInvestmentRepository : IGenericRepository<Investment>
{
    Task<IReadOnlyCollection<Investment>> GetAllInTimeRange(string userId, DateTime from, DateTime to);
}
