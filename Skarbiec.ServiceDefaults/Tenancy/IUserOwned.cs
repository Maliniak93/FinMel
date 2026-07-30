namespace Skarbiec.ServiceDefaults.Tenancy;

/// <summary>
/// Marks an entity as scoped to a single user (ADR-006). <see cref="TenancyModelBuilderExtensions.ApplyUserOwnedQueryFilters"/>
/// adds a global query filter for every implementing entity type, and <see cref="UserOwnedSaveInterceptor"/>
/// stamps <see cref="UserId"/> from the current JWT on insert — never set it from request input.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; set; }
}
