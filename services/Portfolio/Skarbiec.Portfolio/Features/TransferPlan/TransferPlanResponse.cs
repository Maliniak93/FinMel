using System.Text.Json.Serialization;

namespace Skarbiec.Portfolio.Features.TransferPlan;

public sealed record TransferPlanResponse
{
    public required TransferPlanStatus Status { get; init; }

    /// <summary>Why there is no plan; null for a preview.</summary>
    public required TransferPlanNoneReason? NoneReason { get; init; }

    /// <summary>The user's minimum transfer in whole PLN; 0 means no minimum.</summary>
    public required int MinimumTransferPln { get; init; }

    /// <summary>The date of the valuation the shares are computed from, null when there is none.</summary>
    public required DateOnly? ValuationDate { get; init; }

    public required bool ValuationStale { get; init; }

    /// <summary>Why the rows are not split into allocation groups; null when they are.</summary>
    public required TransferPlanFallbackReason? FallbackReason { get; init; }

    public required IReadOnlyList<TransferRowResponse> Rows { get; init; }

    /// <summary>Empty in a fallback.</summary>
    public required IReadOnlyList<TransferGroupShare> Groups { get; init; }

    /// <summary>PLN sum of the buckets the rows split.</summary>
    public required decimal ToInvestPln { get; init; }

    /// <summary>True for an accepted plan while none of the latest batch's rows is checked.</summary>
    public required bool CanUndo { get; init; }

    /// <summary>Per bucket, where the amount to invest now differs from the accepted rows; empty outside an accepted plan.</summary>
    public required IReadOnlyList<TransferDifferenceResponse> Differences { get; init; }

    /// <summary>The preview of the next batch; null when no bucket's difference is positive.</summary>
    public required TransferTopUpResponse? TopUp { get; init; }
}

public sealed record TransferDifferenceResponse
{
    public required Guid BucketId { get; init; }
    public required string BucketName { get; init; }

    /// <summary>Sum of the bucket's rows over all batches.</summary>
    public required decimal AcceptedPln { get; init; }

    /// <summary>The bucket's amount to invest now, 0 when it no longer invests this month.</summary>
    public required decimal CurrentPln { get; init; }

    public required decimal DifferencePln { get; init; }
}

public sealed record TransferTopUpResponse
{
    /// <summary>The user's current minimum transfer in whole PLN; 0 means no minimum.</summary>
    public required int MinimumTransferPln { get; init; }

    /// <summary>The date of the current valuation, null when there is none.</summary>
    public required DateOnly? ValuationDate { get; init; }

    /// <summary>Why the rows are not split into allocation groups; null when they are.</summary>
    public required TransferPlanFallbackReason? FallbackReason { get; init; }

    public required IReadOnlyList<TransferRowResponse> Rows { get; init; }

    /// <summary>Empty in a fallback.</summary>
    public required IReadOnlyList<TransferGroupShare> Groups { get; init; }
}

public sealed record TransferRowResponse
{
    /// <summary>Null in a preview, the row is not stored yet.</summary>
    public required Guid? Id { get; init; }

    public required int? BatchNumber { get; init; }
    public required bool? Checked { get; init; }
    public required Guid BucketId { get; init; }
    public required string BucketName { get; init; }
    public required Guid? AccountId { get; init; }
    public required string? AccountName { get; init; }

    /// <summary>Null for a row outside every allocation group.</summary>
    public required string? GroupName { get; init; }

    public required decimal AmountPln { get; init; }
    public required TransferMarker Marker { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TransferMarker>))]
public enum TransferMarker
{
    None,
    AboveTarget,
    OutsideAllocation,
    BelowMinimum,
}

[JsonConverter(typeof(JsonStringEnumConverter<TransferPlanStatus>))]
public enum TransferPlanStatus
{
    None,
    Preview,
    Accepted,
    Completed,
    Skipped,
}

[JsonConverter(typeof(JsonStringEnumConverter<TransferPlanNoneReason>))]
public enum TransferPlanNoneReason
{
    NothingToInvest,
    FutureMonth,
    BeforeStart,
}

[JsonConverter(typeof(JsonStringEnumConverter<TransferPlanFallbackReason>))]
public enum TransferPlanFallbackReason
{
    NoTargetAllocation,
    NoValuation,
    EmptyBase,
}
