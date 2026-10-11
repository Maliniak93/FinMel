using Skarbiec.Reporting.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.Reporting.Messaging;

public sealed class HistoryRebuildConsumerDefinition : IdempotentConsumerDefinition<HistoryRebuildConsumer, ReportingDbContext>
{
    public HistoryRebuildConsumerDefinition() => Endpoint(e => e.Name = "reporting-history-rebuild");
}
