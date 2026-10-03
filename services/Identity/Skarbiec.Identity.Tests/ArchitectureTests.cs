using NetArchTest.Rules;

namespace Skarbiec.Identity.Tests;

// No IUserOwned rule here: ApplicationUser is the tenant root, and RefreshToken is looked up by its hash.
public sealed class ArchitectureTests
{
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
