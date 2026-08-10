using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features;

internal static class SyncErrors
{
    public static readonly Error AlreadyRunning =
        new("Conflict.SyncAlreadyRunning", "A price sync is already running — wait for it to finish before triggering another.");
}
