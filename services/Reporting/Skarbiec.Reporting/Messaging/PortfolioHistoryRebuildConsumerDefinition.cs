using Skarbiec.Reporting.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.Reporting.Messaging;

public sealed class PortfolioHistoryRebuildConsumerDefinition : IdempotentConsumerDefinition<PortfolioHistoryRebuildConsumer, ReportingDbContext>
{
    public PortfolioHistoryRebuildConsumerDefinition() => Endpoint(e => e.Name = "reporting-portfolio-history-rebuild");
}
