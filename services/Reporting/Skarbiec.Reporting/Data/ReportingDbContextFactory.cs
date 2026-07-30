using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>: design-time tooling builds the model without
/// running the app (and without Aspire injecting the real connection string), so a placeholder
/// Npgsql connection is enough — nothing here ever actually connects.
/// </summary>
public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ReportingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=reporting_db;Username=reporting_user;Password=design-time-only");

        return new ReportingDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }
}
