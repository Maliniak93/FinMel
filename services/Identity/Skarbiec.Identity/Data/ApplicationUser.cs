using Microsoft.AspNetCore.Identity;
using Skarbiec.Contracts;

namespace Skarbiec.Identity.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public string BaseCurrency { get; set; } = Money.BaseCurrency;
}
