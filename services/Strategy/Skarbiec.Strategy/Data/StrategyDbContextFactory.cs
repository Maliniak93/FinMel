using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Strategy.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>: design-time tooling builds the model without
/// running the app (and without Aspire injecting the real connection string), so a placeholder
/// Npgsql connection is enough — nothing here ever actually connects.
/// </summary>
public sealed class StrategyDbContextFactory : IDesignTimeDbContextFactory<StrategyDbContext>
{
    public StrategyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StrategyDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=strategy_db;Username=strategy_user;Password=design-time-only");

        return new StrategyDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }
}
