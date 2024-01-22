using Domain.Entities.Bank;

namespace Domain.Repositories;
public interface IBankAccountRepository : IGenericRepository<BankAccount>
{
    Task<bool> IsAcountNumberUniqueAsync(string accountNumber);
    Task<bool> IsCurrencyExistAsync(int currencyId);
    Task<bool> IsFirstBankAccount(string id);
    Task<BankAccount> GetByIdAsync(int id, string userId, bool asNoTracking);
    Task<List<BankAccount>> GetAllAsync(string userId);
    Task<IEnumerable<BankAccount>> GetAllWithStatementsAndTransactionsAsync(string userId);
    Task<BankAccount> GetByAccountNumberAsync(string accountNumber, string userId, bool asNoTracking);
}
