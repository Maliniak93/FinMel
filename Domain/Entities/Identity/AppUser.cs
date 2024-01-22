using Microsoft.AspNetCore.Identity;

namespace Domain.Entities.Identity;
public sealed class AppUser : IdentityUser
{
    public AppUser(string displayName, string email)
    {
        DisplayName = displayName;
        Email = email;
        UserName = email;
    }
    
    public string DisplayName { get; set; }
}
