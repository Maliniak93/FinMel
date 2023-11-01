using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.Common;
using Domain.Entities.Identity;
using Domain.Errors;
using Microsoft.AspNetCore.Identity;

namespace Application.Core.Authentication.Commands.Register;

public record RegisterCommand(string Email, string Password, string DisplayName) : ICommand<RegisterDto>;
public class RegisterCommandHandler : ICommandHandler<RegisterCommand, RegisterDto>
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IToken _token;

    public RegisterCommandHandler(UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IToken token)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _token = token;
    }
    public async Task<Result<RegisterDto>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var user = new AppUser(request.DisplayName, request.Email);

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Result.Failure<RegisterDto>(DomainErrors.Authentication.RegisterError(
            result.Errors.FirstOrDefault().Code,
            result.Errors.FirstOrDefault().Description));
        }

        return Result.Success(new RegisterDto(user.Email, _token.CreateToken(user), user.DisplayName));
    }
}
