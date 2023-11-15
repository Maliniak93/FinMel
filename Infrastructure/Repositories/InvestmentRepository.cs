using Domain.Entities.Common;
using Domain.Repositories;
using FinMel.Infrastructure.Persistence;

namespace Infrastructure.Repositories;
public class InvestmentRepository : GenericRepository<Investment>, IInvestmentRepository
{
    public InvestmentRepository(ApplicationDbContext context) : base(context)
    {
    }
}
