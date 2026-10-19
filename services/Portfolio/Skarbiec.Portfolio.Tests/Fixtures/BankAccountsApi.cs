using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only, apart from the raw-body builders: the Bank fields travel as JSON so a test states the wire shape.
internal static class BankAccountsApi
{
    public const string CurrentVariant = "Current";

    public const string SavingsVariant = "Savings";

    public const string InterestNotSupportedErrorCode = "Validation.InterestNotSupported";

    public const string BankFieldsNotAllowedErrorCode = "Validation.BankFieldsNotAllowed";

    public const string WrapperNotAllowedErrorCode = "Validation.WrapperNotAllowed";

    public const string TransferCurrencyMismatchErrorCode = "Validation.TransferCurrencyMismatch";

    public static readonly DateOnly AugustOpening = new(2026, 8, 1);

    public static readonly DateOnly AugustEnd = new(2026, 8, 31);

    public static readonly DateTimeOffset BankInterestSeptemberEndedUtc = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly InterestOpeningDate = new(2026, 9, 1);

    public static readonly DateOnly SeptemberEnd = new(2026, 9, 30);

    public static readonly DateOnly OctoberEnd = new(2026, 10, 31);

    public static readonly DateTimeOffset SeptemberLastDayUtc = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset SeptemberEndedUtc = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset OctoberEndedUtc = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset NovemberEndedUtc = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    public static string InterestPreviewUri(Guid accountId) => $"{AccountUri(accountId)}/interest-preview";

    public static string InterestSettlementsUri(Guid accountId) => $"{AccountUri(accountId)}/interest-settlements";

    public static string InterestSettlementUri(Guid accountId, Guid settlementId) =>
        $"{InterestSettlementsUri(accountId)}/{settlementId}";

    public static string AccountsOfTypeUri(AccountType type) => $"{AccountsUri}?type={type}";

    public static object NewBankAccountBody(
        string name = "mBank",
        string variant = CurrentVariant,
        string currency = "PLN",
        string? bankName = "mBank",
        decimal annualInterestRatePercent = 0m,
        TaxWrapper wrapper = TaxWrapper.Regular,
        Guid? groupId = null) => new
        {
            name,
            type = (int)AccountType.Bank,
            wrapper = (int)wrapper,
            currency,
            variant = VariantValue(variant),
            bankName,
            annualInterestRatePercent,
            groupId
        };

    public static object NewUpdateBankBody(
        string name = "mBank",
        string variant = SavingsVariant,
        string? bankName = "Nest Bank",
        decimal annualInterestRatePercent = 4m,
        Guid? groupId = null) => new
        {
            name,
            variant = VariantValue(variant),
            bankName,
            annualInterestRatePercent,
            groupId
        };

    public static object NewSettleInterestBody(DateOnly periodEnd, decimal grossInterest, decimal tax) =>
        new { periodEnd, grossInterest, tax };

    public sealed record BankAccount(Guid AccountId, Guid MoneyAssetId);

