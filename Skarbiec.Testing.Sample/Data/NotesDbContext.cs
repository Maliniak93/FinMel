using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Testing.Sample.Data;

// Exists so TenancyIsolationTests has a real tenancy-filtered resource to prove itself against.
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, ICurrentUser currentUser)
    : DbContext(options), ITenantScopedDbContext
{
    public DbSet<Note> Notes => Set<Note>();

    public Guid CurrentUserId => currentUser.UserId;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new UserOwnedSaveInterceptor(currentUser));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyUserOwnedQueryFilters(this);
    }
}
