using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

internal static class NetWorthHistoryErrors
{
    public static Error InvalidRange(string range) =>
        new("Validation.InvalidRange", $"Range '{range}' is not one of: 1M, 1Y, YTD, MAX.");
}
