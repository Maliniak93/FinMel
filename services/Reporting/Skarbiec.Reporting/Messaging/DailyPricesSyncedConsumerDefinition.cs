using Skarbiec.Reporting.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.Reporting.Messaging;

/// <summary>T0.12 inbox template — see <c>IdempotentConsumerDefinition</c> for what this wires onto the consumer's receive endpoint.</summary>
public sealed class DailyPricesSyncedConsumerDefinition : IdempotentConsumerDefinition<DailyPricesSyncedConsumer, ReportingDbContext>;
