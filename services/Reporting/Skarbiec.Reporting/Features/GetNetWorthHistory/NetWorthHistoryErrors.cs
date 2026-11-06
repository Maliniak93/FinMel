using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

internal static class NetWorthHistoryErrors
{
    public static Error InvalidRange(string range) =>
        new("Validation.InvalidRange", $"Range '{range}' is not one of: 1M, 1Y, YTD, MAX; or pass both 'from' and 'to' (yyyy-MM-dd) instead of a range.");

    public static readonly Error RangeWithDates =
        new("Validation.InvalidRange", "'range' cannot be combined with 'from' or 'to'; use one of: 1M, 1Y, YTD, MAX, or both 'from' and 'to' (yyyy-MM-dd).");

    public static readonly Error IncompleteDates =
        new("Validation.InvalidRange", "'from' and 'to' (yyyy-MM-dd) must be given together.");

    public static readonly Error FromAfterTo =
        new("Validation.InvalidRange", "'from' cannot be later than 'to'.");

    public static readonly Error InvalidGroupFilter =
        new("Validation.InvalidGroupFilter", "A group and 'ungrouped' cannot be combined.");

    public static Error InvalidAssetClass(AssetClass assetClass) =>
        new("Validation.InvalidAssetClass", $"Asset class '{(int)assetClass}' is not defined.");
}
