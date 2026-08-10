namespace Skarbiec.Portfolio.Features.GetPositionsForValuation;

public sealed record PositionsForValuationPage
{
    public required IReadOnlyList<PositionForValuationResponse> Items { get; init; }
    public required bool HasMore { get; init; }
}
