namespace Domain.Specifications.TransactionSpecification;
public class BankStatementsTransactionsSpecificationParameters
{
    public string Sort { get; set; }
    public int SearchYear { get; set; }
    private string _search;
    public string Search
    {
        get => _search;
        set => _search = value.ToLower();
    }
}
