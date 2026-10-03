using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Testing.Sample.Data;

public sealed class NotesDbContextFactory : IDesignTimeDbContextFactory<NotesDbContext>
{
    public NotesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotesDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=notes_db;Username=notes_user;Password=design-time-only");

        return new NotesDbContext(optionsBuilder.Options, new DesignTimeCurrentUser());
    }
}
