namespace Skarbiec.Portfolio.Features.Transfers;

/// <summary>Which side of a transfer a leg is, seen from its own asset.</summary>
public enum TransferDirection
{
    /// <summary>The source's Withdraw leg — the money left this asset.</summary>
    Out,

    /// <summary>The target's Deposit leg — the money arrived in this asset.</summary>
    In,
}
