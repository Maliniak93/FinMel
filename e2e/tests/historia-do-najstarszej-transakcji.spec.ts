import type { APIRequestContext, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  wealthTab,
  warsawDate,
} from './fixtures';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_ETF = 3;
const TYPE_BUY = 0;
const VARIANT_CURRENT = 0;
const GOLD = 'Złoto 31,1 g';

interface Point {
  date: string;
  netWorthPln: number;
  isIncomplete: boolean;
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

async function fetchPoints(request: APIRequestContext, token: string): Promise<Point[]> {
  const response = await request.get('/api/reporting/net-worth-history?range=MAX', {
    headers: bearer(token),
  });
  expect(response.ok()).toBeTruthy();
  return ((await response.json()) as { points: Point[] }).points;
}

async function expectPointsProjected(
  request: APIRequestContext,
  token: string,
  assertion: (points: Point[]) => void,
): Promise<void> {
  let points: Point[] = [];
  await expectProjected(
    async () => {
      points = await fetchPoints(request, token);
    },
    async () => {
      assertion(points);
    },
    { timeout: 90_000 },
  );
}

function pointOn(points: Point[], date: string): Point {
  const found = points.find((point) => point.date === date);
  expect(found, `point ${date}`).toBeDefined();
  return found as Point;
}

function between(points: Point[], from: string, to: string): Point[] {
  return points.filter((point) => point.date >= from && point.date <= to);
}

async function openMaxChart(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await page
    .getByRole('radiogroup', { name: 'Zakres wykresu' })
    .getByRole('radio', { name: 'MAX' })
    .click();
}

function chart(page: Page) {
  return page.getByRole('img', { name: /^Wykres historii wartości/ });
}

test('historia do najstarszej transakcji: złoto i ETF w walucie wyceniane od pierwszego zakupu', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  const headers = bearer(token);
  const yesterday = warsawDate(1);
  let vwceId = '';

  const bank = await createAccountThroughApi(request, token, {
    name: 'Konto na złoto',
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
  } as Parameters<typeof createAccountThroughApi>[2]);
  const broker = await createAccountThroughApi(request, token, {
    name: 'Broker',
    type: ACCOUNT_TYPE_BROKERAGE,
    wrapper: WRAPPER_REGULAR,
  });

  await page.goto('/login');
  await logInThroughUi(page, user);

  await test.step('Krok 1: gold 31,1 g bought on 2023-12-15 for 6 000 zł is on the Metale list', async () => {
    await depositCashThroughApi(request, token, bank.cashAssetId, {
      quantity: 6000,
      date: '2023-12-15',
    });
    const metal = await request.post('/api/portfolio/metals', {
      headers,
      data: { name: GOLD, metal: 0, fineWeight: 31.1, weightUnit: 'Gram' },
    });
    expect(metal.ok()).toBeTruthy();
    const metalId = ((await metal.json()) as { assetId: string }).assetId;
    const buy = await request.post(`/api/portfolio/assets/${metalId}/transactions`, {
      headers,
      data: {
        type: TYPE_BUY,
        quantity: 1,
        totalAmount: 6000,
        date: '2023-12-15',
        cashAssetId: bank.cashAssetId,
      },
    });
    expect(buy.ok()).toBeTruthy();

    await wealthTab(page, 'Metale szlachetne');
    await expect(page.getByRole('row').filter({ hasText: GOLD })).toBeVisible();
  });

  await test.step('Krok 3: the MAX chart starts on 2023-12-15 and gold is valued from the first day, never dropping to zero, with no band', async () => {
    await expectPointsProjected(request, token, (points) => {
      expect(points[0]?.date).toBe('2023-12-15');
      const goldDays = between(points, '2023-12-15', yesterday);
      expect(goldDays.length).toBeGreaterThan(0);
      for (const point of goldDays) {
        expect(point.netWorthPln, point.date).toBeGreaterThan(0);
        expect(point.isIncomplete, point.date).toBe(false);
      }
    });
    await expectProjected(
      () => openMaxChart(page),
      async () => {
        await expect(chart(page)).toHaveAccessibleName(/MAX/, { timeout: 2_000 });
      },
    );
    await expect(page.locator('.net-worth-chart__band')).toHaveCount(0);
  });

  await test.step('Krok 4: the Saturday 2024-01-06 point equals Friday 2024-01-05 and is not flagged incomplete', async () => {
    const points = await fetchPoints(request, token);
    const friday = pointOn(points, '2024-01-05');
    const saturday = pointOn(points, '2024-01-06');
    expect(saturday.netWorthPln).toBe(friday.netWorthPln);
    expect(saturday.isIncomplete).toBe(false);
  });

  await test.step('Krok 5: buying 5 VWCE.DE at 100 EUR dated 2021-03-15 is accepted without a missing-rate error', async () => {
    await depositCashThroughApi(request, token, broker.cashAssetId, {
      quantity: 2125,
      date: '2021-03-15',
    });
    const search = await request.get('/api/marketdata/instruments/search?q=VWCE&assetClass=Etf', {
      headers,
    });
    expect(search.ok()).toBeTruthy();
    const found = (
      (await search.json()) as { results: { id: string | null; ticker: string }[] }
    ).results.find((result) => result.ticker === 'VWCE.DE');
    expect(found?.id).toBeTruthy();

    // The first FX sync of the stack may not have finished yet, so a rejection is retried for a while.
    await expect(async () => {
      const created = await request.post('/api/portfolio/assets', {
        headers,
        data: {
          accountId: broker.id,
          assetClass: ASSET_CLASS_ETF,
          name: 'VWCE.DE',
          currency: 'EUR',
          instrumentId: found?.id,
          initialTransaction: { type: TYPE_BUY, quantity: 5, unitPrice: 100, date: '2021-03-15' },
        },
      });
      expect(created.ok(), await created.text()).toBeTruthy();
      vwceId = ((await created.json()) as { id: string }).id;
    }).toPass({ timeout: 90_000, intervals: [2_000] });
  });

  await test.step('Krok 7: the MAX chart reaches 2021-03-15 and the history before the gold purchase holds 5 VWCE.DE at the EUR rate of the day, with no band', async () => {
    await expectPointsProjected(request, token, (points) => {
      expect(points[0]?.date).toBe('2021-03-15');
      const etfOnly = between(points, '2021-03-15', '2023-12-14');
      expect(etfOnly.length).toBeGreaterThan(0);
      expect(new Set(etfOnly.map((point) => point.netWorthPln))).toEqual(new Set([2125]));
      expect(points.some((point) => point.isIncomplete)).toBe(false);
    });
    await expectProjected(
      () => openMaxChart(page),
      async () => {
        await expect(chart(page)).toHaveAccessibleName(/MAX/, { timeout: 2_000 });
      },
    );
    await expect(page.locator('.net-worth-chart__band')).toHaveCount(0);
  });

  await test.step('Krok 8: buying 3 VWCE.DE at 90 EUR dated 2018-05-10 is accepted', async () => {
    await depositCashThroughApi(request, token, broker.cashAssetId, {
      quantity: 1147.5,
      date: '2018-05-10',
    });
    const buy = await request.post(`/api/portfolio/assets/${vwceId}/transactions`, {
      headers,
      data: { type: TYPE_BUY, quantity: 3, unitPrice: 90, date: '2018-05-10' },
    });
    expect(buy.ok(), await buy.text()).toBeTruthy();
    await expectPointsProjected(request, token, (points) => {
      expect(points[0]?.date).toBe('2018-05-10');
    });
  });

  await test.step('Krok 11: the 1Y range shows no band and MAX is selectable again', async () => {
    await page.goto('/dashboard');
    const ranges = page.getByRole('radiogroup', { name: 'Zakres wykresu' });
    await ranges.getByRole('radio', { name: '1Y' }).click();
    await expect(ranges.getByRole('radio', { name: '1Y' })).toBeChecked();
    await expect(page.locator('.net-worth-chart__band')).toHaveCount(0);
    await ranges.getByRole('radio', { name: 'MAX' }).click();
    await expect(ranges.getByRole('radio', { name: 'MAX' })).toBeChecked();
  });

  await test.step('Krok 16: the Metale page works as before and shows the spot prices', async () => {
    await wealthTab(page, 'Metale szlachetne');
    await expect(page.getByRole('row').filter({ hasText: GOLD })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Ceny spot' })).toContainText(/\/\sg/);
  });
});
