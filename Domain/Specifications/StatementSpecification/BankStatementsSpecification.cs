using Domain.Common;
using Domain.Entities.Bank;

namespace Domain.Specifications.StatementSpecification;
public class BankStatementsSpecification : BaseSpecification<BankStatement>
{
    public BankStatementsSpecification(BankStatementsSpecificationParameters parameters) : base(x =>
    /*(string.IsNullOrEmpty(parameters.Search) && */x.StatementTo.Year == parameters.SearchYear)
    {
        if (!string.IsNullOrEmpty(parameters.Sort))
        {
            AddOrderBy(d => d.StatementTo);
        }
        AddOrderByDesc(d => d.StatementTo);
    }
}
