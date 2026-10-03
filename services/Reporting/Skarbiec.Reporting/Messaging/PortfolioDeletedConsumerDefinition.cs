using Skarbiec.Reporting.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.Reporting.Messaging;

public sealed class PortfolioDeletedConsumerDefinition : IdempotentConsumerDefinition<PortfolioDeletedConsumer, ReportingDbContext>;
