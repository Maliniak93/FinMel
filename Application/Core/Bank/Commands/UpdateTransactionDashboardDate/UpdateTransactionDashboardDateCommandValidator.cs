using FluentValidation;

namespace Application.Core.Bank.Commands.UpdateTransactionDashboardDate;
public class UpdateTransactionDashboardDateCommandValidator : AbstractValidator<UpdateTransactionDashboardDateCommand>
{
    public UpdateTransactionDashboardDateCommandValidator()
    {
        RuleFor(x => x.NewDate)
            .GreaterThan(new DateTime(2020, 1, 1))
            .WithMessage("The new date must be after 2020-01-01.");
    }
}
