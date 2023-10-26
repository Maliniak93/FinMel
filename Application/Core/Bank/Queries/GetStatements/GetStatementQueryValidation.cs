using FluentValidation;

namespace Application.Core.Bank.Queries.GetStatements;
public class GetStatementQueryValidation : AbstractValidator<GetStatementsQuery>
{
    public GetStatementQueryValidation()
    {
        RuleFor(x => x.SpecParameters.SearchYear).InclusiveBetween(2000, 2100)
            .WithMessage("Year should be after 2000");
    }
}
