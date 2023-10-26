using Domain.Common;
using Domain.Repositories;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public abstract class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : BaseEntity
{
    protected GenericRepository(ApplicationDbContext context) => Context = context;

    protected ApplicationDbContext Context { get; }

    public async Task<TEntity> GetByIdAsync(int id) => await Context.GetByIdAsync<TEntity>(id);

    public async Task<IEnumerable<TEntity>> GetAllAsync() => await Context.Set<TEntity>().ToListAsync();

    public void Insert(TEntity entity) => Context.Insert(entity);

    public void Update(TEntity entity) => Context.Update(entity);

    public void Remove(TEntity entity) => Context.Remove(entity);
}
