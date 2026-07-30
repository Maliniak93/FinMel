using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Skarbiec.ServiceDefaults.Tenancy;

public static class TenancyModelBuilderExtensions
{
    /// <summary>
    /// Adds <c>e => e.UserId == context.CurrentUserId</c> as a global query filter to every
    /// entity type in the model implementing <see cref="IUserOwned"/> (ADR-006). One
    /// reflection-based pass instead of a per-entity <c>HasQueryFilter</c> call, so new
    /// user-owned entities are covered automatically as they're added in later phases. Takes the
    /// context itself (not <see cref="Authentication.ICurrentUser"/> directly) — see
    /// <see cref="ITenantScopedDbContext"/> for why that's required for correctness.
    /// </summary>
    public static void ApplyUserOwnedQueryFilters<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        var userIdProperty = typeof(IUserOwned).GetProperty(nameof(IUserOwned.UserId))!;
        var currentUserIdProperty = typeof(ITenantScopedDbContext).GetProperty(nameof(ITenantScopedDbContext.CurrentUserId))!;

        var contextConstant = Expression.Constant(context, typeof(TContext));
        var currentUserId = Expression.Property(contextConstant, currentUserIdProperty);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IUserOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var entity = Expression.Parameter(entityType.ClrType, "e");
            var entityUserId = Expression.Property(entity, userIdProperty);
            var filter = Expression.Lambda(Expression.Equal(entityUserId, currentUserId), entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
