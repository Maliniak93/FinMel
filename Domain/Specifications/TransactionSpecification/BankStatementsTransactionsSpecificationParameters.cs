namespace Domain.Specifications.TransactionSpecification;
public class BankStatementsTransactionsSpecificationParameters
{
    public string Sort { get; set; } = "desc";
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    //private string _search;
    //public string Search
    //{
    //    get => _search;
    //    set => _search = value.ToLower();
    //}
}
