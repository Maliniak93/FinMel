using Microsoft.AspNetCore.Identity;

namespace Skarbiec.Identity.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
}
