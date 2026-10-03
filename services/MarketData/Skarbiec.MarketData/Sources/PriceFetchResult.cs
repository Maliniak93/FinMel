namespace Skarbiec.MarketData.Sources;

// No data (a closed market) is not an error, so a job never retries or alerts on a weekend.
public enum PriceFetchOutcome
{
    Success,
    NoData,
    Error,
}

// Never an exception for an expected case, so one bad ticker cannot take down the whole job.
public sealed class PriceFetchResult<TValue>
{
    public PriceFetchOutcome Outcome { get; }
    public IReadOnlyList<TValue> Values { get; }
    public string? ErrorReason { get; }

    private PriceFetchResult(PriceFetchOutcome outcome, IReadOnlyList<TValue> values, string? errorReason)
    {
        Outcome = outcome;
        Values = values;
        ErrorReason = errorReason;
    }

    public static PriceFetchResult<TValue> Success(IReadOnlyList<TValue> values) =>
        new(PriceFetchOutcome.Success, values, null);

    public static PriceFetchResult<TValue> NoData() =>
        new(PriceFetchOutcome.NoData, [], null);

    public static PriceFetchResult<TValue> Error(string reason) =>
        new(PriceFetchOutcome.Error, [], reason);
}
