using Microsoft.AspNetCore.Identity;
using Skarbiec.Contracts;
using Skarbiec.Identity.Data;

namespace Skarbiec.Identity.Features.Register;

public sealed class RegisterHandler(UserManager<ApplicationUser> userManager)
{
    // UserManager<TUser> predates CancellationToken support and has no overload to forward it to.
    public async Task<Result<RegisterResponse>> HandleAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        var identityResult = await userManager.CreateAsync(user, request.Password);

        if (!identityResult.Succeeded)
        {
            return MapError(identityResult);
        }

        return new RegisterResponse
        {
            UserId = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName
        };
    }

    private static Error MapError(IdentityResult identityResult)
    {
        var errors = identityResult.Errors.ToArray();
        var message = string.Join(" ", errors.Select(e => e.Description));

        var isDuplicate = errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");

        return isDuplicate
            ? new Error("Conflict.DuplicateEmail", message)
            : new Error("Validation.Register", message);
    }
}
