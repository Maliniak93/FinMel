namespace Skarbiec.ServiceDefaults.Tenancy;

// EF re-binds only filter closures rooted at this per instance; a separately captured ICurrentUser would bake in the first one.
public interface ITenantScopedDbContext
{
    Guid CurrentUserId { get; }
}
