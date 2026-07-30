using Microsoft.EntityFrameworkCore;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Testing.Sample.Data;

/// <summary>
/// Exercises the shared tenancy plumbing (ADR-006) end to end over HTTP, so
/// <see cref="Skarbiec.Testing.Tenancy.TenancyIsolationTests{TProgram}"/> (T0.14) has something real
/// to prove itself against.
/// </summary>
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
