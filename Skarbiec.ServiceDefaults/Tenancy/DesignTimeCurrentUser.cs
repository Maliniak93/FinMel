using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.ServiceDefaults.Tenancy;

// Only satisfies the DbContext constructor for dotnet ef: design-time model building never reads UserId.
public sealed class DesignTimeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid UserId => Guid.Empty;
}
