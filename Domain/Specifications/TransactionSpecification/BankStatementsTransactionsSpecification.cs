using Domain.Common;
using Domain.Entities.Bank;

namespace Domain.Specifications.TransactionSpecification;
public class BankStatementsTransactionsSpecification : BaseSpecification<StatementTransaction>
{
    public BankStatementsTransactionsSpecification(BankStatementsTransactionsSpecificationParameters parameters) : base(x =>
        (string.IsNullOrEmpty(parameters.Search) || x.DescriptionBase.ToLower().Contains(parameters.Search) || x.DescriptionOptional.ToLower().Contains(parameters.Search) /*&&*/
        /*x.TransactionDate.Year == parameters.SearchYear*/))
    {
        if (!string.IsNullOrEmpty(parameters.Sort))
        {
            switch (parameters.Sort)
            {
                case "asc":
                    AddOrderBy(x => x.TransactionDate);
                    break;
                case "desc":
                    AddOrderByDesc(x => x.TransactionDate);
                    break;
                case "low":
                    AddOrderBy(x => x.Value);
                    break;
                case "high":
                    AddOrderByDesc(x => x.Value);
                    break;
            }
        }
    }
}
