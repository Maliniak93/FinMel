using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skarbiec.MarketData.Data;

public sealed class MarketDataDbContextFactory : IDesignTimeDbContextFactory<MarketDataDbContext>
{
    public MarketDataDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MarketDataDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=marketdata_db;Username=marketdata_user;Password=design-time-only");

        return new MarketDataDbContext(optionsBuilder.Options);
    }
}
