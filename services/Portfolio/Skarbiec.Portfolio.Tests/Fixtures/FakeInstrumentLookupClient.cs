using System.Text;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// An unscripted instrument is Found as a Stock quoted in USD, unless its id came from IdFor, which encodes the class and currency it answers with.
public sealed class FakeInstrumentLookupClient : IInstrumentLookupClient
{
    private static readonly byte[] Marker = [0x5E, 0xED, 0xCA, 0xFE];

    private readonly Dictionary<Guid, InstrumentLookupResult> _overrides = [];

    public static Guid IdFor(AssetClass assetClass, string currency)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        Marker.CopyTo(bytes, 0);
        BitConverter.GetBytes((int)assetClass).CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes(currency).CopyTo(bytes, 8);

        return new Guid(bytes);
    }

    public FakeInstrumentLookupClient WithNotFound(Guid instrumentId)
    {
        _overrides[instrumentId] = InstrumentLookupResult.NotFound;
        return this;
    }

    public FakeInstrumentLookupClient WithUnavailable(Guid instrumentId)
    {
        _overrides[instrumentId] = InstrumentLookupResult.Unavailable;
        return this;
    }

    public FakeInstrumentLookupClient WithInstrument(Guid instrumentId, AssetClass assetClass, string currency)
    {
        _overrides[instrumentId] = InstrumentLookupResult.Found(assetClass, currency);
        return this;
    }

    public Task<InstrumentLookupResult> CheckAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        Task.FromResult(_overrides.TryGetValue(instrumentId, out var scripted) ? scripted : Decode(instrumentId));

    private static InstrumentLookupResult Decode(Guid instrumentId)
    {
        var bytes = instrumentId.ToByteArray();
        if (!bytes.AsSpan(0, Marker.Length).SequenceEqual(Marker))
        {
            return InstrumentLookupResult.Found(AssetClass.Stock, "USD");
        }

        return InstrumentLookupResult.Found((AssetClass)BitConverter.ToInt32(bytes, 4), Encoding.ASCII.GetString(bytes, 8, 3));
    }
}
