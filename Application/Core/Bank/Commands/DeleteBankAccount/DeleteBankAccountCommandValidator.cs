using FluentValidation;

namespace Application.Core.Bank.Commands.DeleteBankAccount;
public class DeleteBankAccountCommandValidator : AbstractValidator<DeleteBankAccountCommand>
{
    public DeleteBankAccountCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0.");
    }
}
