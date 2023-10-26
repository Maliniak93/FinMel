using FluentValidation;

namespace Application.Core.Bank.Queries.GetStatementTransaction;
public class GetStatementTransactionQueryValidation : AbstractValidator<GetStatementTransactionQuery>
{
    public GetStatementTransactionQueryValidation()
    {
        RuleFor(x => x.SpecParameters.SearchYear).InclusiveBetween(2000, 2100)
            .WithMessage("Year should be after 2000");
    }
}
