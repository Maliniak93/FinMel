using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skarbiec.MarketData.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>: design-time tooling builds the model without
/// running the app (and without Aspire injecting the real connection string), so a placeholder
/// Npgsql connection is enough — nothing here ever actually connects.
/// </summary>
public sealed class MarketDataDbContextFactory : IDesignTimeDbContextFactory<MarketDataDbContext>
{
    public MarketDataDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MarketDataDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=marketdata_db;Username=marketdata_user;Password=design-time-only");

        return new MarketDataDbContext(optionsBuilder.Options);
    }
}
