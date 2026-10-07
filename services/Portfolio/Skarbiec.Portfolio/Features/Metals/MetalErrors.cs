using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Metals;

internal static class MetalErrors
{
    public static Error NotFound(Guid assetId) =>
        new("NotFound.Metal", $"Precious metal holding '{assetId}' was not found.");

    public static readonly Error UseMetalEndpoints =
        new("Validation.UseMetalEndpoints", "A PreciousMetal asset is a metal holding — create and edit it through the metal endpoints.");

    public static readonly Error PurchaseDateInFuture =
        new("Validation.PurchaseDateInFuture", "The purchase date can't be in the future.");
}
