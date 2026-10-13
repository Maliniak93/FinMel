using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Skarbiec.Gateway.Tests.Infrastructure;

internal sealed class StubUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;

    private StubUpstream(WebApplication app, Uri address)
    {
        _app = app;
        Address = address;
    }

    public Uri Address { get; }

    public static async Task<StubUpstream> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapFallback(() => Results.Ok());
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new StubUpstream(app, new Uri(address));
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
