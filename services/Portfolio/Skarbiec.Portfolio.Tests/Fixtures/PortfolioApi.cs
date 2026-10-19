using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.UpdateBond;
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
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of endpoint X calls X directly and asserts on the raw response.
internal static class PortfolioApi
{
    public const string AllAssetsUri = "/api/portfolio/assets";

    public static string AssetsUri(Guid accountId) =>
        $"{AllAssetsUri}?accountId={accountId}";

    public static string AssetUri(Guid assetId) =>
        $"/api/portfolio/assets/{assetId}";

    public static string ArchiveAssetUri(Guid assetId) =>
        $"{AssetUri(assetId)}/archive";

    public static string RestoreAssetUri(Guid assetId) =>
        $"{AssetUri(assetId)}/restore";

    public static async Task ArchiveAssetAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task RestoreAssetAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync(RestoreAssetUri(assetId), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static string TransactionsUri(Guid assetId) =>
        $"/api/portfolio/assets/{assetId}/transactions";

    public static string TransactionUri(Guid assetId, Guid transactionId) =>
        $"/api/portfolio/assets/{assetId}/transactions/{transactionId}";

    public const string AllDepositsUri = "/api/portfolio/deposits";

    public static string DepositsOfAccountUri(Guid accountId) =>
        $"{AllDepositsUri}?accountId={accountId}";

    public static string DepositUri(Guid assetId) =>
        $"/api/portfolio/deposits/{assetId}";

    public static string DepositSettlementPreviewUri(Guid assetId) =>
        $"{DepositUri(assetId)}/settlement-preview";

    public static string SettleDepositUri(Guid assetId) =>
        $"{DepositUri(assetId)}/settle";

    public static readonly DateTimeOffset AfterDefaultMaturityUtc = new(2026, 4, 20, 10, 0, 0, TimeSpan.Zero);

    public static SettleDepositRequest NewSettleRequest(
        DateOnly? settledOn = null,
        decimal grossInterest = 147.95m,
        decimal tax = 28.12m,
        bool payOut = false) => new()
        {
            SettledOn = settledOn ?? new DateOnly(2026, 4, 15),
            GrossInterest = grossInterest,
            Tax = tax,
            PayOut = payOut
        };

    public static string PayOutDepositUri(Guid assetId) =>
        $"{DepositUri(assetId)}/payout";

    public static PayOutDepositRequest NewPayOutRequest(DateOnly? date = null) => new()
    {
        Date = date ?? new DateOnly(2026, 4, 18)
    };

    public static async Task PayOutDepositAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken, DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync(PayOutDepositUri(assetId), NewPayOutRequest(date), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<DepositSettlementPreviewResponse> GetSettlementPreviewAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(DepositSettlementPreviewUri(assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositSettlementPreviewResponse>(cancellationToken))!;
    }

    public static string RollOverDepositUri(Guid assetId) =>
        $"{DepositUri(assetId)}/rollover";

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
        this HttpClient client, Guid assetId, CancellationToken cancellationToken, object? body = null)
    {
        var response = await client.PostAsJsonAsync(RollOverDepositUri(assetId), body ?? NewRollOverRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return await client.GetDepositAsync(assetId, cancellationToken);
    }

    public sealed record PaidOutDeposit(DepositResponse Deposit, Guid CashAssetId);

    // CashAssetId is the money asset of the deposit's return account, which received the payout.
    public static async Task<PaidOutDeposit> CreatePaidOutDepositAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken, NewSettleRequest(payOut: true));
        var moneyAssetId = await client.GetAccountCashAssetIdAsync(deposit.ReturnAccountId, cancellationToken);

        return new PaidOutDeposit(deposit, moneyAssetId);
    }

    public static async Task SettleDepositAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken, SettleDepositRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(SettleDepositUri(assetId), request ?? NewSettleRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<DepositResponse> GetDepositAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(DepositUri(assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken))!;
    }

