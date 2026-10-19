using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

internal static class NetWorthHistoryErrors
{
    public static Error InvalidRange(string range) =>
        new("Validation.InvalidRange", $"Range '{range}' is not one of: 1M, 1Y, YTD, MAX.");

    public static readonly Error InvalidGroupFilter =
        new("Validation.InvalidGroupFilter", "A group and 'ungrouped' cannot be combined.");

    public static Error InvalidAssetClass(AssetClass assetClass) =>
        new("Validation.InvalidAssetClass", $"Asset class '{(int)assetClass}' is not defined.");
}
