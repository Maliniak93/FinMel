using Microsoft.Extensions.Options;

namespace Skarbiec.MarketData.Sources.MfBonds;

public sealed class MfBondSource(HttpClient httpClient, IOptions<MfBondSourceOptions> options) : IMfBondSource
{
    public async Task<byte[]> FetchFileAsync(CancellationToken cancellationToken)
    {
        var pageUrl = options.Value.PageUrl;
        var fileName = options.Value.FileName;

        var html = await httpClient.GetStringAsync(pageUrl, cancellationToken);
        var fileUrl = MfBondFileLink.Find(html, pageUrl, fileName)
            ?? throw new InvalidOperationException($"No link to '{fileName}' on {pageUrl}.");

        return await httpClient.GetByteArrayAsync(fileUrl, cancellationToken);
    }
}
