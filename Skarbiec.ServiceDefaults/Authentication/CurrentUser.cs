using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Skarbiec.ServiceDefaults.Authentication;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public Guid UserId => TryGetUserId() is { } userId
        ? userId
        : throw new InvalidOperationException("No authenticated user on the current request.");

    private Guid? TryGetUserId()
    {
        var subject = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value
            ?? httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
