using Domain.Entities.Bank;
using Domain.Entities.Common;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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
        if (_context.Database.IsSqlite())
        {
            await _context.Database.MigrateAsync();
        }
    }

    public async Task SeedAsync()
    {
        await TrySeedAsync();
    }

    public async Task TrySeedAsync()
    {
        if (!_context.Set<Currency>().Any())
        {
            _context.Set<Currency>().AddRange(new Currency
            {
                CurrencyName = "Polski Złoty",
                CurrencyTag = "PLN"
            },
            new Currency
            {
                CurrencyName = "Euro",
                CurrencyTag = "EUR"
            });
            await _context.SaveChangesAsync();
        }
        if (!_context.Set<TransactionCode>().Any())
        {
            string filePath = Path.Combine("..", "Infrastructure", "Persistence", "TransactionCodes.json");
            if (File.Exists(filePath))
            {
                string jsonData = File.ReadAllText(filePath);
                List<TransactionCode> codes = JsonConvert.DeserializeObject<List<TransactionCode>>(jsonData);

                _context.Set<TransactionCode>().AddRange(codes);

                await _context.SaveChangesAsync();
            }
        }
    }
}

