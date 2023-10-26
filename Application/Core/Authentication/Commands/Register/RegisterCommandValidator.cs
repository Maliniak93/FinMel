using FluentValidation;

namespace Application.Core.Authentication.Commands.Register;
public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {

        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Invalid email address.");

        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).WithMessage("Password must be at least 8 characters long and contain at least one digit, non-alphanumeric character, lowercase, and uppercase letter.");

        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("The display name is required.");
    }
}
