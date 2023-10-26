using FluentValidation;

namespace Application.Core.Bank.Commands.UpdateBankAccount;
public class UpdateBankAccountCommandValidator : AbstractValidator<UpdateBankAccountCommand>
{
    public UpdateBankAccountCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0.");
        RuleFor(x => x.ClientName).NotEmpty().MaximumLength(255).WithMessage("ClientName field cannot exceed 255 characters.");
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(255).WithMessage("AccountName field cannot exceed 255 characters.");
        RuleFor(x => x.IntrestRate).InclusiveBetween(0, 100).WithMessage("IntrestRate must be a number between 0 and 100.");

    }
}
