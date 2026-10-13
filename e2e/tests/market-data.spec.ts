import { STUB_URL, expect, test } from './fixtures';

interface StubRequest {
  provider: string;
  path: string;
}

interface SearchResponse {
  results: { ticker: string; name: string }[];
  providerUnavailable: boolean;
}

test.describe('market data stub', () => {
  test('market data: instrument search returns the stub listing', async ({
    request,
    accessToken,
  }) => {
    const response = await request.get(
      '/api/marketdata/instruments/search?q=PKO&assetClass=Stock',
      {
        headers: { Authorization: `Bearer ${accessToken}` },
      },
    );

    expect(response.ok()).toBeTruthy();
    const body = (await response.json()) as SearchResponse;
    expect(body.providerUnavailable).toBe(false);
    expect(body.results.map((r) => r.ticker)).toContain('PKO.WA');
  });

  test('market data: MarketData fetched NBP and gold-api from the stub', async ({
    request,
    accessToken,
  }) => {
    // Gold is fetched only for an instrument some asset uses, so the user holds a gold bar first.
    const holding = await request.post('/api/portfolio/metals', {
      headers: { Authorization: `Bearer ${accessToken}` },
      data: { name: 'Gold bar 50 g', metal: 0, fineWeight: 50, weightUnit: 'Gram' },
    });
    expect(holding.ok()).toBeTruthy();

    // The FX and price crons fire every 15 seconds in the e2e stack.
    await expect(async () => {
      const log = await request.get(`${STUB_URL}/__requests`);
      const providers = new Set(((await log.json()) as StubRequest[]).map((r) => r.provider));
      expect([...providers]).toEqual(expect.arrayContaining(['nbp', 'gold']));
    }).toPass({ timeout: 60_000, intervals: [2_000] });
  });
});
