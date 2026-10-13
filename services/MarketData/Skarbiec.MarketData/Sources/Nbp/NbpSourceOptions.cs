namespace Skarbiec.MarketData.Sources.Nbp;

public sealed class NbpSourceOptions
{
    public const string SectionName = "Nbp";

    public Uri BaseUrl { get; set; } = new("https://api.nbp.pl/api/");
}
