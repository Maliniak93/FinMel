using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Transfers;

internal static class TransferErrors
{
    /// <summary>
    /// The counterpart comes from a pick list, so anything outside it is bad input (400): not the
    /// user's asset (a stranger's id included — the same answer, so nothing leaks), the same asset, a
    /// route <see cref="TransferRoutes"/> does not allow, another currency, or an archived portfolio.
    /// </summary>
    public static readonly Error InvalidCounterpart =
        new("Validation.InvalidTransferCounterpart", "This asset can't be the other side of the transfer — pick one from the list.");

    /// <summary>The transfer would take the source's running balance below zero somewhere in its history.</summary>
    public static readonly Error InsufficientFunds =
        new("Validation.InsufficientFunds", "The source of funds can't cover this amount on this date.");

    /// <summary>A manual transfer (savings-cash-transfers) dated after today's Europe/Warsaw date.</summary>
    public static readonly Error DateInFuture =
        new("Validation.TransferDateInFuture", "The transfer date can't be in the future.");

    /// <summary>No transfer of the current user has this id — a stranger's id included, so nothing leaks.</summary>
    public static Error NotFound(Guid transferId) =>
        new("NotFound.Transfer", $"Transfer '{transferId}' was not found.");

    /// <summary>
    /// UpdateTransaction/DeleteTransaction on a transfer leg — only the transfer's entry point changes it;
    /// also DeleteTransfer on a route that is not manual, whose own slice owns it.
    /// </summary>
    public static readonly Error LegManaged =
        new("Conflict.TransferLegManaged", "This transaction is one side of a transfer — change it through the transfer instead.");
}
