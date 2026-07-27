using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;

namespace Skarbiec.Identity.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(200);
            user.Property(u => u.BaseCurrency).HasMaxLength(3).HasDefaultValue(Money.BaseCurrency);
        });

        builder.Entity<RefreshToken>(refreshToken =>
        {
            refreshToken.HasKey(t => t.Id);
            refreshToken.Property(t => t.TokenHash).HasMaxLength(64);
            refreshToken.HasIndex(t => t.TokenHash).IsUnique();
            refreshToken.HasIndex(t => t.UserId);
        });
    }
}
