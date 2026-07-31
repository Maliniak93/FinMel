namespace Skarbiec.Portfolio.Features;

public sealed record PagedResponse<TItem>
{
    public required IReadOnlyList<TItem> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
}