    public static async Task<BankAccount> CreateBankAccountAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "mBank",
        string variant = CurrentVariant,
        string currency = "PLN",
        decimal annualInterestRatePercent = 0m,
        decimal balance = 0m,
        DateOnly? depositedOn = null)
    {
        var response = await client.PostAsJsonAsync(
            AccountsUri,
            NewBankAccountBody(name, variant, currency, name, annualInterestRatePercent),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.ReadJsonAsync(cancellationToken);
        var accountId = created.GetProperty("id").GetGuid();
        var moneyAssetId = created.GetProperty("cashAssetId").GetGuid();
        if (balance > 0m)
        {
            await client.RecordTransactionAsync(
                moneyAssetId, TransactionType.Deposit, balance, depositedOn ?? CashDay1, cancellationToken, unitPrice: 1m);
        }

        return new BankAccount(accountId, moneyAssetId);
    }

    // A savings account holding 10 000 from 1 September 2026, so September is the first month to settle.
    public static Task<BankAccount> CreateInterestAccountAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Interest account",
        decimal annualInterestRatePercent = 5m,
        string currency = "PLN",
        decimal balance = 10_000m) =>
        client.CreateBankAccountAsync(
            cancellationToken, name, SavingsVariant, currency, annualInterestRatePercent, balance, InterestOpeningDate);

    // A savings account holding 10 000 from the fixed "today" of the transaction tests.
    public static Task<BankAccount> CreateSavingsBankAccountAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Savings account",
        decimal annualInterestRatePercent = 5.25m,
        decimal balance = 10_000m) =>
        client.CreateBankAccountAsync(
            cancellationToken, name, SavingsVariant, "PLN", annualInterestRatePercent, balance, FixedToday);

    public sealed record CurrentAndSavings(BankAccount Current, BankAccount Savings);

    // A funded current account and an empty savings account, both PLN, for the transfer tests.
    public static async Task<CurrentAndSavings> CreateCurrentAndSavingsAsync(
        this HttpClient client, CancellationToken cancellationToken, decimal currentBalance = 5_000m, DateOnly? toppedUpOn = null)
    {
        var current = await client.CreateBankAccountAsync(
            cancellationToken, "Current account", balance: currentBalance, depositedOn: toppedUpOn ?? DefaultTopUpDate);
        var savings = await client.CreateBankAccountAsync(
            cancellationToken, "Savings account", SavingsVariant, annualInterestRatePercent: 5.25m);

        return new CurrentAndSavings(current, savings);
    }

    public sealed record DepositPaidIntoBank(DepositResponse Deposit, BankAccount Account);

    public static async Task<DepositPaidIntoBank> CreateDepositPaidIntoBankAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var account = await client.CreateBankAccountAsync(cancellationToken, "Savings account", SavingsVariant, annualInterestRatePercent: 5.25m);
        var deposit = await client.AddDepositAsync(cancellationToken, NewDepositRequest(returnAccountId: account.AccountId));
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken, NewSettleRequest(payOut: true));

        return new DepositPaidIntoBank(deposit, account);
    }

    public static async Task<Guid> CreateArchivedBankAccountAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var account = await client.CreateBankAccountAsync(cancellationToken, "Old account");
        await client.ArchiveAccountAsync(account.AccountId, cancellationToken);

        return account.MoneyAssetId;
    }

    public static async Task SettleInterestAsync(
        this HttpClient client,
        Guid accountId,
        DateOnly periodEnd,
        decimal grossInterest,
        decimal tax,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(accountId), NewSettleInterestBody(periodEnd, grossInterest, tax), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> GetLastSettlementIdAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken) =>
        (await client.GetAccountJsonAsync(accountId, cancellationToken))
            .Bank().GetProperty("lastSettlement").GetProperty("settlementId").GetGuid();

    public static async Task<JsonElement> GetAccountJsonAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AccountUri(accountId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.ReadJsonAsync(cancellationToken);
    }

    public static async Task<JsonElement> GetInterestPreviewAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(InterestPreviewUri(accountId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.ReadJsonAsync(cancellationToken);
    }

    public static async Task<DateOnly> SettlePreviewedInterestAsync(
        this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        var preview = await client.GetInterestPreviewAsync(accountId, cancellationToken);
        var periodEnd = DateOnly.Parse(preview.GetProperty("periodEnd").GetString()!, CultureInfo.InvariantCulture);
        var response = await client.PostAsJsonAsync(
            InterestSettlementsUri(accountId),
            NewSettleInterestBody(periodEnd, preview.GetProperty("grossInterest").GetDecimal(), preview.GetProperty("tax").GetDecimal()),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return periodEnd;
    }

    public static JsonElement Bank(this JsonElement account) => account.GetProperty("bank");

    public static string VariantName(this JsonElement bank)
    {
        var variant = bank.GetProperty("variant");

        return variant.ValueKind == JsonValueKind.Number
            ? (variant.GetInt32() == 0 ? CurrentVariant : SavingsVariant)
            : variant.GetString()!;
    }

    private static int VariantValue(string variant) => variant == CurrentVariant ? 0 : 1;
}
