namespace Skarbiec.ServiceDefaults.Tenancy;

/// <summary>
/// Exposes the current user's id as an instance member on the <c>DbContext</c> itself. EF Core
/// caches the compiled model per context type and only re-binds query filter closures rooted at
/// <c>this</c> to the actual executing context instance — a filter built from a *separately*
/// captured <see cref="Authentication.ICurrentUser"/> would permanently bake in whichever
/// instance happened to build the model first. <see cref="TenancyModelBuilderExtensions.ApplyUserOwnedQueryFilters{TContext}"/>
/// relies on this to stay correct per instance.
/// </summary>
public interface ITenantScopedDbContext
{
    Guid CurrentUserId { get; }
}
