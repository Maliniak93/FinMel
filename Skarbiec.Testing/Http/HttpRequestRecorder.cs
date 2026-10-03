using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Skarbiec.Testing.Http;

/// <summary>An outgoing service-to-service request as it reached the wire; Uri is after service discovery.</summary>
public sealed record RecordedHttpRequest(HttpMethod Method, Uri Uri, string? Authorization);

// Records after the whole delegating-handler pipeline, so headers a handler adds are visible.
public sealed class HttpRequestRecorder(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
{
    private readonly ConcurrentQueue<RecordedHttpRequest> _requests = new();
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond =
        respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK));

    public IReadOnlyList<RecordedHttpRequest> Requests => [.. _requests];

    // IHttpClientFactory expects a new handler instance per pipeline.
    public HttpMessageHandler CreateHandler() => new RecordingHandler(this);

    private sealed class RecordingHandler(HttpRequestRecorder recorder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var authorization = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(", ", values)
                : null;
            recorder._requests.Enqueue(new RecordedHttpRequest(request.Method, request.RequestUri!, authorization));

            var response = recorder._respond(request);
            response.RequestMessage ??= request;

            return Task.FromResult(response);
        }
    }
}

public static class HttpRequestRecorderExtensions
{
    // The service's own delegating handlers still run in front of the recorder.
    public static WebApplicationFactory<TProgram> WithRecordedOutboundHttp<TProgram>(
        this WebApplicationFactory<TProgram> factory, HttpRequestRecorder recorder)
        where TProgram : class =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(recorder.CreateHandler))));
}
