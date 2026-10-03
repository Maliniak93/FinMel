using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.ServiceDefaults.Tenancy;

// A UserId already set is kept: consumers have no request user and write the user their event or Position row names.
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
