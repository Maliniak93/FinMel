using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// <see cref="ICurrentUser"/> for tests that construct a DbContext directly instead of going
/// through the request pipeline — there is no <c>HttpContext</c> to read the JWT claim from.
/// </summary>
internal sealed class StubCurrentUser(Guid userId) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId => userId;
}
