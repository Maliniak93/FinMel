using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>: design-time tooling builds the model without
/// running the app (and without Aspire injecting the real connection string), so a placeholder
/// Npgsql connection is enough — nothing here ever actually connects.
/// </summary>
public sealed class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PortfolioDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=portfolio_db;Username=portfolio_user;Password=design-time-only");

        return new PortfolioDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }
}
