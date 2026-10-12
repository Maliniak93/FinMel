using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts;

public sealed record AccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AccountType Type { get; init; }
    public required TaxWrapper Wrapper { get; init; }
    public required bool IsArchived { get; init; }
    public required Guid CashAssetId { get; init; }

    /// <summary>The account's PLN cash, the quantity of its cash asset.</summary>
    public required decimal CashBalance { get; init; }
}

public static class AccountMappingExtensions
{
    public static AccountResponse ToResponse(this Account account, Asset cash) => new()
    {
        Id = account.Id,
        Name = account.Name,
        Type = account.Type,
        Wrapper = account.Wrapper,
        IsArchived = account.IsArchived,
        CashAssetId = cash.Id,
        CashBalance = cash.Quantity
    };
}
