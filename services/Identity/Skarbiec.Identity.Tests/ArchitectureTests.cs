using NetArchTest.Rules;

namespace Skarbiec.Identity.Tests;

/// <summary>
/// Architecture guardrails (T0.14, ADR-006). No Testcontainers needed — pure reflection over the
/// compiled assembly.
/// </summary>
/// <remarks>
/// Decision: unlike Portfolio/Strategy/Reporting, Identity has no "<c>Data</c> entities must
/// implement <c>IUserOwned</c>" rule here — <c>ApplicationUser</c> is the tenant root itself, not
/// data owned by a user, and <c>RefreshToken</c> is looked up by token hash rather than through
/// the generic per-user query filter, so neither goes through the ADR-006 tenancy plumbing.
/// </remarks>
public sealed class ArchitectureTests
{
    /// <summary>
    /// UserId always comes from the JWT (<c>ICurrentUser</c>), never from caller-supplied input
    /// (ADR-006) — a request record binding it would let one user stamp another user's id.
    /// </summary>
    [Fact]
    public void RequestTypes_NeverExposeSettable_UserId()
    {
        var requestTypes = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .ResideInNamespaceStartingWith("Skarbiec.Identity.Features")
            .And()
            .HaveNameEndingWith("Request")
            .GetTypes();

        Assert.All(requestTypes, t => Assert.Null(
            t.GetProperty("UserId")));
    }
}
