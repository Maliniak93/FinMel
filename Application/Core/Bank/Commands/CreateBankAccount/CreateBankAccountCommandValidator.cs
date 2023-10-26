using FluentValidation;

namespace Application.Core.Bank.Commands.CreateBankAccount;
public class CreateBankAccountCommandValidator : AbstractValidator<CreateBankAccountCommand>
{
    public CreateBankAccountCommandValidator()
    {
        RuleFor(x => x.AccountNumber).NotEmpty().WithMessage("Account number is required.")
            .Matches(@"^\d{26}$").WithMessage("Account number must consist of 26 digits.");

        RuleFor(x => x.Balance).GreaterThanOrEqualTo(0).WithMessage("Account balance must be a non-negative number.");

        RuleFor(x => x.ClientNumber).NotEmpty().WithMessage("Client number is required.");

        RuleFor(x => x.ClientName).NotEmpty().WithMessage("Client name is required.");

        RuleFor(x => x.AccountName).NotEmpty().WithMessage("Account name is required.");

        RuleFor(x => x.CurrencyId).NotEmpty().WithMessage("Currency is required.");

        RuleFor(x => x.IntrestRate).GreaterThanOrEqualTo(0).WithMessage("Interest rate must be a non-negative number.");
    }
}
