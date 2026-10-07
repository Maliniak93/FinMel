using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.UpdateBond;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.GetSettlementPreview;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Features.Metals.AddMetal;
using Skarbiec.Portfolio.Features.Metals.UpdateMetal;
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

    public const string AllBondsUri = "/api/portfolio/bonds";

    public static string BondsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/bonds";

    public static string BondUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/bonds/{assetId}";

    public static readonly DateOnly DefaultBondPurchaseDate = new(2026, 10, 1);

    public static readonly DateTimeOffset BondPurchaseDayUtc = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    public static AddBondRequest NewBondRequest(
        string name = "EDO1036",
        string seriesCode = "EDO1036",
        TreasuryBondType type = TreasuryBondType.Edo,
        DateOnly? purchaseDate = null,
        int bondCount = 50,
        decimal purchasePricePerBond = 100m,
        decimal firstPeriodRatePercent = 5.35m,
        decimal? marginPercent = 2.00m,
        decimal earlyRedemptionFeePerBond = 3.00m,
        bool taxExempt = false,
        Guid? fundingAssetId = null) => new()
        {
            Name = name,
            SeriesCode = seriesCode,
            Type = type,
            PurchaseDate = purchaseDate ?? DefaultBondPurchaseDate,
            BondCount = bondCount,
            PurchasePricePerBond = purchasePricePerBond,
            FirstPeriodRatePercent = firstPeriodRatePercent,
            MarginPercent = marginPercent,
            EarlyRedemptionFeePerBond = earlyRedemptionFeePerBond,
            TaxExempt = taxExempt,
            FundingAssetId = fundingAssetId
        };

    public static UpdateBondRequest ToUpdateRequest(this AddBondRequest request) => new()
    {
        Name = request.Name,
        SeriesCode = request.SeriesCode,
        Type = request.Type,
        PurchaseDate = request.PurchaseDate,
        BondCount = request.BondCount,
        PurchasePricePerBond = request.PurchasePricePerBond,
        FirstPeriodRatePercent = request.FirstPeriodRatePercent,
        MarginPercent = request.MarginPercent,
        EarlyRedemptionFeePerBond = request.EarlyRedemptionFeePerBond,
        TaxExempt = request.TaxExempt
    };

    public static async Task<BondResponse> AddBondAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken, AddBondRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(BondsUri(portfolioId), request ?? NewBondRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken))!;
    }

    public static async Task<(Guid PortfolioId, BondResponse Bond)> CreatePortfolioWithBondAsync(
        this HttpClient client, CancellationToken cancellationToken, AddBondRequest? request = null, string portfolioName = "Bonds")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var bond = await client.AddBondAsync(portfolioId, cancellationToken, request);

        return (portfolioId, bond);
    }

    public static async Task<BondResponse> GetBondAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(BondUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken))!;
    }

    public sealed record FundedBond(Guid CashPortfolioId, Guid CashAssetId, Guid BondPortfolioId, BondResponse Bond);

    public static async Task<FundedBond> CreateFundedBondAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 6_000m,
        AddBondRequest? request = null,
        DateOnly? cashToppedUpOn = null)
    {
        var cashPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(
            cashPortfolioId, cancellationToken, balance: cashBalance, toppedUpOn: cashToppedUpOn);
        var bondPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Bonds");
        var bond = await client.AddBondAsync(
            bondPortfolioId, cancellationToken, (request ?? NewBondRequest()) with { FundingAssetId = cashId });

        return new FundedBond(cashPortfolioId, cashId, bondPortfolioId, bond);
    }

    public static string BondInterestSettlementsUri(Guid portfolioId, Guid assetId) =>
        $"{BondUri(portfolioId, assetId)}/interest-settlements";

    public static string BondInterestPreviewUri(Guid portfolioId, Guid assetId) =>
        $"{BondInterestSettlementsUri(portfolioId, assetId)}/preview";

    public static string BondInterestSettlementUri(Guid portfolioId, Guid assetId, Guid settlementId) =>
        $"{BondInterestSettlementsUri(portfolioId, assetId)}/{settlementId}";

    public static readonly DateOnly RorPurchaseDate = new(2026, 6, 10);

    public static AddBondRequest NewRorBondRequest(
        DateOnly? purchaseDate = null, int bondCount = 50, decimal firstPeriodRatePercent = 4.00m, bool taxExempt = false) =>
        NewBondRequest(
            name: "ROR0627",
            seriesCode: "ROR0627",
            type: TreasuryBondType.Ror,
            purchaseDate: purchaseDate ?? RorPurchaseDate,
            bondCount: bondCount,
            firstPeriodRatePercent: firstPeriodRatePercent,
            marginPercent: null,
            taxExempt: taxExempt);

    public static object NewSettleBondBody(
        IEnumerable<(int PeriodIndex, decimal? RatePercent)> periods, Guid? destinationAssetId = null) => new
        {
            periods = periods.Select(p => new { periodIndex = p.PeriodIndex, ratePercent = p.RatePercent }).ToList(),
            destinationAssetId
        };

    public static async Task<HttpResponseMessage> SettleBondInterestRawAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        IEnumerable<(int PeriodIndex, decimal? RatePercent)> periods,
        Guid? destinationAssetId,
        CancellationToken cancellationToken) =>
        await client.PostAsJsonAsync(
            BondInterestSettlementsUri(portfolioId, assetId), NewSettleBondBody(periods, destinationAssetId), cancellationToken);

    public static async Task SettleBondInterestAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        IEnumerable<(int PeriodIndex, decimal? RatePercent)> periods,
        Guid? destinationAssetId,
        CancellationToken cancellationToken)
    {
        var response = await client.SettleBondInterestRawAsync(portfolioId, assetId, periods, destinationAssetId, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> GetLastBondSettlementIdAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var bond = await client.GetBondAsync(portfolioId, assetId, cancellationToken);

        return bond.LastSettlement!.SettlementId;
    }

    public static string BondRedemptionUri(Guid portfolioId, Guid assetId) =>
        $"{BondUri(portfolioId, assetId)}/redemption";

    public static string BondRedemptionPreviewUri(Guid portfolioId, Guid assetId) =>
        $"{BondUri(portfolioId, assetId)}/redemption-preview";

    public static string BondSwapUri(Guid portfolioId, Guid assetId) =>
        $"{BondUri(portfolioId, assetId)}/swap";

    public static readonly DateOnly TosMaturityDate = new(2029, 10, 1);

    public static readonly DateTimeOffset AfterTosMaturityUtc = new(2029, 10, 2, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly RorMaturityDate = new(2027, 6, 10);

    public static readonly DateTimeOffset AfterRorMaturityUtc = new(2027, 6, 11, 10, 0, 0, TimeSpan.Zero);

    public static AddBondRequest NewTosBondRequest(int bondCount = 10, decimal purchasePricePerBond = 100m, bool taxExempt = false) =>
        NewBondRequest(
            name: "TOS1029",
            seriesCode: "TOS1029",
            type: TreasuryBondType.Tos,
            bondCount: bondCount,
            purchasePricePerBond: purchasePricePerBond,
            firstPeriodRatePercent: 4.40m,
            marginPercent: null,
            taxExempt: taxExempt);

    public static async Task<FundedBond> CreateSettledTosAsync(
        this HttpClient client, CancellationToken cancellationToken, bool taxExempt = false)
    {
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewTosBondRequest(taxExempt: taxExempt));
        await client.SettleBondInterestAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, [(1, null), (2, null), (3, null)], null, cancellationToken);

        return funded;
    }

    public static async Task<FundedBond> CreateSettledRorAsync(
        this HttpClient client, CancellationToken cancellationToken, decimal purchasePricePerBond = 99.90m, bool taxExempt = false)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken,
            request: NewRorBondRequest(bondCount: 10, taxExempt: taxExempt) with { PurchasePricePerBond = purchasePricePerBond });
        var periods = Enumerable.Range(1, 12).Select(index => (index, index == 1 ? (decimal?)null : 3.75m)).ToList();
        await client.SettleBondInterestAsync(funded.BondPortfolioId, funded.Bond.AssetId, periods, funded.CashAssetId, cancellationToken);

        return funded;
    }

    public static async Task<HttpResponseMessage> RedeemBondRawAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, Guid destinationAssetId, CancellationToken cancellationToken) =>
        await client.PostAsJsonAsync(BondRedemptionUri(portfolioId, assetId), new { destinationAssetId }, cancellationToken);

    public static async Task RedeemBondAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, Guid destinationAssetId, CancellationToken cancellationToken)
    {
        var response = await client.RedeemBondRawAsync(portfolioId, assetId, destinationAssetId, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static readonly DateOnly EdoEarlyPurchaseDate = new(2025, 3, 1);

    public static readonly DateOnly EdoEarlyRedemptionDate = new(2026, 5, 13);

    public static readonly DateTimeOffset EdoEarlyRedemptionDayUtc = new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly RorEarlyRedemptionDate = new(2026, 9, 25);

    public static readonly DateTimeOffset RorEarlyRedemptionDayUtc = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    public static string BondEarlyRedemptionUri(Guid portfolioId, Guid assetId) =>
        $"{BondUri(portfolioId, assetId)}/early-redemption";

    public static string BondEarlyRedemptionPreviewUri(
        Guid portfolioId, Guid assetId, DateOnly date, int bondCount, decimal? runningPeriodRatePercent = null) =>
        $"{BondUri(portfolioId, assetId)}/early-redemption-preview?date={date:yyyy-MM-dd}&bondCount={bondCount}"
        + (runningPeriodRatePercent is { } rate ? $"&runningPeriodRatePercent={rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty);

    public static AddBondRequest NewEdoEarlyBondRequest(int bondCount = 10) =>
        NewBondRequest(purchaseDate: EdoEarlyPurchaseDate, bondCount: bondCount, earlyRedemptionFeePerBond: 3.00m);

    public static object NewEarlyRedeemBody(
        DateOnly date, int bondCount, Guid destinationAssetId, decimal? runningPeriodRatePercent = null) => new
        {
            date,
            bondCount,
            runningPeriodRatePercent,
            destinationAssetId
        };

    public static async Task<HttpResponseMessage> RedeemBondEarlyRawAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        DateOnly date,
        int bondCount,
        Guid destinationAssetId,
        CancellationToken cancellationToken,
        decimal? runningPeriodRatePercent = null) =>
        await client.PostAsJsonAsync(
            BondEarlyRedemptionUri(portfolioId, assetId),
            NewEarlyRedeemBody(date, bondCount, destinationAssetId, runningPeriodRatePercent),
            cancellationToken);

    public static async Task RedeemBondEarlyAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        DateOnly date,
        int bondCount,
        Guid destinationAssetId,
        CancellationToken cancellationToken,
        decimal? runningPeriodRatePercent = null)
    {
        var response = await client.RedeemBondEarlyRawAsync(
            portfolioId, assetId, date, bondCount, destinationAssetId, cancellationToken, runningPeriodRatePercent);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<FundedBond> CreateEdoYearOneSettledAsync(
        this HttpClient client, CancellationToken cancellationToken, bool settleYearOne = true)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: NewEdoEarlyBondRequest(), cashToppedUpOn: new DateOnly(2025, 1, 1));
        if (settleYearOne)
        {
            await client.SettleBondInterestAsync(funded.BondPortfolioId, funded.Bond.AssetId, [(1, null)], null, cancellationToken);
        }

        return funded;
    }

    public static async Task<FundedBond> CreateRorWithThreeMonthsSettledAsync(
        this HttpClient client, CancellationToken cancellationToken, int bondCount = 20)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: NewRorBondRequest(bondCount: bondCount) with { EarlyRedemptionFeePerBond = 0.50m });
        await client.SettleBondInterestAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, [(1, null), (2, 3.75m), (3, 3.75m)], funded.CashAssetId, cancellationToken);

        return funded;
    }

    public static object NewSwapBody(
        int bondCount,
        Guid? destinationAssetId,
        decimal swapPricePerBond = 99.90m,
        string name = "EDO1036",
        string seriesCode = "EDO1036",
        TreasuryBondType type = TreasuryBondType.Edo) => new
        {
            bondCount,
            newBond = new
            {
                name,
                seriesCode,
                type,
                swapPricePerBond,
                firstPeriodRatePercent = 5.35m,
                marginPercent = (decimal?)2.00m,
                earlyRedemptionFeePerBond = 3.00m
            },
            destinationAssetId
        };

    public static async Task<HttpResponseMessage> SwapBondRawAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        object body,
        CancellationToken cancellationToken) =>
        await client.PostAsJsonAsync(BondSwapUri(portfolioId, assetId), body, cancellationToken);

    public static async Task SwapBondAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        int bondCount,
        Guid? destinationAssetId,
        CancellationToken cancellationToken)
    {
        var response = await client.SwapBondRawAsync(portfolioId, assetId, NewSwapBody(bondCount, destinationAssetId), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<BondResponse> FindSwappedBondAsync(
        this HttpClient client, Guid fromAssetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AllBondsUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bonds = (await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken))!;

        return Assert.Single(bonds, b => b.SwappedFrom?.AssetId == fromAssetId);
    }

    public static UpdateBondRequest ToUpdateRequest(this BondResponse bond) => new()
    {
        Name = bond.Name,
        SeriesCode = bond.SeriesCode,
        Type = bond.Type,
        PurchaseDate = bond.PurchaseDate,
        BondCount = bond.BondCount,
        PurchasePricePerBond = bond.PurchasePricePerBond,
        FirstPeriodRatePercent = bond.FirstPeriodRatePercent,
        MarginPercent = bond.MarginPercent,
        EarlyRedemptionFeePerBond = bond.EarlyRedemptionFeePerBond,
        TaxExempt = bond.TaxExempt
    };

    public const string AllCashAccountsUri = "/api/portfolio/cash-accounts";

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

    public const string AllMetalsUri = "/api/portfolio/metals";

    public static string MetalsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/metals";

    public static string MetalUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/metals/{assetId}";

    public static readonly DateTimeOffset MetalTodayUtc = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly MetalPurchaseDate = new(2026, 10, 1);

    public static AddMetalRequest NewMetalRequest(
        string name = "Maple Leaf 1 oz",
        Metal metal = Metal.Silver,
        decimal fineWeight = 1m,
        WeightUnit weightUnit = WeightUnit.TroyOunce,
        bool withFirstPurchase = true,
        decimal pieces = 10m,
        decimal pricePerPiece = 260.00m,
        DateOnly? purchaseDate = null) => new()
        {
            Name = name,
            Metal = metal,
            FineWeight = fineWeight,
            WeightUnit = weightUnit,
            FirstPurchase = withFirstPurchase
                ? new FirstPurchaseRequest { Pieces = pieces, PricePerPiece = pricePerPiece, Date = purchaseDate ?? MetalPurchaseDate }
                : null
        };

    public static UpdateMetalRequest NewUpdateMetalRequest(
        string name = "Gold bar",
        Metal metal = Metal.Silver,
        decimal fineWeight = 100m,
        WeightUnit weightUnit = WeightUnit.Gram) => new()
        {
            Name = name,
            Metal = metal,
            FineWeight = fineWeight,
            WeightUnit = weightUnit
        };

    public static async Task<MetalResponse> AddMetalAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken, AddMetalRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(MetalsUri(portfolioId), request ?? NewMetalRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken))!;
    }

    public static async Task<(Guid PortfolioId, MetalResponse Metal)> CreatePortfolioWithMetalAsync(
        this HttpClient client, CancellationToken cancellationToken, AddMetalRequest? request = null, string portfolioName = "Metals")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var metal = await client.AddMetalAsync(portfolioId, cancellationToken, request);

        return (portfolioId, metal);
    }

    public static async Task<MetalResponse> GetMetalAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(MetalUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken))!;
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
        // The largest page, so a settled-and-redeemed bond's whole history fits in one read.
        var response = await client.GetAsync($"{TransactionsUri(portfolioId, assetId)}?pageSize=100", cancellationToken);
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

    public static readonly DateOnly MetalCashDate = MetalPurchaseDate.AddDays(1);

    public sealed record CashAndMetal(Guid CashPortfolioId, Guid CashAssetId, Guid MetalPortfolioId, Guid MetalAssetId);

    public static async Task<CashAndMetal> CreateCashAndMetalAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 5_000m,
        decimal pieces = 0m)
    {
        var cashPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(cashPortfolioId, cancellationToken, balance: cashBalance);
        var (metalPortfolioId, metal) = await client.CreatePortfolioWithMetalAsync(
            cancellationToken,
            NewMetalRequest(name: "Gold bar", metal: Metal.Gold, withFirstPurchase: pieces > 0m, pieces: pieces),
            portfolioName: "Metals");

        return new CashAndMetal(cashPortfolioId, cashId, metalPortfolioId, metal.AssetId);
    }

    public static RecordTransactionRequest NewMetalCashRequest(
        Guid? cashAssetId,
        TransactionType type = TransactionType.Buy,
        decimal pieces = 2m,
        decimal pricePerPiece = 1_200.00m,
        DateOnly? date = null) => new()
        {
            Type = type,
            Quantity = pieces,
            UnitPrice = pricePerPiece,
            Date = date ?? MetalCashDate,
            CashAssetId = cashAssetId
        };

    public static async Task<TransactionResponse> RecordMetalTransactionWithCashAsync(
        this HttpClient client,
        Guid portfolioId,
        Guid assetId,
        Guid cashAssetId,
        CancellationToken cancellationToken,
        TransactionType type = TransactionType.Buy,
        decimal pieces = 2m,
        decimal pricePerPiece = 1_200.00m,
        DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync(
            TransactionsUri(portfolioId, assetId), NewMetalCashRequest(cashAssetId, type, pieces, pricePerPiece, date), cancellationToken);
        response.EnsureSuccessStatusCode();

        return Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items,
            t => t.Type == type && t.Transfer is not null);
    }

    public static async Task<Guid> CreateTransferAsync(
        this HttpClient client, CancellationToken cancellationToken, CreateTransferRequest request)
    {
        var response = await client.PostAsJsonAsync(TransfersUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreateTransferResponse>(cancellationToken))!.TransferId;
    }
}
