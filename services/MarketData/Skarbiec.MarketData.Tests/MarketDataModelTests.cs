using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Tests;

public sealed class MarketDataModelTests
{
    private static MarketDataDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MarketDataDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options);

    [Fact]
    public void EfModel_ContainsNoQuartzTables()
    {
        using var db = CreateContext();

        var entities = db.Model.GetEntityTypes().ToList();
        Assert.DoesNotContain(entities, e => e.GetTableName()?.StartsWith("qrtz_", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(entities, e => string.Equals(e.GetSchema(), "quartz", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entities, e => e.ClrType.Namespace?.Contains("AppAny", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Migrations_AreSquashedToOneInitialCreate()
    {
        using var db = CreateContext();

        var migrations = ((IInfrastructure<IServiceProvider>)db).Instance.GetService(typeof(IMigrationsAssembly)) as IMigrationsAssembly;

        var only = Assert.Single(migrations!.Migrations);
        Assert.EndsWith("InitialCreate", only.Key, StringComparison.Ordinal);
    }
}
