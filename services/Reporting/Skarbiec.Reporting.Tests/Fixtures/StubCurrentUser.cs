using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.Reporting.Tests.Fixtures;

internal sealed class StubCurrentUser(Guid userId) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId => userId;
}
