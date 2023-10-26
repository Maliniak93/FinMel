using Domain.Entities.Bank;

namespace Domain.Repositories;
public interface ITransactionCodeRepository : IGenericRepository<TransactionCode>
{
    Task<TransactionCode> GetByIdAsync(int id, string userId, bool asNoTracking);
    Task<IEnumerable<TransactionCode>> GetAllAsync(string userId);
}
