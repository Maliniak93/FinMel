namespace Skarbiec.MarketData.Sources.MfBonds;

public sealed class MfBondSourceOptions
{
    public const string SectionName = "BondCatalog";

    public Uri PageUrl { get; set; } = new("https://www.gov.pl/web/finanse/obligacje-detaliczne1");

    public string FileName { get; set; } = "Dane_dotyczace_obligacji_detalicznych.xls";
}
