using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class GroupFundingApi
{
    public const string CurrentMonth = "2026-10";

    public static readonly DateOnly EarlierMonthDay = new(2026, 9, 28);

    public static readonly DateOnly FundingDay = new(2026, 10, 14);

    public static readonly DateOnly LaterFundingDay = new(2026, 10, 16);

    public static async Task<Guid> CreateFundingGroupAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name,
        decimal planPln,
        int color = 1,
        Guid[]? contributionAccountIds = null,
        ContributionBody[]? contributions = null) =>
        (await client.CreateAssetGroupAsync(
            cancellationToken,
            name,
            ColorKey(color),
            monthlyContributionPln: planPln,
            contributions: contributions
                ?? contributionAccountIds?.Select(id => new ContributionBody(planPln / contributionAccountIds.Length, id)).ToArray())).Id;

    public static async Task<BankAccount> CreateBankAccountInGroupAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        Guid? groupId,
        string name,
        decimal balance = 0m,
        string currency = "PLN",
        DateOnly? depositedOn = null)
    {
        var account = await client.CreateBankAccountAsync(
            cancellationToken, name, currency: currency, balance: balance, depositedOn: depositedOn ?? EarlierMonthDay);
        if (groupId is not null)
        {
            await client.AssignAssetsAsync(groupId, cancellationToken, account.MoneyAssetId);
        }

        return account;
    }

    public sealed record BrokerageInGroup(Guid AccountId, Guid CashAssetId, Guid EtfAssetId);

    public static async Task<BrokerageInGroup> CreateBrokerageInGroupAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        Guid? cashGroupId,
        Guid? etfGroupId,
        string name = "Maklerskie",
        decimal balance = 0m)
    {
        var holding = await client.CreateBrokerageHoldingAsync(
            cancellationToken, cashBalance: balance, accountName: name, depositedOn: EarlierMonthDay);
        if (cashGroupId is not null)
        {
            await client.AssignAssetsAsync(cashGroupId, cancellationToken, holding.CashAssetId);
        }

        if (etfGroupId is not null)
        {
            await client.AssignAssetsAsync(etfGroupId, cancellationToken, holding.AssetId);
        }

        return new BrokerageInGroup(holding.AccountId, holding.CashAssetId, holding.AssetId);
    }

    public static Task<Guid> DepositMoneyAsync(
        this HttpClient client, Guid moneyAssetId, decimal amount, DateOnly date, CancellationToken cancellationToken) =>
        client.RecordTransactionAsync(moneyAssetId, TransactionType.Deposit, amount, date, cancellationToken, unitPrice: 1m);

    public static Task<Guid> WithdrawMoneyAsync(
        this HttpClient client, Guid moneyAssetId, decimal amount, DateOnly date, CancellationToken cancellationToken) =>
        client.RecordTransactionAsync(moneyAssetId, TransactionType.Withdraw, amount, date, cancellationToken, unitPrice: 1m);

    public static Task<Guid> TransferMoneyAsync(
        this HttpClient client,
        Guid sourceMoneyAssetId,
        Guid targetMoneyAssetId,
        decimal amount,
        DateOnly date,
        CancellationToken cancellationToken) =>
        client.CreateTransferAsync(
            cancellationToken,
            new CreateTransferRequest
            {
                SourceAssetId = sourceMoneyAssetId,
                TargetAssetId = targetMoneyAssetId,
                Amount = amount,
                Date = date
            });

    public static async Task<BankAccount> CreateIncomeAccountAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Pensja",
        decimal balance = 10_000m,
        string currency = "PLN")
    {
        var account = await client.CreateBankAccountInGroupAsync(
            cancellationToken, null, name, balance: balance, currency: currency);
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata", 8_000m, new DateOnly(2026, 10, 1), dayOfMonth: 10);

        return account;
    }

    public static async Task<GroupContributionView> ContributionOfAsync(
        this HttpClient client, Guid groupId, CancellationToken cancellationToken, string month = CurrentMonth)
    {
        var view = await client.GetPlanningMonthAsync(month, cancellationToken);

        return Assert.Single(view.Expenses.GroupContributions!, c => c.GroupId == groupId);
    }
}
