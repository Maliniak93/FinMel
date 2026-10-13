using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Sources.Yahoo;

namespace Skarbiec.MarketData.Tests;

public sealed class SourceBaseAddressTests
{
    private static (Uri? Yahoo, Uri? Nbp) ResolveBaseAddresses(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddYahooSource();
        builder.AddNbpSources();

        using var host = builder.Build();
        var factory = host.Services.GetRequiredService<IHttpClientFactory>();

        return (
            factory.CreateClient(nameof(IYahooApiClient)).BaseAddress,
            factory.CreateClient(nameof(INbpApiClient)).BaseAddress);
    }

    [Fact]
    public void TypedClients_WithoutConfig_UseProviderDefaults()
    {
        var (yahoo, nbp) = ResolveBaseAddresses([]);

        Assert.Multiple(
            () => Assert.Equal(new Uri("https://query1.finance.yahoo.com/"), yahoo),
            () => Assert.Equal(new Uri("https://api.nbp.pl/api/"), nbp));
    }

    [Fact]
    public void TypedClients_WithConfiguredBaseUrls_UseThem()
    {
        var (yahoo, nbp) = ResolveBaseAddresses(new()
        {
            ["Yahoo:BaseUrl"] = "http://market-stub:8080/yahoo/",
            ["Nbp:BaseUrl"] = "http://market-stub:8080/nbp/api/",
        });

        Assert.Multiple(
            () => Assert.Equal(new Uri("http://market-stub:8080/yahoo/"), yahoo),
            () => Assert.Equal(new Uri("http://market-stub:8080/nbp/api/"), nbp));
    }
}
