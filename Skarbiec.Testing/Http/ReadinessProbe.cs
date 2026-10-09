using System.Net;

namespace Skarbiec.Testing.Http;

public static class ReadinessProbe
{
    // MassTransit's health check briefly reports not started after the host comes up, so readiness is polled with a deadline.
    public static async Task<HttpResponseMessage> GetReadyAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        HttpResponseMessage response;
        do
        {
            response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return response;
    }
}
