namespace Skarbiec.Portfolio.Quotes;

public static class QuotesSourceExtensions
{
    private static readonly Uri QuotesApiBaseAddress = new("https://quotes.fixture.test/");

    public static TBuilder AddQuotesSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<QuotesApiClient>(client => client.BaseAddress = QuotesApiBaseAddress);

        return builder;
    }
}
