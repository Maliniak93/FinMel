using Skarbiec.Reporting.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.Reporting.Messaging;

public sealed class AssetGroupDeletedConsumerDefinition : IdempotentConsumerDefinition<AssetGroupDeletedConsumer, ReportingDbContext>;
