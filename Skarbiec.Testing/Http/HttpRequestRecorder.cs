using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Skarbiec.Testing.Http;

/// <summary>What an outgoing service-to-service request looked like when it reached the wire.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The final request URI, after service discovery resolved the base address.</param>
/// <param name="Authorization">The raw <c>Authorization</c> header, or <see langword="null"/> when none was sent.</param>
public sealed record RecordedHttpRequest(HttpMethod Method, Uri Uri, string? Authorization);

/// <summary>
/// Stands in for the network under a service's typed <see cref="HttpClient"/>s: every outgoing
/// request is recorded (after the whole delegating-handler pipeline has run, so headers a handler
/// adds are visible) and answered by <c>respond</c> instead of leaving the process. Plug it into a
/// test host with <see cref="HttpRequestRecorderExtensions.WithRecordedOutboundHttp{TProgram}"/>.
/// </summary>
/// <param name="respond">Builds the response per request; defaults to an empty <c>200 OK</c>.</param>
public sealed class HttpRequestRecorder(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
{
    private readonly ConcurrentQueue<RecordedHttpRequest> _requests = new();
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond =
        respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK));

    /// <summary>Every request seen so far, in arrival order.</summary>
    public IReadOnlyList<RecordedHttpRequest> Requests => [.. _requests];

    /// <summary>
    /// A fresh primary handler writing into this recorder — <c>IHttpClientFactory</c> expects a new
    /// handler instance per pipeline it builds.
    /// </summary>
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
    /// <summary>
    /// A copy of <paramref name="factory"/> whose every <see cref="HttpClient"/> built through
    /// <c>IHttpClientFactory</c> (typed clients included) sends into <paramref name="recorder"/>
    /// instead of the network. The service's own delegating handlers (resilience, service
    /// discovery, anything a typed client adds) still run in front of it. Dispose the returned
    /// factory with <c>await using</c>.
    /// </summary>
    public static WebApplicationFactory<TProgram> WithRecordedOutboundHttp<TProgram>(
        this WebApplicationFactory<TProgram> factory, HttpRequestRecorder recorder)
        where TProgram : class =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(recorder.CreateHandler))));
}
