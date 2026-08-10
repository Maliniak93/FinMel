using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.ServiceDefaults.Tenancy;

/// <summary>
/// Stamps <see cref="IUserOwned.UserId"/> from the current JWT on every newly-added user-owned
/// entity that doesn't already carry one (ADR-006) — never from request input. Register per
/// <see cref="DbContext"/> instance via constructor-injected <see cref="ICurrentUser"/> and
/// <c>optionsBuilder.AddInterceptors(...)</c> in <c>OnConfiguring</c>, since this interceptor needs
/// the request-scoped current user and can't be a shared singleton.
/// </summary>
/// <remarks>
/// The "already set" escape hatch (T2.11) is for system-context writers with no single request
/// user to read from <see cref="ICurrentUser"/> — Reporting's <c>DailyPricesSynced</c> consumer
/// computes snapshots for many users in one message and sets each row's <c>UserId</c> itself from
/// Portfolio's own data (trusted internal source, not a request body — ADR-006's guarantee is
/// unchanged). Every other call site leaves <c>UserId</c> at its <c>Guid.Empty</c> default and is
/// stamped exactly as before.
/// </remarks>
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
            if (entry.State == EntityState.Added && entry.Entity.UserId == Guid.Empty)
            {
                entry.Entity.UserId = currentUser.UserId;
            }
        }
    }
}
