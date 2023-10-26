namespace Domain.Specifications.StatementSpecification;
public class BankStatementsSpecificationParameters
{
    public string Sort { get; set; }
    public int SearchYear { get; set; } = DateTime.Now.Year;
    //private string _search;
    //public string Search
    //{
    //    get => _search;
    //    set => _search = value.ToLower();
    //}
}
