using System.Net.Http.Json;

namespace Skarbiec.Reporting.Portfolio;

/// <summary>
/// Calls Portfolio's <c>GET /api/portfolio/positions-for-valuation</c> (T2.11) — every user's
/// positions, paged, walked to exhaustion. Resilience and service discovery come from
/// <c>ConfigureHttpClientDefaults</c> in ServiceDefaults; a total failure to reach Portfolio at all
/// (unlike a single portfolio's own valuation) is deliberately left to propagate — see
/// <c>DailyPricesSyncedConsumer</c> for why.
/// </summary>
public sealed class PortfolioPositionsClient(HttpClient httpClient) : IPositionsClient
{
    private const int PageSize = 500;

    public async Task<IReadOnlyList<PositionForValuation>> GetAllPositionsAsync(CancellationToken cancellationToken)
    {
        var positions = new List<PositionForValuation>();
        var page = 1;

        while (true)
        {
            var response = await httpClient.GetFromJsonAsync<PositionsPageResponse>(
                $"/api/portfolio/positions-for-valuation?page={page}&pageSize={PageSize}", cancellationToken)
                ?? throw new InvalidOperationException("Portfolio returned an empty positions-for-valuation response.");

            positions.AddRange(response.Items);

            if (!response.HasMore)
            {
                break;
            }

            page++;
        }

        return positions;
    }

    private sealed record PositionsPageResponse
    {
        public required IReadOnlyList<PositionForValuation> Items { get; init; }
        public required bool HasMore { get; init; }
    }
}