    public static AddDepositRequest NewDepositRequest(
        string name = "Term deposit",
        string? bankName = "Test bank",
        decimal principal = 10_000m,
        DateOnly? startDate = null,
        int termLength = 3,
        DepositTermUnit termUnit = DepositTermUnit.Months,
        decimal annualInterestRatePercent = 6m,
        DepositCapitalization capitalization = DepositCapitalization.AtMaturity,
        decimal earlyBreakInterestLossPercent = 100m,
        Guid? sourceAccountId = null,
        Guid? returnAccountId = null) => new()
        {
            Name = name,
            BankName = bankName,
            SourceAccountId = sourceAccountId ?? Guid.Empty,
            ReturnAccountId = returnAccountId,
            Principal = principal,
            StartDate = startDate ?? new DateOnly(2026, 1, 15),
            TermLength = termLength,
            TermUnit = termUnit,
            AnnualInterestRatePercent = annualInterestRatePercent,
            Capitalization = capitalization,
            EarlyBreakInterestLossPercent = earlyBreakInterestLossPercent
        };

    public static UpdateDepositRequest ToUpdateRequest(this AddDepositRequest request) => new()
    {
        Name = request.Name,
        BankName = request.BankName,
        ReturnAccountId = request.ReturnAccountId,
        Principal = request.Principal,
        StartDate = request.StartDate,
        TermLength = request.TermLength,
        TermUnit = request.TermUnit,
        AnnualInterestRatePercent = request.AnnualInterestRatePercent,
        Capitalization = request.Capitalization,
        EarlyBreakInterestLossPercent = request.EarlyBreakInterestLossPercent
    };

    // A request without a source account is funded from a fresh PLN bank account that holds far more than any deposit needs.
    public static async Task<AddDepositRequest> WithFundingAsync(
        this HttpClient client, AddDepositRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceAccountId != Guid.Empty)
        {
            return request;
        }

        var source = await client.CreateBankAccountAsync(
            cancellationToken, $"Funding {Guid.NewGuid():N}", balance: 1_000_000m, depositedOn: new DateOnly(2025, 1, 1));

