using FluentValidation;

namespace Application.Core.Dashboard.Commands.CreateMainDashboard;
public class CreateMainDashboardCommandValidator : AbstractValidator<CreateMainDashboardCommand>
{
    public CreateMainDashboardCommandValidator()
    {
        RuleFor(command => command.year)
            .GreaterThanOrEqualTo(2020).WithMessage("Year must be equal to or greater than 2020.");

        RuleFor(command => command.month)
            .InclusiveBetween(1, 12).WithMessage("Month should be int from 1 to 12");

        RuleFor(command => new DateTime(command.year, command.month, 1))
            .LessThan(DateTime.Now.AddMonths(-1)).WithMessage("Date cannot be never than previous month");
    }
}
