using FluentValidation;

namespace Application.Core.Authentication.Commands.Login;
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Invalid email address.");

        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}
