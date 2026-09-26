using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RecordTransaction;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Portfolio's HTTP surface as arrange-step helpers: route builders, plus the "get me a portfolio /
/// an asset / a transaction to act on" calls that nearly every slice test needs before it can
/// exercise the endpoint it actually cares about.
/// </summary>
/// <remarks>
/// Arrange only. A test asserting on one of these endpoints must call it directly and assert on the
/// raw <see cref="HttpResponseMessage"/> — the helpers here <c>EnsureSuccessStatusCode</c>, which
/// would turn the very failure such a test is looking for into an exception.
/// </remarks>
internal static class PortfolioApi
{
    public const string PortfoliosUri = "/api/portfolio/portfolios";

    public static string PortfolioUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}";

    public static string AssetsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/assets";

    public static string AssetUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}";

    public static string TransactionsUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions";

    public static string TransactionUri(Guid portfolioId, Guid assetId, Guid transactionId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions/{transactionId}";

    /// <summary>term-deposits: every deposit of the calling user, across all their portfolios.</summary>
    public const string AllDepositsUri = "/api/portfolio/deposits";

    public static string DepositsUri(Guid portfolioId) =>
        $"{PortfoliosUri}/{portfolioId}/deposits";

    public static string DepositUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/deposits/{assetId}";

    /// <summary>term-deposits-settlement: the settlement projection of a Due deposit.</summary>
    public static string DepositSettlementPreviewUri(Guid portfolioId, Guid assetId) =>
        $"{DepositUri(portfolioId, assetId)}/settlement-preview";

    /// <summary>term-deposits-settlement: settles a Due deposit at maturity.</summary>
    public static string SettleDepositUri(Guid portfolioId, Guid assetId) =>
        $"{DepositUri(portfolioId, assetId)}/settle";

    /// <summary>
    /// term-deposits-settlement: "now" for a settlement fact — Warsaw 2026-04-20 12:00, five days after
    /// the <see cref="NewDepositRequest"/> deposit's 2026-04-15 maturity, so that deposit is Due.
    /// Pin it with <c>Factory.Clock.SetUtcNow(...)</c>.
    /// </summary>
    public static readonly DateTimeOffset AfterDefaultMaturityUtc = new(2026, 4, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// term-deposits-settlement: a valid <see cref="SettleDepositRequest"/> — by default exactly the
    /// part-1 projection of <see cref="NewDepositRequest"/>'s deposit (gross 147.95, tax 28.12 → net
    /// 119.83, final 10 119.83) settled on its 2026-04-15 maturity. Override only what the fact is about.
    /// </summary>
    public static SettleDepositRequest NewSettleRequest(
        DateOnly? settledOn = null,
        decimal grossInterest = 147.95m,
        decimal tax = 28.12m) => new()
        {
            SettledOn = settledOn ?? new DateOnly(2026, 4, 15),
            GrossInterest = grossInterest,
            Tax = tax
        };

    /// <summary>
    /// term-deposits-settlement: settles <paramref name="assetId"/> (arrange only — the fact must have
    /// pinned a clock past its maturity). Defaults to <see cref="NewSettleRequest"/>.
    /// </summary>
    public static async Task SettleDepositAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken, SettleDepositRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(SettleDepositUri(portfolioId, assetId), request ?? NewSettleRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>term-deposits: a deposit as <c>GET .../deposits/{assetId}</c> returns it.</summary>
    public static async Task<DepositResponse> GetDepositAsync(
        this HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(DepositUri(portfolioId, assetId), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken))!;
    }

    /// <summary>
    /// term-deposits: a valid <see cref="AddDepositRequest"/> — by default the spec's AC-1 terms
    /// (10 000.00 PLN at 6 % from 2026-01-15 for 3 months, capitalised at maturity, taxed), which
    /// mature on 2026-04-15 with a net interest of 119.83. Override only what the fact is about;
    /// derive an invalid request with a <c>with</c> expression.
    /// </summary>
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

    /// <summary>The <see cref="UpdateDepositRequest"/> carrying the same terms as <paramref name="request"/> (currency is immutable, so it is dropped).</summary>
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

    /// <summary>
    /// term-deposits: adds a term deposit to <paramref name="portfolioId"/> through the deposit
    /// endpoint (the only way a Deposit-class asset comes to exist) and returns it. Defaults to
    /// <see cref="NewDepositRequest"/>.
    /// </summary>
    public static async Task<DepositResponse> AddDepositAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken, AddDepositRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(DepositsUri(portfolioId), request ?? NewDepositRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DepositResponse>(cancellationToken))!;
    }

    /// <summary>The common deposit arrange step: a portfolio holding one term deposit, both owned by <paramref name="client"/>'s user.</summary>
    public static async Task<(Guid PortfolioId, DepositResponse Deposit)> CreatePortfolioWithDepositAsync(
        this HttpClient client, CancellationToken cancellationToken, AddDepositRequest? request = null, string portfolioName = "Savings")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: portfolioName);
        var deposit = await client.AddDepositAsync(portfolioId, cancellationToken, request);

        return (portfolioId, deposit);
    }

    /// <summary>
    /// asset-transfers-deposit-funding: <c>GET /api/portfolio/transfer-candidates</c>. A <see langword="null"/>
    /// argument leaves that query parameter out, so a fact can ask without it.
    /// </summary>
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

    /// <summary>asset-transfers-deposit-funding: the default date a Cash asset is topped up on — before <see cref="NewDepositRequest"/>'s 2026-01-15 start.</summary>
    public static readonly DateOnly DefaultTopUpDate = new(2026, 1, 1);

    /// <summary>
    /// asset-transfers-deposit-funding: a <see cref="AssetClass.Cash"/> asset holding
    /// <paramref name="balance"/>, from one ordinary Deposit transaction dated <paramref name="toppedUpOn"/>
    /// (default <see cref="DefaultTopUpDate"/>). Returns the asset id.
    /// </summary>
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

    /// <summary>
    /// asset-transfers-deposit-funding: a PLN Cash asset holding <paramref name="balance"/> in a
    /// portfolio of its own (<paramref name="portfolioName"/>), which is then archived — a counterpart a
    /// transfer must refuse. Returns both ids.
    /// </summary>
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

    /// <summary>asset-transfers-deposit-funding: what <see cref="CreateFundedDepositAsync"/> arranged.</summary>
    public sealed record FundedDeposit(Guid CashPortfolioId, Guid CashAssetId, Guid DepositPortfolioId, DepositResponse Deposit);

    /// <summary>
    /// asset-transfers-deposit-funding: the spec's funded deposit — a PLN "Cash account" holding
    /// <paramref name="cashBalance"/> (topped up on <see cref="DefaultTopUpDate"/>) in a "Wallet"
    /// portfolio, and a <paramref name="principal"/> deposit in a separate "Savings" portfolio funded from
    /// it (a Cash → Deposit transfer on the deposit's 2026-01-15 start date).
    /// </summary>
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

    /// <summary>asset-transfers-deposit-funding: the single Withdraw on <paramref name="cashId"/> — the Cash leg of a funding transfer.</summary>
    public static async Task<TransactionResponse> GetCashWithdrawAsync(
        this HttpClient client, Guid portfolioId, Guid cashId, CancellationToken cancellationToken) =>
        Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

    /// <summary>Creates a portfolio and returns its id. Pass distinct <paramref name="name"/>s — the name is unique per user.</summary>
    public static async Task<Guid> CreatePortfolioAsync(
        this HttpClient client, CancellationToken cancellationToken, string name = "Retirement")
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken))!.Id;
    }

    /// <summary>
    /// Archives <paramref name="portfolioId"/>. From then on every asset/transaction write in it is
    /// a 409 (archived-portfolio-out-of-net-worth) — arrange any content the fact needs first.
    /// </summary>
    public static async Task ArchivePortfolioAsync(
        this HttpClient client, Guid portfolioId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsync($"{PortfolioUri(portfolioId)}/archive", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Adds an asset to <paramref name="portfolioId"/> and returns its id. No InitialTransaction is
    /// sent (M1.5), so the asset starts at quantity 0 with zero transactions — quantity is driven
    /// purely by transactions (ADR-009), never a directly-settable request field.
    /// </summary>
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

    /// <summary>
    /// cash-transaction-types: adds a currency-valued <see cref="AssetClass.Cash"/> asset (neither
    /// InstrumentId nor ManualValue) with no transactions, and returns its id. Such an asset accepts
    /// only Deposit/Withdraw transactions. A Deposit-class asset goes through
    /// <see cref="AddDepositAsync"/> instead (term-deposits).
    /// </summary>
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

    /// <summary>
    /// The common arrange step: a portfolio holding one asset, both owned by <paramref name="client"/>'s
    /// user. <paramref name="currency"/> is the asset's — a non-PLN one makes every transaction write
    /// resolve its PLN rate through <see cref="FakeFxRateLookupClient"/>.
    /// </summary>
    public static async Task<(Guid PortfolioId, Guid AssetId)> CreatePortfolioWithAssetAsync(
        this HttpClient client, CancellationToken cancellationToken, string currency = "PLN")
    {
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, currency: currency);

        return (portfolioId, assetId);
    }

    /// <summary>
    /// spec-08: a portfolio holding <paramref name="assetCount"/> assets, each with
    /// <paramref name="transactionsPerAsset"/> Buy transactions — the children a cascading delete
    /// must take with it (or, for a stranger, must leave untouched).
    /// </summary>
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

    /// <summary>Records a transaction and returns its id.</summary>
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
}
