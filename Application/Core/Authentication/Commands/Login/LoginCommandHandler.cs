using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Domain.Common;
using Domain.Entities.Identity;
using Domain.Errors;
using Microsoft.AspNetCore.Identity;

namespace Application.Core.Authentication.Commands.Login;

public record LoginCommand(string Email, string Password) : ICommand<LoginDto>;


public class LoginCommandHandler : ICommandHandler<LoginCommand, LoginDto>
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IToken _token;

    public LoginCommandHandler(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IToken token)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _token = token;
    }
    public async Task<Result<LoginDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user == null) return Result.Failure<LoginDto>(DomainErrors.Authentication.InvalidEmailOrPassword);

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

        if (!result.Succeeded) return Result.Failure<LoginDto>(DomainErrors.Authentication.InvalidEmailOrPassword);

        return Result.Success(new LoginDto(user.Email, _token.CreateToken(user), user.DisplayName));
    }
}
