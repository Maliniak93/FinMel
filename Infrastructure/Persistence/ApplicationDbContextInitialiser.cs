using Domain.Entities.Common;
using FinMel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;
public class ApplicationDbContextInitialiser
{
    private readonly ApplicationDbContext _context;

    public ApplicationDbContextInitialiser(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            if (_context.Database.IsSqlServer())
            {
                await _context.Database.MigrateAsync();
            }
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        if (!_context.Set<Currency>().Any())
        {
            _context.Set<Currency>().Add(new Currency
            {
                CurrencyName = "Polski Złoty",
                CurrencyTag = "PLN"
            });
            await _context.SaveChangesAsync();
        }
    }
}

