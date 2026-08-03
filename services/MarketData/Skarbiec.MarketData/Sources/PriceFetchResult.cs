namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Distinguishes "no data" (non-trading day — weekend/holiday) from "error" (malformed payload,
/// transport failure). Collapsing these into one failure case would make PriceSyncJob (T2.6) retry
/// or alert on a plain closed market — the distinction is a hard requirement (E4).
/// </summary>
public enum PriceFetchOutcome
{
    Success,
    NoData,
    Error,
}

/// <summary>Outcome of an <see cref="IPriceSource"/> fetch — never an exception for an expected
/// no-data/error case, so one bad ticker or a closed market can't take down the whole job (T2.6).</summary>
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
