namespace Skarbiec.MarketData.Sources.GoldApi;

public sealed class GoldApiSourceOptions
{
    public const string SectionName = "GoldApi";

    public Uri BaseUrl { get; set; } = new("https://api.gold-api.com/");
}
