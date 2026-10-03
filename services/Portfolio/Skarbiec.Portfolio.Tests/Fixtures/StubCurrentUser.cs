using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal sealed class StubCurrentUser(Guid userId) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId => userId;
}
