using Microsoft.Extensions.DependencyInjection;

namespace Skarbiec.ServiceDefaults.Http;

public static class HttpClientBuilderExtensions
{
    /// <summary>Adds <see cref="JwtForwardingHandler"/> to a typed/named client calling another Skarbiec service.</summary>
    public static IHttpClientBuilder AddJwtForwardingHandler(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<JwtForwardingHandler>();
}
