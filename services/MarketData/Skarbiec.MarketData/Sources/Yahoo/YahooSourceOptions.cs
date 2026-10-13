namespace Skarbiec.MarketData.Sources.Yahoo;

public sealed class YahooSourceOptions
{
    public const string SectionName = "Yahoo";

    public Uri BaseUrl { get; set; } = new("https://query1.finance.yahoo.com/");
}
