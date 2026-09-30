using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.SavingsAccounts;

namespace Skarbiec.Portfolio.Features;

public sealed record AssetResponse
{
    public required Guid Id { get; init; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? ManualValue { get; init; }
    public DateOnly? ManualValueDate { get; init; }
    public Guid? InstrumentId { get; init; }

    /// <summary>How many transactions the asset holds — counted from the Transactions table, never a stored counter (spec-02).</summary>
    public required int TransactionCount { get; init; }

    /// <summary>A Deposit-class asset's maturity date, read from its term-deposit terms; <see langword="null"/> for every other asset (term-deposits).</summary>
    public DateOnly? DepositMaturityDate { get; init; }

    /// <summary>Whether a Deposit-class asset's term deposit is settled, so the asset list drops its "Due" chip; <see langword="null"/> for every other asset (term-deposits-settlement).</summary>
    public bool? DepositSettled { get; init; }

    /// <summary>
    /// Whether a Savings-class asset has an ended, unsettled month with interest, against today's
    /// Europe/Warsaw date, so the asset list shows its "Due" chip; <see langword="null"/> for every
    /// other asset (savings-interest-settlement).
    /// </summary>
    public bool? SavingsInterestDue { get; init; }

    /// <summary>The asset's own archive flag (asset-archive) — the lists still return archived assets and the client filters them.</summary>
    public required bool IsArchived { get; init; }
}

public static class AssetMappingExtensions
{
    /// <summary>
    /// spec-02: <paramref name="transactionCount"/> is a parameter because the count lives in the
    /// Transactions table, not on the row — read slices project it as a correlated subquery inside
    /// their own query, and a slice that already knows the number passes it directly.
    /// <paramref name="depositMaturityDate"/> and <paramref name="depositSettled"/> likewise come from
    /// the TermDeposits table (term-deposits, term-deposits-settlement), and
    /// <paramref name="savingsInterestDue"/> from the savings-account interest status (savings-interest-settlement).
    /// </summary>
    public static AssetResponse ToResponse(
        this Asset asset,
        int transactionCount,
        DateOnly? depositMaturityDate = null,
        bool? depositSettled = null,
        bool? savingsInterestDue = null) => new()
        {
            Id = asset.Id,
            PortfolioId = asset.PortfolioId,
            AssetClass = asset.AssetClass,
            ValuationMode = asset.ValuationMode,
            Name = asset.Name,
            Currency = asset.Currency,
            Quantity = asset.Quantity,
            ManualValue = asset.ManualValueAmount,
            ManualValueDate = asset.ManualValueDate,
            InstrumentId = asset.InstrumentId,
            TransactionCount = transactionCount,
            DepositMaturityDate = depositMaturityDate,
            DepositSettled = depositSettled,
            SavingsInterestDue = savingsInterestDue,
            IsArchived = asset.IsArchived
        };

    /// <summary>
    /// The body GetAsset returns for <paramref name="asset"/> — transaction count and deposit terms read
    /// beside it — for a slice that holds the tracked entity rather than a projection (asset-archive).
    /// </summary>
    internal static async Task<AssetResponse> ToFullResponseAsync(
        this PortfolioDbContext dbContext, Asset asset, DateOnly today, CancellationToken cancellationToken)
    {
        var transactionCount = await dbContext.Transactions.CountAsync(t => t.AssetId == asset.Id, cancellationToken);
        var terms = await dbContext.TermDeposits
            .AsNoTracking()
            .Where(t => t.AssetId == asset.Id)
            .Select(t => new { t.MaturityDate, Settled = t.SettledOn != null })
            .FirstOrDefaultAsync(cancellationToken);

        var savingsInterestDue = await dbContext.SavingsInterestDueAsync(asset, today, cancellationToken);

        return asset.ToResponse(transactionCount, terms?.MaturityDate, terms?.Settled, savingsInterestDue);
    }

    /// <summary><see cref="AssetResponse.SavingsInterestDue"/> of one asset: <see langword="null"/> unless it is a savings account.</summary>
    internal static async Task<bool?> SavingsInterestDueAsync(
        this PortfolioDbContext dbContext, Asset asset, DateOnly today, CancellationToken cancellationToken)
    {
        if (asset.AssetClass != AssetClass.Savings)
        {
            return null;
        }

        var status = await dbContext.LoadSavingsInterestStatusAsync([asset.Id], today, cancellationToken);
        return status.TryGetValue(asset.Id, out var interest) ? interest.Due is not null : null;
    }
}
