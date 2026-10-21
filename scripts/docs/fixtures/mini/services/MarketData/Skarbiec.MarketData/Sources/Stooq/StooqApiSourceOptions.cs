namespace Skarbiec.MarketData.Sources.StooqApi;

public sealed class StooqApiSourceOptions
{
    public const string SectionName = "StooqApi";

    public Uri BaseUrl { get; set; } = new("https://api.stooq.example/");
}
