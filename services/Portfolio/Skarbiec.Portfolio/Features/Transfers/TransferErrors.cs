using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Transfers;

internal static class TransferErrors
{
    public static readonly Error InvalidCounterpart =
        new("Validation.InvalidTransferCounterpart", "This asset can't be the other side of the transfer — pick one from the list.");

    public static readonly Error InsufficientFunds =
        new("Validation.InsufficientFunds", "The source of funds can't cover this amount on this date.");

    public static readonly Error DateInFuture =
        new("Validation.TransferDateInFuture", "The transfer date can't be in the future.");

    public static Error NotFound(Guid transferId) =>
        new("NotFound.Transfer", $"Transfer '{transferId}' was not found.");

    public static readonly Error LegManaged =
        new("Conflict.TransferLegManaged", "This transaction is one side of a transfer — change it through the transfer instead.");
}
