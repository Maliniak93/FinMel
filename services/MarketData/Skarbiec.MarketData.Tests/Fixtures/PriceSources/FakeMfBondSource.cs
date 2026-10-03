using Skarbiec.MarketData.Sources.MfBonds;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

public sealed class FakeMfBondSource : IMfBondSource
{
    public const string FixtureFileName = "mf-bonds.xls";

    private readonly byte[]? _file;
    private readonly Exception? _throwOnRequest;

    private FakeMfBondSource(byte[]? file, Exception? throwOnRequest)
    {
        _file = file;
        _throwOnRequest = throwOnRequest;
    }

    public static FakeMfBondSource FromFixture() => new(RecordedResponse.ReadBytes(FixtureFileName), throwOnRequest: null);

    public static FakeMfBondSource WithFile(byte[] file) => new(file, throwOnRequest: null);

    public static FakeMfBondSource ThrowingOnRequest(Exception exception) => new(file: null, throwOnRequest: exception);

    public int FetchCount { get; private set; }

    public Task<byte[]> FetchFileAsync(CancellationToken cancellationToken)
    {
        FetchCount++;
        return _throwOnRequest is not null ? Task.FromException<byte[]>(_throwOnRequest) : Task.FromResult(_file!);
    }
}
