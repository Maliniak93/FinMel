using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class MetalTradeFixtures
{
    public static Transaction Trade(TransactionType type, decimal pieces, Guid? transferId, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Type = type,
        Quantity = pieces,
        UnitPriceAmount = 0m,
        Date = date,
        TransferId = transferId
    };

    public static Transaction CashLeg(TransactionType type, Guid transferId, decimal amount, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Type = type,
        Quantity = amount,
        UnitPriceAmount = 1m,
        FxRateToPln = 1m,
        Date = date,
        TransferId = transferId
    };
}
