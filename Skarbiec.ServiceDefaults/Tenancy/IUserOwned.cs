namespace Skarbiec.ServiceDefaults.Tenancy;

// Filtered by ApplyUserOwnedQueryFilters and stamped by UserOwnedSaveInterceptor; never set UserId from request input.
public interface IUserOwned
{
    Guid UserId { get; set; }
}
