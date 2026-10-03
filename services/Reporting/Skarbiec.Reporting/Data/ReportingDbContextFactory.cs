using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ReportingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=reporting_db;Username=reporting_user;Password=design-time-only");

        return new ReportingDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }
}
