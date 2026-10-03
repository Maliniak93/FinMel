using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds;

internal static class BondErrors
{
    public static Error NotFound(Guid assetId) =>
        new("NotFound.Bond", $"Treasury bond '{assetId}' was not found.");

    public static readonly Error UseBondEndpoints =
        new("Validation.UseBondEndpoints", "A Bond asset is a treasury bond — create and edit it through the bond endpoints.");

    public static readonly Error TransactionsManaged =
        new("Conflict.BondTransactionsManaged", "A treasury bond's transactions are managed by the bond itself — edit the bond instead.");
}
