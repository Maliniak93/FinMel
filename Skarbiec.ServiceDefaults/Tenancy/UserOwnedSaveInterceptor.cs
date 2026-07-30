using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.ServiceDefaults.Tenancy;

/// <summary>
/// Stamps <see cref="IUserOwned.UserId"/> from the current JWT on every newly-added user-owned
/// entity (ADR-006) — the only place <c>UserId</c> is ever set, never from request input. Register
/// per <see cref="DbContext"/> instance via constructor-injected <see cref="ICurrentUser"/> and
/// <c>optionsBuilder.AddInterceptors(...)</c> in <c>OnConfiguring</c>, since this interceptor needs
/// the request-scoped current user and can't be a shared singleton.
/// </summary>
public sealed class UserOwnedSaveInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampUserIds(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        StampUserIds(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampUserIds(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IUserOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.UserId = currentUser.UserId;
            }
        }
    }
}
