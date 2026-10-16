using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SettleOccurrence;

public sealed class SettleOccurrenceHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(
        Guid id, string month, SettleOccurrenceRequest request, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var nominal = parsed.Value;

        var income = await dbContext.RecurringIncomes
            .Include(i => i.Versions)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(request.AccountId);
        }

        var today = WarsawCalendar.Today(timeProvider);
        if (request.ActualDate > today)
        {
            return OccurrenceErrors.FutureDate(today);
        }

        var stored = await dbContext.RecurringIncomeOccurrences
            .FirstOrDefaultAsync(o => o.RecurringIncomeId == id && o.NominalMonth == nominal.FirstDay, cancellationToken);
        var occurrence = stored ?? income.Freeze(nominal, OccurrenceStatus.Settled);
        if (occurrence is null)
        {
            return OccurrenceErrors.NotFound(id, month);
        }

        var plannedCurrency = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == occurrence.PlannedAccountId)
            .Select(a => a.Currency)
            .FirstAsync(cancellationToken);
        if (account.Currency != plannedCurrency)
        {
            return OccurrenceErrors.CurrencyMismatch(plannedCurrency, account.Currency);
        }

        if (account.IsArchived && account.Id != stored?.AccountId)
        {
            return AccountErrors.Archived(account.Id);
        }

        var rate = await fxRateLookupClient.ResolveFxRateToPlnAsync(plannedCurrency, request.ActualDate, cancellationToken);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        if (rate.Value is not { } fxRateToPln)
        {
            return TransactionErrors.FxRateMissing(plannedCurrency, request.ActualDate);
        }

        var synced = await SyncDepositAsync(occurrence, account.Id, request, fxRateToPln, cancellationToken);
        if (synced.IsFailure)
        {
            return synced.Error;
        }

        occurrence.Status = OccurrenceStatus.Settled;
        occurrence.ActualAmount = request.ActualAmount;
        occurrence.ActualDate = request.ActualDate;
        occurrence.AccountId = account.Id;
        occurrence.FxRateToPln = fxRateToPln;

        if (stored is null)
        {
            dbContext.RecurringIncomeOccurrences.Add(occurrence);
        }

        foreach (var money in synced.Value)
        {
            await positionEventPublisher.PublishChangedAsync(money, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }

    // Stages the deposit's create, update, move or removal and returns the money assets whose position changed.
    private async Task<Result<List<Asset>>> SyncDepositAsync(
        RecurringIncomeOccurrence occurrence,
        Guid accountId,
        SettleOccurrenceRequest request,
        decimal fxRateToPln,
        CancellationToken cancellationToken)
    {
        var existing = occurrence.TransactionId is { } existingId
            ? await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == existingId, cancellationToken)
            : null;
        var oldMoney = existing is null
            ? null
            : await dbContext.Assets.FirstAsync(a => a.Id == existing.AssetId, cancellationToken);
        var newMoney = request.CreateDeposit ? await dbContext.LoadAccountCashAsync(accountId, cancellationToken) : null;

        List<Asset> touched = [];
        if (oldMoney is null && newMoney is null)
        {
            return touched;
        }

        foreach (var money in new[] { oldMoney, newMoney })
        {
            if (money is not null && await dbContext.ReadOnlyErrorAsync(money, cancellationToken) is { } readOnly)
            {
                return readOnly;
            }
        }

        var candidate = newMoney is null
            ? null
            : new Transaction
            {
                Id = existing is not null && existing.AssetId == newMoney.Id ? existing.Id : Guid.NewGuid(),
                AssetId = newMoney.Id,
                Type = TransactionType.Deposit,
                Quantity = request.ActualAmount,
                UnitPriceAmount = 1m,
                FxRateToPln = fxRateToPln,
                Date = request.ActualDate
            };

        foreach (var money in new[] { oldMoney, newMoney }.OfType<Asset>().DistinctBy(a => a.Id))
        {
            var others = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == money.Id && (existing == null || t.Id != existing.Id))
                .ToListAsync(cancellationToken);
            if (candidate?.AssetId == money.Id)
            {
                others.Add(candidate);
            }

            var recomputed = TransactionQuantityCalculator.Recompute(others, money.OversellError(TransactionErrors.MutationBreaksHistory));
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            money.Quantity = recomputed.Value;
            touched.Add(money);
        }

        if (existing is not null && candidate?.Id == existing.Id)
        {
            existing.Quantity = candidate.Quantity;
            existing.Date = candidate.Date;
            existing.FxRateToPln = candidate.FxRateToPln;
        }
        else
        {
            if (existing is not null)
            {
                dbContext.Transactions.Remove(existing);
            }

            if (candidate is not null)
            {
                dbContext.Transactions.Add(candidate);
            }
        }

        occurrence.TransactionId = candidate?.Id;

        return touched;
    }
}
