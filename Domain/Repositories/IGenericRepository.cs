namespace Domain.Repositories;
public interface IGenericRepository<TEntity>
{
    Task<TEntity> GetByIdAsync(int id);
    Task<IEnumerable<TEntity>> GetAllAsync();
    void Insert(TEntity entity);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}
