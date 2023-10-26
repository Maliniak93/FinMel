using Domain.Repositories;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction _transaction;

    public UnitOfWork(ApplicationDbContext context) =>
        _context = context;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public virtual void BeginTransaction()
    {
        if (_transaction != null)
        {
            _transaction.Rollback();
            _transaction.Dispose();
            _transaction = null;
        }

        _transaction = _context.Database.BeginTransaction();
    }

    public virtual void RollbackTransaction()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException();
        }

        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = null;

        _context.Clear();
    }

    public virtual void CommitTransaction()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException();
        }

        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
    }
}
