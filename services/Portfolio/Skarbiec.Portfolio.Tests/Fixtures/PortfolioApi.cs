using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of endpoint X calls X directly and asserts on the raw response.
internal static class PortfolioApi
{
    public const string PortfoliosUri = "/api/portfolio/portfolios";

    public static string PortfolioUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}";

    public static string AssetsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/assets";

    public static string AssetUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}";

    public static string ArchiveAssetUri(Guid portfolioId, Guid assetId) =>
        $"{AssetUri(portfolioId, assetId)}/archive";

    public static string RestoreAssetUri(Guid portfolioId, Guid assetId) =>
        $"{AssetUri(portfolioId, assetId)}/restore";

    public static async Task ArchiveAssetAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, assetId), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task RestoreAssetAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(RestoreAssetUri(portfolioId, assetId), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<(Guid PortfolioId, Guid CashId)> AddArchivedCashAssetInLivePortfolioAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal balance = 5_000m,
        string portfolioName = "Wallet",
        string name = "Archived cash")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: balance, name: name);
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);

        return (portfolioId, cashId);
    }

    public static string TransactionsUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions";

    public static string TransactionUri(Guid portfolioId, Guid assetId, Guid transactionId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions/{transactionId}";

    public const string AllDepositsUri = "/api/portfolio/deposits";

    public static string DepositsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/deposits";

    public static string DepositUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/deposits/{assetId}";

    public static string DepositSettlementPreviewUri(Guid portfolioId, Guid assetId) =>
        $"{DepositUri(portfolioId, assetId)}/settlement-preview";

    public static string SettleDepositUri(Guid portfolioId, Guid assetId) =>
        $"{DepositUri(portfolioId, assetId)}/settle";

    public static readonly DateTimeOffset AfterDefaultMaturityUtc = new(2026, 4, 20, 10, 0, 0, TimeSpan.Zero);

    public static SettleDepositRequest NewSettleRequest(
        DateOnly? settledOn = null,
        decimal grossInterest = 147.95m,
        decimal tax = 28.12m,
        Guid? destinationAssetId = null) => new()
        {
            SettledOn = settledOn ?? new DateOnly(2026, 4, 15),
            GrossInterest = grossInterest,
            Tax = tax,
            DestinationAssetId = destinationAssetId
        };

    public static string PayOutDepositUri(Guid portfolioId, Guid assetId) =>
        $"{DepositUri(portfolioId, assetId)}/payout";

    public static PayOutDepositRequest NewPayOutRequest(Guid destinationAssetId, DateOnly? date = null) => new()
    {
        DestinationAssetId = destinationAssetId,
        Date = date ?? new DateOnly(2026, 4, 18)
    };

    public static async Task PayOutDepositAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, Guid destinationAssetId, CancellationToken cancellationToken, DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync(
            PayOutDepositUri(portfolioId, assetId), NewPayOutRequest(destinationAssetId, date), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<DepositSettlementPreviewResponse> GetSettlementPreviewAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(DepositSettlementPreviewUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositSettlementPreviewResponse>(cancellationToken))!;
    }

    public static string RollOverDepositUri(Guid portfolioId, Guid assetId) =>
        $"{DepositUri(portfolioId, assetId)}/rollover";

    public static RollOverDepositRequest NewRollOverRequest(
        decimal annualInterestRatePercent = 5.5m,
        decimal? grossInterest = 147.95m,
        decimal? tax = 28.12m) => new()
        {
            AnnualInterestRatePercent = annualInterestRatePercent,
            GrossInterest = grossInterest,
            Tax = tax
        };

    public static object SettledRollOverBody(decimal annualInterestRatePercent = 5.5m) =>
        new { annualInterestRatePercent };

    public static async Task<DepositResponse> RollOverDepositAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken, object? body = null)
    {
        var response = await client.PostAsJsonAsync(RollOverDepositUri(portfolioId, assetId), body ?? NewRollOverRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return await client.GetDepositAsync(portfolioId, assetId, cancellationToken);
    }

    public sealed record PaidOutDeposit(Guid DepositPortfolioId, DepositResponse Deposit, Guid CashPortfolioId, Guid CashAssetId);

    public static async Task<PaidOutDeposit> CreatePaidOutDepositAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var (depositPortfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var cashPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetAsync(cashPortfolioId, cancellationToken);
        await client.SettleDepositAsync(
            depositPortfolioId, deposit.AssetId, cancellationToken, NewSettleRequest(destinationAssetId: cashId));

        return new PaidOutDeposit(depositPortfolioId, deposit, cashPortfolioId, cashId);
    }

    public sealed record DepositPaidIntoSavings(
        Guid DepositPortfolioId, DepositResponse Deposit, Guid SavingsPortfolioId, SavingsAccountResponse Account);

    public static async Task<DepositPaidIntoSavings> CreateDepositPaidIntoSavingsAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var (depositPortfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var (savingsPortfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(withOpeningDeposit: false), portfolioName: "Wallet");
        await client.SettleDepositAsync(
            depositPortfolioId, deposit.AssetId, cancellationToken, NewSettleRequest(destinationAssetId: account.AssetId));

        return new DepositPaidIntoSavings(depositPortfolioId, deposit, savingsPortfolioId, account);
    }

    public static async Task<(Guid PortfolioId, Guid SavingsId)> AddArchivedSavingsAccountAsync(
        this HttpClient client, CancellationToken cancellationToken, string portfolioName = "Old savings")
    {
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(withOpeningDeposit: false), portfolioName);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        return (portfolioId, account.AssetId);
    }

    public static async Task SettleDepositAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken, SettleDepositRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, assetId), request ?? NewSettleRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<DepositResponse> GetDepositAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(DepositUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken))!;
    }

    public static AddDepositRequest NewDepositRequest(
        string name = "Term deposit",
        string? bankName = "Test bank",
        string currency = "PLN",
        decimal principal = 10_000m,
        DateOnly? startDate = null,
        int termLength = 3,
        DepositTermUnit termUnit = DepositTermUnit.Months,
        decimal annualInterestRatePercent = 6m,
        DepositCapitalization capitalization = DepositCapitalization.AtMaturity,
        bool taxExempt = false,
        decimal earlyBreakInterestLossPercent = 100m,
        Guid? fundingAssetId = null) => new()
        {
            Name = name,
            BankName = bankName,
            Currency = currency,
            Principal = principal,
            StartDate = startDate ?? new DateOnly(2026, 1, 15),
            TermLength = termLength,
            TermUnit = termUnit,
            AnnualInterestRatePercent = annualInterestRatePercent,
            Capitalization = capitalization,
            TaxExempt = taxExempt,
            EarlyBreakInterestLossPercent = earlyBreakInterestLossPercent,
            FundingAssetId = fundingAssetId
        };

    public static UpdateDepositRequest ToUpdateRequest(this AddDepositRequest request) => new()
    {
        Name = request.Name,
        BankName = request.BankName,
        Principal = request.Principal,
        StartDate = request.StartDate,
        TermLength = request.TermLength,
        TermUnit = request.TermUnit,
        AnnualInterestRatePercent = request.AnnualInterestRatePercent,
        Capitalization = request.Capitalization,
        TaxExempt = request.TaxExempt,
        EarlyBreakInterestLossPercent = request.EarlyBreakInterestLossPercent
    };

    public static async Task<DepositResponse> AddDepositAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken, AddDepositRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(DepositsUri(portfolioId), request ?? NewDepositRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken))!;
    }

    public static async Task<(Guid PortfolioId, DepositResponse Deposit)> CreatePortfolioWithDepositAsync(
        this HttpClient client, CancellationToken cancellationToken, AddDepositRequest? request = null, string portfolioName = "Savings")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var deposit = await client.AddDepositAsync(portfolioId, cancellationToken, request);

        return (portfolioId, deposit);
    }

    public const string AllSavingsAccountsUri = "/api/portfolio/savings-accounts";

    public static string SavingsAccountsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/savings-accounts";

    public static string SavingsAccountUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/savings-accounts/{assetId}";

    public static readonly DateTimeOffset SavingsTodayUtc = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly SavingsToday = new(2026, 2, 1);

    public static AddSavingsAccountRequest NewSavingsAccountRequest(
        string name = "Savings account",
        string? bankName = "Test bank",
        string currency = "PLN",
        decimal annualInterestRatePercent = 5.25m,
        bool taxExempt = false,
        bool withOpeningDeposit = true,
        decimal openingAmount = 10_000m,
        DateOnly? openingDate = null) => new()
        {
            Name = name,
            BankName = bankName,
            Currency = currency,
            AnnualInterestRatePercent = annualInterestRatePercent,
            TaxExempt = taxExempt,
            OpeningDeposit = withOpeningDeposit
                ? new OpeningDepositRequest { Amount = openingAmount, Date = openingDate ?? SavingsToday }
                : null
        };

    public static UpdateSavingsAccountRequest NewUpdateSavingsAccountRequest(
        string name = "Renamed savings",
        string? bankName = "Other bank",
        decimal annualInterestRatePercent = 3m,
        bool taxExempt = true) => new()
        {
            Name = name,
            BankName = bankName,
            AnnualInterestRatePercent = annualInterestRatePercent,
            TaxExempt = taxExempt
        };

    public static async Task<SavingsAccountResponse> AddSavingsAccountAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken, AddSavingsAccountRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(SavingsAccountsUri(portfolioId), request ?? NewSavingsAccountRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SavingsAccountResponse>(cancellationToken))!;
    }

    public static async Task<(Guid PortfolioId, SavingsAccountResponse Account)> CreatePortfolioWithSavingsAccountAsync(
        this HttpClient client, CancellationToken cancellationToken, AddSavingsAccountRequest? request = null, string portfolioName = "Savings")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var account = await client.AddSavingsAccountAsync(portfolioId, cancellationToken, request);

        return (portfolioId, account);
    }

    public static async Task<SavingsAccountResponse> GetSavingsAccountAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(SavingsAccountUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SavingsAccountResponse>(cancellationToken))!;
    }

    public static string TransferCandidatesUri(string? currency = "PLN", string? assetClass = "Cash")
    {
        var query = new List<string>();
        if (currency is not null)
        {
            query.Add($"currency={Uri.EscapeDataString(currency)}");
        }

        if (assetClass is not null)
        {
            query.Add($"assetClass={Uri.EscapeDataString(assetClass)}");
        }

        const string path = "/api/portfolio/transfer-candidates";
        return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }

    public static readonly DateOnly DefaultTopUpDate = new(2026, 1, 1);

    public static async Task<Guid> AddCashAssetWithBalanceAsync(
        this HttpClient client,
        Guid portfolioId,
        CancellationToken cancellationToken,
        decimal balance = 5_000m,
        DateOnly? toppedUpOn = null,
        string currency = "PLN",
        string name = "Cash account")
    {
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken, currency: currency, name: name);
        await client.RecordTransactionAsync(
            portfolioId, cashId, TransactionType.Deposit, balance, toppedUpOn ?? DefaultTopUpDate, cancellationToken, unitPrice: 1m);

        return cashId;
    }

    public static async Task<(Guid PortfolioId, Guid CashId)> AddArchivedCashAssetAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal balance = 5_000m,
        string portfolioName = "Old wallet",
        string name = "Archived cash")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: balance, name: name);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        return (portfolioId, cashId);
    }

    public sealed record FundedDeposit(Guid CashPortfolioId, Guid CashAssetId, Guid DepositPortfolioId, DepositResponse Deposit);

    public static async Task<FundedDeposit> CreateFundedDepositAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 5_000m,
        decimal principal = 1_000m)
    {
        var cashPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(cashPortfolioId, cancellationToken, balance: cashBalance);
        var depositPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Savings");
        var deposit = await client.AddDepositAsync(
            depositPortfolioId, cancellationToken, NewDepositRequest(principal: principal, fundingAssetId: cashId));

        return new FundedDeposit(cashPortfolioId, cashId, depositPortfolioId, deposit);
    }

    public static async Task<TransactionResponse> GetCashWithdrawAsync(
        this HttpClient client, Guid portfolioId, Guid cashId, CancellationToken cancellationToken) =>
        Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

    public static async Task<Guid> CreatePortfolioAsync(
        this HttpClient client, CancellationToken cancellationToken, string name = "Retirement")
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken))!.Id;
    }

    public static async Task ArchivePortfolioAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> AddAssetAsync(
        this HttpClient client,
        Guid portfolioId,
        CancellationToken cancellationToken,
        string name = "Test asset",
        AssetClass assetClass = AssetClass.Stock,
        decimal manualValue = 0m,
        string currency = "PLN")
    {
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = name,
            Currency = currency,
            ManualValue = manualValue,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!.Id;
    }

    public static async Task<Guid> AddCashAssetAsync(
        this HttpClient client,
        Guid portfolioId,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Cash,
        string currency = "PLN",
        string name = "Cash account")
    {
        var request = new AddAssetRequest { AssetClass = assetClass, Name = name, Currency = currency };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!.Id;
    }

    public static async Task<(Guid PortfolioId, Guid AssetId)> CreatePortfolioWithAssetAsync(
        this HttpClient client, CancellationToken cancellationToken, string currency = "PLN")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, currency: currency);

        return (portfolioId, assetId);
    }

    public static async Task<(Guid PortfolioId, IReadOnlyList<Guid> AssetIds)> CreatePortfolioWithAssetsAndTransactionsAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        int assetCount = 2,
        int transactionsPerAsset = 2,
        string portfolioName = "Retirement")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var assetIds = new List<Guid>();

        for (var i = 0; i < assetCount; i++)
        {
            var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: $"Asset {i + 1}");
            for (var t = 0; t < transactionsPerAsset; t++)
            {
                await client.RecordTransactionAsync(
                    portfolioId, assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 1).AddDays(t), cancellationToken);
            }

            assetIds.Add(assetId);
        }

        return (portfolioId, assetIds);
    }

    public static async Task<Guid> RecordTransactionAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        TransactionType type,
        decimal quantity,
        DateOnly date,
        CancellationToken cancellationToken,
        decimal unitPrice = 10m)
    {
        var request = new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = unitPrice, Date = date };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!.Id;
    }

    public static async Task<AssetResponse> GetAssetAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
    }

    public static async Task<PagedResponse<TransactionResponse>> ListTransactionsAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(TransactionsUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!;
    }

    public static string SavingsInterestPreviewUri(Guid portfolioId, Guid assetId) =>
        $"{SavingsAccountUri(portfolioId, assetId)}/interest-preview";

    public static string SavingsInterestSettlementsUri(Guid portfolioId, Guid assetId) =>
        $"{SavingsAccountUri(portfolioId, assetId)}/interest-settlements";

    public static string SavingsInterestSettlementUri(Guid portfolioId, Guid assetId, Guid settlementId) =>
        $"{SavingsInterestSettlementsUri(portfolioId, assetId)}/{settlementId}";

    public static readonly DateOnly InterestOpeningDate = new(2026, 9, 1);

    public static readonly DateOnly SeptemberEnd = new(2026, 9, 30);

    public static readonly DateOnly OctoberEnd = new(2026, 10, 31);

    public static readonly DateTimeOffset SeptemberLastDayUtc = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset SeptemberEndedUtc = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset OctoberEndedUtc = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset NovemberEndedUtc = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    public static AddSavingsAccountRequest NewInterestAccountRequest(
        string name = "Interest account", bool taxExempt = false, decimal annualInterestRatePercent = 5m, DateOnly? openingDate = null) =>
        NewSavingsAccountRequest(
            name: name,
            annualInterestRatePercent: annualInterestRatePercent,
            taxExempt: taxExempt,
            openingDate: openingDate ?? InterestOpeningDate);

    public static object NewSettleInterestBody(DateOnly periodEnd, decimal grossInterest, decimal tax) =>
        new { periodEnd, grossInterest, tax };

    public static async Task<System.Text.Json.JsonElement> GetSavingsInterestPreviewAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(SavingsInterestPreviewUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.ReadJsonAsync(cancellationToken);
    }

    public static async Task SettleSavingsInterestAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        DateOnly periodEnd,
        decimal grossInterest,
        decimal tax,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            SavingsInterestSettlementsUri(portfolioId, assetId), NewSettleInterestBody(periodEnd, grossInterest, tax), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<DateOnly> SettlePreviewedSavingsInterestAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var preview = await client.GetSavingsInterestPreviewAsync(portfolioId, assetId, cancellationToken);
        var periodEnd = DateOnly.Parse(preview.GetProperty("periodEnd").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        await client.SettleSavingsInterestAsync(
            portfolioId,
            assetId,
            periodEnd,
            preview.GetProperty("grossInterest").GetDecimal(),
            preview.GetProperty("tax").GetDecimal(),
            cancellationToken);

        return periodEnd;
    }

    public static async Task<Guid> GetLastSettlementIdAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var account = await client.GetSavingsAccountAsync(portfolioId, assetId, cancellationToken);

        return account.LastSettlement!.SettlementId;
    }

    public const string TransfersUri = "/api/portfolio/transfers";

    public static string TransferUri(Guid transferId) => $"{TransfersUri}/{transferId}";

    public static readonly DateOnly DefaultTransferDate = new(2026, 1, 20);

    public static CreateTransferRequest NewTransferRequest(
        Guid sourceAssetId, Guid targetAssetId, decimal amount = 2_000m, DateOnly? date = null) => new()
        {
            SourceAssetId = sourceAssetId,
            TargetAssetId = targetAssetId,
            Amount = amount,
            Date = date ?? DefaultTransferDate
        };

    public sealed record CashAndSavings(Guid CashPortfolioId, Guid CashAssetId, Guid SavingsPortfolioId, Guid SavingsAssetId);

    public static async Task<CashAndSavings> CreateCashAndSavingsAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 5_000m,
        DateOnly? toppedUpOn = null,
        AddSavingsAccountRequest? savingsRequest = null)
    {
        var cashPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(cashPortfolioId, cancellationToken, balance: cashBalance, toppedUpOn: toppedUpOn);
        var (savingsPortfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, savingsRequest ?? NewSavingsAccountRequest(withOpeningDeposit: false));

        return new CashAndSavings(cashPortfolioId, cashId, savingsPortfolioId, account.AssetId);
    }

    public static async Task<Guid> CreateTransferAsync(
        this HttpClient client, CancellationToken cancellationToken, CreateTransferRequest request)
    {
        var response = await client.PostAsJsonAsync(TransfersUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken))!.TransferId;
    }
}
