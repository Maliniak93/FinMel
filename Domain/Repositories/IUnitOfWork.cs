namespace Domain.Repositories;
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    void BeginTransaction();
    void CommitTransaction();
    void RollbackTransaction();
}
