using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Testing.Sample.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>: design-time tooling builds the model without
/// running the app, so a placeholder Npgsql connection is enough — nothing here ever actually connects.
/// </summary>
public sealed class NotesDbContextFactory : IDesignTimeDbContextFactory<NotesDbContext>
{
    public NotesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotesDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=notes_db;Username=notes_user;Password=design-time-only");

        return new NotesDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }
}
