using Domain;
using Domain.Entities.Files;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;
public class StatementFileRepository : GenericRepository<StatementFile>, IStatementFileRepository
{
    private readonly ApplicationDbContext _context;
    public StatementFileRepository(ApplicationDbContext context)
        : base(context)
    {
        _context = context;
    }

    public async Task<bool> IsStatementFileUniqueAsync(string fileName, string userId) =>
        !await _context
        .Set<StatementFile>()
        .Where(u => u.CreatedBy == userId)
        .AnyAsync(b => b.FileName == fileName);
}
