namespace Domain.Specifications.StatementSpecification;
public class BankStatementsSpecificationParameters
{
    public string Sort { get; set; } = "desc";
    public int SearchYear { get; set; } = DateTime.Now.Year;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    //private string _search;
    //public string Search
    //{
    //    get => _search;
    //    set => _search = value.ToLower();
    //}
}
