using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts;

public sealed record AccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AccountType Type { get; init; }
    public required TaxWrapper Wrapper { get; init; }
    public required bool IsArchived { get; init; }
}

public static class AccountMappingExtensions
{
    public static AccountResponse ToResponse(this Account account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        Type = account.Type,
        Wrapper = account.Wrapper,
        IsArchived = account.IsArchived
    };
}
