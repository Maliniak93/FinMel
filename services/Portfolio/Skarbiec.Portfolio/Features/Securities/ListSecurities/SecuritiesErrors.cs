using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Securities.ListSecurities;

internal static class SecuritiesErrors
{
    public static readonly Error UnsupportedAssetClass =
        new("Validation.UnsupportedAssetClass", "Only Stock and Etf holdings are listed.");
}
