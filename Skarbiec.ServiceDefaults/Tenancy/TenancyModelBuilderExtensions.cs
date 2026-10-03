using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Skarbiec.ServiceDefaults.Tenancy;

public static class TenancyModelBuilderExtensions
{
    // Takes the context, not ICurrentUser: see ITenantScopedDbContext.
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
