using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

/// <summary>
/// A manual transfer (savings-cash-transfers): <see cref="Amount"/> moves out of the source and into
/// the target on <see cref="Date"/>. The date must not be after today (Europe/Warsaw) — checked in the
/// handler, which owns the clock.
/// </summary>
public sealed record CreateTransferRequest
{
    public required Guid SourceAssetId { get; init; }

    public required Guid TargetAssetId { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true)]
    public required decimal Amount { get; init; }

    public required DateOnly Date { get; init; }
}
