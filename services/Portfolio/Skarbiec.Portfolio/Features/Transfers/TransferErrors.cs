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

    /// <summary>UpdateTransaction/DeleteTransaction on a transfer leg — only the transfer's entry point changes it.</summary>
    public static readonly Error LegManaged =
        new("Conflict.TransferLegManaged", "This transaction is one side of a transfer — change it through the transfer instead.");
}