        return request with { SourceAccountId = source.AccountId };
    }

    public static async Task<DepositResponse> AddDepositAsync(
        this HttpClient client, CancellationToken cancellationToken, AddDepositRequest? request = null)
    {
        var funded = await client.WithFundingAsync(request ?? NewDepositRequest(), cancellationToken);
        var response = await client.PostAsJsonAsync(AllDepositsUri, funded, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken))!;
    }

    public const string AllBondsUri = "/api/portfolio/bonds";

    public static string BondUri(Guid assetId) =>
        $"/api/portfolio/bonds/{assetId}";

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
        decimal earlyRedemptionFeePerBond = 3.00m) => new()
        {
            Name = name,
            SeriesCode = seriesCode,
            Type = type,
            PurchaseDate = purchaseDate ?? DefaultBondPurchaseDate,
            BondCount = bondCount,
            PurchasePricePerBond = purchasePricePerBond,
            FirstPeriodRatePercent = firstPeriodRatePercent,
            MarginPercent = marginPercent,
            EarlyRedemptionFeePerBond = earlyRedemptionFeePerBond
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
        EarlyRedemptionFeePerBond = request.EarlyRedemptionFeePerBond
    };

    public static async Task<BondResponse> AddBondAsync(
        this HttpClient client,
        Guid accountId,
        CancellationToken cancellationToken,
        AddBondRequest? request = null,
        bool fundAccount = true)
    {
        if (fundAccount)
        {
            await client.FundAccountAsync(accountId, cancellationToken);
        }

        var response = await client.PostAsJsonAsync(
            AllBondsUri, (request ?? NewBondRequest()) with { AccountId = accountId }, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken))!;
    }

    public static async Task<(Guid AccountId, BondResponse Bond)> CreateAccountWithBondAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        AddBondRequest? request = null,
        TaxWrapper wrapper = TaxWrapper.Regular,
        string accountName = "Bonds")
    {
        var accountId = await client.CreateAccountAsync(cancellationToken, accountName, AccountType.Bonds, wrapper);
        var bond = await client.AddBondAsync(accountId, cancellationToken, request);

        return (accountId, bond);
    }

    public static async Task<BondResponse> GetBondAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(BondUri(assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken))!;
    }

    public sealed record FundedBond(Guid CashAssetId, Guid BondAccountId, BondResponse Bond);

    public static async Task<FundedBond> CreateFundedBondAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 6_000m,
        AddBondRequest? request = null,
        DateOnly? cashToppedUpOn = null,
        TaxWrapper wrapper = TaxWrapper.Regular,
        string accountName = "Bonds")
    {
        var bondAccountId = await client.CreateAccountAsync(cancellationToken, accountName, AccountType.Bonds, wrapper);
        var cashId = await client.GetAccountCashAssetIdAsync(bondAccountId, cancellationToken);
        await client.RecordTransactionAsync(
            cashId, TransactionType.Deposit, cashBalance, cashToppedUpOn ?? DefaultTopUpDate, cancellationToken, unitPrice: 1m);
        var bond = await client.AddBondAsync(bondAccountId, cancellationToken, request, fundAccount: false);

        return new FundedBond(cashId, bondAccountId, bond);
    }

    public static string BondInterestSettlementsUri(Guid assetId) =>
        $"{BondUri(assetId)}/interest-settlements";

    public static string BondInterestPreviewUri(Guid assetId) =>
        $"{BondInterestSettlementsUri(assetId)}/preview";

    public static string BondInterestSettlementUri(Guid assetId, Guid settlementId) =>
        $"{BondInterestSettlementsUri(assetId)}/{settlementId}";

    public static readonly DateOnly RorPurchaseDate = new(2026, 6, 10);

    public static AddBondRequest NewRorBondRequest(
        DateOnly? purchaseDate = null, int bondCount = 50, decimal firstPeriodRatePercent = 4.00m) =>
        NewBondRequest(
            name: "ROR0627",
            seriesCode: "ROR0627",
            type: TreasuryBondType.Ror,
            purchaseDate: purchaseDate ?? RorPurchaseDate,
            bondCount: bondCount,
            firstPeriodRatePercent: firstPeriodRatePercent,
            marginPercent: null);

    public static object NewSettleBondBody(
        IEnumerable<(int PeriodIndex, decimal? RatePercent)> periods) => new
        {
            periods = periods.Select(p => new { periodIndex = p.PeriodIndex, ratePercent = p.RatePercent }).ToList()
        };

    public static async Task<HttpResponseMessage> SettleBondInterestRawAsync(
        this HttpClient client,
        Guid assetId,
        IEnumerable<(int PeriodIndex, decimal? RatePercent)> periods,
        CancellationToken cancellationToken) =>
        await client.PostAsJsonAsync(
            BondInterestSettlementsUri(assetId), NewSettleBondBody(periods), cancellationToken);

    public static async Task SettleBondInterestAsync(
        this HttpClient client,
        Guid assetId,
        IEnumerable<(int PeriodIndex, decimal? RatePercent)> periods,
        CancellationToken cancellationToken)
    {
        var response = await client.SettleBondInterestRawAsync(assetId, periods, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> GetLastBondSettlementIdAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var bond = await client.GetBondAsync(assetId, cancellationToken);

        return bond.LastSettlement!.SettlementId;
    }

    public static string BondRedemptionUri(Guid assetId) =>
        $"{BondUri(assetId)}/redemption";

    public static string BondRedemptionPreviewUri(Guid assetId) =>
        $"{BondUri(assetId)}/redemption-preview";

    public static string BondSwapUri(Guid assetId) =>
        $"{BondUri(assetId)}/swap";

    public static readonly DateOnly TosMaturityDate = new(2029, 10, 1);

    public static readonly DateTimeOffset AfterTosMaturityUtc = new(2029, 10, 2, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly RorMaturityDate = new(2027, 6, 10);

    public static readonly DateTimeOffset AfterRorMaturityUtc = new(2027, 6, 11, 10, 0, 0, TimeSpan.Zero);

    public static AddBondRequest NewTosBondRequest(int bondCount = 10, decimal purchasePricePerBond = 100m) =>
        NewBondRequest(
            name: "TOS1029",
            seriesCode: "TOS1029",
            type: TreasuryBondType.Tos,
            bondCount: bondCount,
            purchasePricePerBond: purchasePricePerBond,
            firstPeriodRatePercent: 4.40m,
            marginPercent: null);

    public static async Task<FundedBond> CreateSettledTosAsync(
        this HttpClient client, CancellationToken cancellationToken, TaxWrapper wrapper = TaxWrapper.Regular, Guid? groupId = null)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: NewTosBondRequest() with { GroupId = groupId }, wrapper: wrapper);
        await client.SettleBondInterestAsync(
            funded.Bond.AssetId, [(1, null), (2, null), (3, null)], cancellationToken);

        return funded;
    }

    public static async Task<FundedBond> CreateSettledRorAsync(
        this HttpClient client, CancellationToken cancellationToken, decimal purchasePricePerBond = 99.90m, TaxWrapper wrapper = TaxWrapper.Regular)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken,
            request: NewRorBondRequest(bondCount: 10) with { PurchasePricePerBond = purchasePricePerBond },
            wrapper: wrapper);
        var periods = Enumerable.Range(1, 12).Select(index => (index, index == 1 ? (decimal?)null : 3.75m)).ToList();
        await client.SettleBondInterestAsync(funded.Bond.AssetId, periods, cancellationToken);

        return funded;
    }

    public static async Task<HttpResponseMessage> RedeemBondRawAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken) =>
        await client.PostAsJsonAsync(BondRedemptionUri(assetId), new { }, cancellationToken);

    public static async Task RedeemBondAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.RedeemBondRawAsync(assetId, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static readonly DateOnly EdoEarlyPurchaseDate = new(2025, 3, 1);

    public static readonly DateOnly EdoEarlyRedemptionDate = new(2026, 5, 13);

    public static readonly DateTimeOffset EdoEarlyRedemptionDayUtc = new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly RorEarlyRedemptionDate = new(2026, 9, 25);

    public static readonly DateTimeOffset RorEarlyRedemptionDayUtc = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    public static string BondEarlyRedemptionUri(Guid assetId) =>
        $"{BondUri(assetId)}/early-redemption";

    public static string BondEarlyRedemptionPreviewUri(
        Guid assetId, DateOnly date, int bondCount, decimal? runningPeriodRatePercent = null) =>
        $"{BondUri(assetId)}/early-redemption-preview?date={date:yyyy-MM-dd}&bondCount={bondCount}"
        + (runningPeriodRatePercent is { } rate ? $"&runningPeriodRatePercent={rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty);

    public static AddBondRequest NewEdoEarlyBondRequest(int bondCount = 10) =>
        NewBondRequest(purchaseDate: EdoEarlyPurchaseDate, bondCount: bondCount, earlyRedemptionFeePerBond: 3.00m);

    public static object NewEarlyRedeemBody(
        DateOnly date, int bondCount, decimal? runningPeriodRatePercent = null) => new
        {
            date,
            bondCount,
            runningPeriodRatePercent
        };

    public static async Task<HttpResponseMessage> RedeemBondEarlyRawAsync(
        this HttpClient client,
        Guid assetId,
        DateOnly date,
        int bondCount,
        CancellationToken cancellationToken,
        decimal? runningPeriodRatePercent = null) =>
        await client.PostAsJsonAsync(
            BondEarlyRedemptionUri(assetId),
            NewEarlyRedeemBody(date, bondCount, runningPeriodRatePercent),
            cancellationToken);

    public static async Task RedeemBondEarlyAsync(
        this HttpClient client,
        Guid assetId,
        DateOnly date,
        int bondCount,
        CancellationToken cancellationToken,
        decimal? runningPeriodRatePercent = null)
    {
        var response = await client.RedeemBondEarlyRawAsync(
            assetId, date, bondCount, cancellationToken, runningPeriodRatePercent);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<FundedBond> CreateEdoYearOneSettledAsync(
        this HttpClient client, CancellationToken cancellationToken, bool settleYearOne = true)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: NewEdoEarlyBondRequest(), cashToppedUpOn: new DateOnly(2025, 1, 1));
        if (settleYearOne)
        {
            await client.SettleBondInterestAsync(funded.Bond.AssetId, [(1, null)], cancellationToken);
        }

        return funded;
    }

    public static async Task<FundedBond> CreateRorWithThreeMonthsSettledAsync(
        this HttpClient client, CancellationToken cancellationToken, int bondCount = 20)
    {
        var funded = await client.CreateFundedBondAsync(
            cancellationToken, request: NewRorBondRequest(bondCount: bondCount) with { EarlyRedemptionFeePerBond = 0.50m });
        await client.SettleBondInterestAsync(
            funded.Bond.AssetId, [(1, null), (2, 3.75m), (3, 3.75m)], cancellationToken);

        return funded;
    }

    public static object NewSwapBody(
        int bondCount,
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
            }
        };

    public static async Task<HttpResponseMessage> SwapBondRawAsync(
        this HttpClient client,
        Guid assetId,
        object body,
        CancellationToken cancellationToken) =>
        await client.PostAsJsonAsync(BondSwapUri(assetId), body, cancellationToken);

    public static async Task SwapBondAsync(
        this HttpClient client,
        Guid assetId,
        int bondCount,
        CancellationToken cancellationToken)
    {
        var response = await client.SwapBondRawAsync(assetId, NewSwapBody(bondCount), cancellationToken);
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
        EarlyRedemptionFeePerBond = bond.EarlyRedemptionFeePerBond
    };

    public static readonly DateTimeOffset FixedTodayUtc = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly FixedToday = new(2026, 2, 1);
    public const string AllMetalsUri = "/api/portfolio/metals";

    public static string MetalUri(Guid assetId) =>
        $"/api/portfolio/metals/{assetId}";

    public static readonly DateTimeOffset MetalTodayUtc = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly MetalPurchaseDate = new(2026, 10, 1);

    public static AddMetalRequest NewMetalRequest(
        string name = "Maple Leaf 1 oz",
        Metal metal = Metal.Silver,
        decimal fineWeight = 1m,
        WeightUnit weightUnit = WeightUnit.TroyOunce) => new()
        {
            Name = name,
            Metal = metal,
            FineWeight = fineWeight,
            WeightUnit = weightUnit
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
        this HttpClient client, CancellationToken cancellationToken, AddMetalRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(AllMetalsUri, request ?? NewMetalRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken))!;
    }

    public static async Task<MetalResponse> GetMetalAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(MetalUri(assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<MetalResponse>(cancellationToken))!;
    }

    public static string TransferCandidatesUri(string? currency = "PLN", string? assetClass = "Cash", bool accountCash = false)
    {
        var query = new List<string>();
        if (accountCash)
        {
            query.Add("accountCash=true");
        }

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
        CancellationToken cancellationToken,
        decimal balance = 5_000m,
        DateOnly? toppedUpOn = null,
        string currency = "PLN",
        string name = "Cash account")
    {
        var cashId = await client.AddCashAssetAsync(cancellationToken, currency: currency, name: name);
        await client.RecordTransactionAsync(
            cashId, TransactionType.Deposit, balance, toppedUpOn ?? DefaultTopUpDate, cancellationToken, unitPrice: 1m);

        return cashId;
    }

    public static async Task<Guid> AddArchivedCashAssetAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Archived cash",
        string currency = "PLN")
    {
        var place = await client.CreateCashPlaceAsync(cancellationToken, name, currency);
        await client.ArchiveCashPlaceAsync(place.Id, cancellationToken);

        return place.AssetId(currency);
    }

    // CashAssetId is the money asset of the PLN bank account the deposit was opened from.
    public sealed record FundedDeposit(Guid CashAssetId, DepositResponse Deposit, Guid AccountId);

    public static async Task<FundedDeposit> CreateFundedDepositAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 5_000m,
        decimal principal = 1_000m)
    {
        var source = await client.CreateBankAccountAsync(
            cancellationToken, "Cash account", balance: cashBalance, depositedOn: DefaultTopUpDate);
        var deposit = await client.AddDepositAsync(
            cancellationToken, NewDepositRequest(principal: principal, sourceAccountId: source.AccountId));

        return new FundedDeposit(source.MoneyAssetId, deposit, source.AccountId);
    }

    public static async Task<TransactionResponse> GetCashWithdrawAsync(
        this HttpClient client, Guid cashId, CancellationToken cancellationToken) =>
        Assert.Single(
            (await client.ListTransactionsAsync(cashId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

    public static async Task<Guid> AddAssetAsync(
        this HttpClient client,
        Guid accountId,
        CancellationToken cancellationToken,
        string name = "Test asset",
        AssetClass assetClass = AssetClass.Stock,
        string currency = "PLN")
    {
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = assetClass,
            Name = name,
            Currency = currency,
            InstrumentId = AssetValuationModes.SupportsCurrencyValued(assetClass) ? null : FakeInstrumentLookupClient.IdFor(assetClass, currency)
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!.Id;
    }

    // A cash place named after the balance, holding it alone; returns the balance's asset id.
    public static async Task<Guid> AddCashAssetAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string currency = "PLN",
        string name = "Cash account") =>
        (await client.CreateCashPlaceAsync(cancellationToken, name, currency)).AssetId(currency);

    public const string SecuritiesBaseUri = "/api/portfolio/securities";

    public static string AllSecuritiesUri(AssetClass assetClass) => $"{SecuritiesBaseUri}?assetClass={assetClass}";

    // The instrument must already be scripted in FakeInstrumentLookupClient with the same class and currency.
    public static async Task<Guid> AddMarketAssetAsync(
        this HttpClient client,
        Guid accountId,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Stock,
        string currency = "PLN",
        Guid? instrumentId = null,
        string name = "Market asset")
    {
        var request = new AddAssetRequest
        {
            AccountId = accountId,
            AssetClass = assetClass,
            Name = name,
            Currency = currency,
            InstrumentId = instrumentId ?? Guid.NewGuid()
        };

        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!.Id;
    }

    // The account's cash is funded because every stock trade settles in it.
    public static async Task<(Guid AccountId, Guid AssetId)> CreateAccountWithAssetAsync(
        this HttpClient client, CancellationToken cancellationToken, string currency = "PLN")
    {
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, currency: currency);

        return (accountId, assetId);
    }

    public static async Task<(Guid AccountId, IReadOnlyList<Guid> AssetIds)> CreateAccountWithAssetsAndTransactionsAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        int assetCount = 2,
        int transactionsPerAsset = 2,
        string accountName = "Retirement")
    {
        var accountId = await client.CreateAccountAsync(cancellationToken, accountName);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetIds = new List<Guid>();

        for (var i = 0; i < assetCount; i++)
        {
            var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: $"Asset {i + 1}");
            for (var t = 0; t < transactionsPerAsset; t++)
            {
                await client.RecordTransactionAsync(
                    assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 1).AddDays(t), cancellationToken);
            }

            assetIds.Add(assetId);
        }

        return (accountId, assetIds);
    }

    public static async Task<Guid> RecordTransactionAsync(
        this HttpClient client,
        Guid assetId,
        TransactionType type,
        decimal quantity,
        DateOnly date,
        CancellationToken cancellationToken,
        decimal unitPrice = 10m)
    {
        var request = new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = unitPrice, Date = date };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!.Id;
    }

    public static async Task<AssetResponse> GetAssetAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
    }

    public static async Task<PagedResponse<TransactionResponse>> ListTransactionsAsync(
        this HttpClient client, Guid assetId, CancellationToken cancellationToken)
    {
        // The largest page, so a settled-and-redeemed bond's whole history fits in one read.
        var response = await client.GetAsync($"{TransactionsUri(assetId)}?pageSize=100", cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!;
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

    public static readonly DateOnly MetalCashDate = MetalPurchaseDate.AddDays(1);

    public sealed record CashAndMetal(Guid CashAssetId, Guid MetalAssetId);

    // A metal starts with no pieces; every holding is bought through a trade that debits its settlement source.
    public static async Task<CashAndMetal> CreateCashAndMetalAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        decimal cashBalance = 5_000m)
    {
        var cashId = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: cashBalance);
        var metal = await client.AddMetalAsync(
            cancellationToken,
            NewMetalRequest(name: "Gold bar", metal: Metal.Gold));

        return new CashAndMetal(cashId, metal.AssetId);
    }

    // The invoice total is the amount of record; the stored unit price is totalAmount / pieces.
    public static RecordTransactionRequest NewMetalCashRequest(
        Guid? cashAssetId,
        TransactionType type = TransactionType.Buy,
        decimal pieces = 2m,
        decimal totalAmount = 2_400.00m,
        DateOnly? date = null) => new()
        {
            Type = type,
            Quantity = pieces,
            TotalAmount = totalAmount,
            Date = date ?? MetalCashDate,
            CashAssetId = cashAssetId
        };

    public static async Task<TransactionResponse> RecordMetalTransactionWithCashAsync(
        this HttpClient client,
        Guid assetId,
        Guid cashAssetId,
        CancellationToken cancellationToken,
        TransactionType type = TransactionType.Buy,
        decimal pieces = 2m,
        decimal totalAmount = 2_400.00m,
        DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync(
            TransactionsUri(assetId), NewMetalCashRequest(cashAssetId, type, pieces, totalAmount, date), cancellationToken);
        response.EnsureSuccessStatusCode();

        return Assert.Single(
            (await client.ListTransactionsAsync(assetId, cancellationToken)).Items,
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
