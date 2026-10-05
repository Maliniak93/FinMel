namespace Skarbiec.Portfolio.Features.CashAccounts;

public sealed record CashAccountsResponse
{
    public required IReadOnlyList<CashAccountResponse> Accounts { get; init; }
    public required IReadOnlyList<CashTotalResponse> Totals { get; init; }
}

public sealed record CashAccountResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal Balance { get; init; }
}

public sealed record CashTotalResponse
{
    public required string Currency { get; init; }
    public required decimal Balance { get; init; }
}
