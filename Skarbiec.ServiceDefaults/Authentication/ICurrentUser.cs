namespace Skarbiec.ServiceDefaults.Authentication;

/// <summary>
/// The authenticated caller's identity, read from JWT claims. <c>UserId</c> must never be taken
/// from the request body or route (ADR-006).
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }
}
