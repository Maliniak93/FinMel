namespace Skarbiec.MarketData.Sources.MfBonds;

// Throws when the page is unreachable, has no link to the file, or the download fails.
public interface IMfBondSource
{
    Task<byte[]> FetchFileAsync(CancellationToken cancellationToken);
}
