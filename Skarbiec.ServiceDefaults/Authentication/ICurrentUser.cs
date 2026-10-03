namespace Skarbiec.ServiceDefaults.Authentication;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }
}
