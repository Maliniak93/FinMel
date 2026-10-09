using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

// One pending rebuild per portfolio; the consumer deletes it only if Revision is still the one it read.
public sealed class HistoryRebuildRequest : IUserOwned
{
    public required Guid PortfolioId { get; init; }

    // Set by the requesting consumer from the event, not stamped by UserOwnedSaveInterceptor.
    public Guid UserId { get; set; }

    // The earliest date still to be rewritten.
    public required DateOnly FromDate { get; set; }

    // Bumped by every request, so an edit committed during a rebuild keeps the row for another one.
    public required long Revision { get; set; }
}
