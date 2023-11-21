using FluentValidation;

namespace Application.Core.Bank.Commands.UpdateTransactionType;
public class UpdateTransactionTypeCommandValidator : AbstractValidator<UpdateTransactionTypeCommand>
{
    public UpdateTransactionTypeCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0.");

        RuleFor(command => command.TransactionType)
                .MaximumLength(20).WithMessage("Type field cannot exceed 20 characters.");
    }
}
