using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.ServiceDefaults.Tenancy;

/// <summary>
/// Stand-in <see cref="ICurrentUser"/> for <c>IDesignTimeDbContextFactory</c> implementations
/// (<c>dotnet ef migrations add</c>): design-time model building never runs a query or a save, so
/// <see cref="UserId"/> is never actually read — this only exists to satisfy the DbContext
/// constructor outside of a request.
/// </summary>
public sealed class DesignTimeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid UserId => Guid.Empty;
}
