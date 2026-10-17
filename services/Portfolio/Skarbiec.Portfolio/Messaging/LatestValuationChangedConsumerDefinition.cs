using Skarbiec.Portfolio.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.Portfolio.Messaging;

public sealed class LatestValuationChangedConsumerDefinition
    : IdempotentConsumerDefinition<LatestValuationChangedConsumer, PortfolioDbContext>;
