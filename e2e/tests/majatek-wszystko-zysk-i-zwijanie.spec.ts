import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
  wealthTab,
} from './fixtures';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BONDS = 1;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const WRAPPER_IKE = 1;
const VARIANT_CURRENT = 0;
const ASSET_CLASS_ETF = 3;
const TYPE_BUY = 0;
const BOND_TYPE_COI = 4;
const BOND_TYPE_EDO = 5;

const STOCKS = 'Akcje i ETF';
const BONDS = 'Obligacje';
const METALS = 'Metale szlachetne';
const COST_STOCKS = 510_000;
const COST_BONDS = 1_500_000;
const COST_METALS = 200_000;

interface Gain {
  grosze: number;
  percent: number;
}

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function section(page: Page, name: string): Locator {
  return page.locator('tbody[data-section]').filter({
    has: page.locator('.wealth-all__section-name', { hasText: new RegExp(`^${name}$`) }),
  });
}

function toggle(page: Page, name: string): Locator {
  return section(page, name).locator('.wealth-all__toggle');
}

function sectionChange(page: Page, name: string): Locator {
  return section(page, name).locator('tr.wealth-all__section .wealth-all__change');
}

function row(page: Page, name: string): Locator {
  return page
    .locator('tr.wealth-all__row')
    .filter({ has: page.locator('.wealth-all__name', { hasText: name }) });
}

async function refetchWealth(page: Page): Promise<void> {
  await page.goto('/wealth');
  await expect(page.getByRole('heading', { name: 'Majątek', exact: true })).toBeVisible();
  await expect(page.getByTestId('total')).toBeVisible();
}

async function expand(page: Page, name: string): Promise<void> {
  await toggle(page, name).click();
  await expect(toggle(page, name)).toHaveAttribute('aria-expanded', 'true');
}

function parseGrosze(text: string): number {
  const match = /([+\-−])?\s*(\d[\d\s  ]*),(\d{2})\s*zł/.exec(text);
  expect(match, `money in "${text}"`).not.toBeNull();
  const digits = (match as RegExpExecArray)[2]!.replace(/\D/g, '');
  const value = Number(digits) * 100 + Number((match as RegExpExecArray)[3]);
  return (match as RegExpExecArray)[1] === '-' || (match as RegExpExecArray)[1] === '−'
    ? -value
    : value;
}

function parseGain(text: string): Gain {
  const percent = /\(([+\-−])?(\d[\d\s  ]*)(?:,(\d+))?\s*%\)/.exec(text);
  expect(percent, `percent in "${text}"`).not.toBeNull();
  const magnitude = Number(
    `${(percent as RegExpExecArray)[2]!.replace(/\D/g, '')}.${(percent as RegExpExecArray)[3] ?? '0'}`,
  );
  const negative =
    (percent as RegExpExecArray)[1] === '-' || (percent as RegExpExecArray)[1] === '−';
  return { grosze: parseGrosze(text), percent: negative ? -magnitude : magnitude };
}

async function gainOf(locator: Locator): Promise<Gain> {
  return parseGain((await locator.textContent()) ?? '');
}

async function valueOf(rowLocator: Locator): Promise<number> {
  return parseGrosze((await rowLocator.locator('.wealth-all__num app-money').textContent()) ?? '');
}

function textPattern(text: string): RegExp {
  const normalized = text.replace(/\s+/g, ' ').trim();
  return new RegExp(
    normalized
      .split(' ')
      .map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
      .join('\\s'),
  );
}

async function transferThroughApi(
  request: APIRequestContext,
  token: string,
  transfer: { sourceAssetId: string; targetAssetId: string; amount: number; date: string },
): Promise<void> {
  const response = await request.post('/api/portfolio/transfers', {
    headers: bearer(token),
    data: transfer,
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}

async function addBondThroughApi(
  request: APIRequestContext,
  token: string,
  bond: {
    accountId: string;
    name: string;
    type: number;
    bondCount: number;
    purchaseDate: string;
    firstPeriodRatePercent: number;
    marginPercent: number;
  },
): Promise<void> {
  const response = await request.post('/api/portfolio/bonds', {
    headers: bearer(token),
    data: {
      accountId: bond.accountId,
      name: bond.name,
      seriesCode: bond.name,
      type: bond.type,
      purchaseDate: bond.purchaseDate,
      bondCount: bond.bondCount,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: bond.firstPeriodRatePercent,
      marginPercent: bond.marginPercent,
      earlyRedemptionFeePerBond: 0,
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}

async function openEtfs(page: Page): Promise<void> {
  await wealthTab(page, STOCKS);
  await page.getByRole('tab', { name: 'ETF', exact: true }).click();
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

test('majątek → Wszystko: zysk przy aktywach, zwijane sekcje i szukanie', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  const headers = bearer(token);
  const sixMonthsAgo = 180;
  let mbank = { id: '', cashAssetId: '' };
  let ike = { id: '', cashAssetId: '' };
  let bondsAccount = { id: '', cashAssetId: '' };
  let totalText = '';

  await page.goto('/login');
  await logInThroughUi(page, user);

  await test.step('Krok 1: mBank with 60 000 zł deposited half a year ago shows the balance 60 000,00 zł', async () => {
    mbank = await createAccountThroughApi(request, token, {
      name: 'mBank',
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    } as Parameters<typeof createAccountThroughApi>[2]);
    await depositCashThroughApi(request, token, mbank.cashAssetId, {
      quantity: 60000,
      date: warsawDate(183),
    });
    await wealthTab(page, 'Konta bankowe');
    await expect(accountRow(page, 'mBank')).toContainText(pln('60 000,00'));
  });

  await test.step('Krok 2: 20 000 zł moved to IKE eMakler leaves 20 000,00 zł there and 40 000,00 zł on mBank', async () => {
    ike = await createAccountThroughApi(request, token, {
      name: 'IKE eMakler',
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_IKE,
    });
    await transferThroughApi(request, token, {
      sourceAssetId: mbank.cashAssetId,
      targetAssetId: ike.cashAssetId,
      amount: 20000,
      date: warsawDate(181),
    });
    await wealthTab(page, 'Rachunki inwestycyjne');
    await expect(accountRow(page, 'IKE eMakler')).toContainText(pln('20 000,00'));
    await wealthTab(page, 'Konta bankowe');
    await expect(accountRow(page, 'mBank')).toContainText(pln('40 000,00'));
  });

  await test.step('Krok 3: 10 VWCE.DE at 120 EUR bought 3 months ago appear in Akcje i ETF', async () => {
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
          accountId: ike.id,
          assetClass: ASSET_CLASS_ETF,
          name: 'VWCE.DE',
          currency: 'EUR',
          instrumentId: found?.id,
          initialTransaction: {
            type: TYPE_BUY,
            quantity: 10,
            unitPrice: 120,
            date: warsawDate(90),
          },
        },
      });
      expect(created.ok(), await created.text()).toBeTruthy();
    }).toPass({ timeout: 120_000, intervals: [2_000] });
    await openEtfs(page);
    await expect(page.getByRole('row').filter({ hasText: 'VWCE.DE' })).toBeVisible();
  });

  await test.step('Krok 4: Obligacje account funded with 15 000 zł holds 100 EDO and 50 COI bought half a year ago', async () => {
    bondsAccount = await createAccountThroughApi(request, token, {
      name: 'Obligacje',
      type: ACCOUNT_TYPE_BONDS,
      wrapper: WRAPPER_REGULAR,
    });
    await transferThroughApi(request, token, {
      sourceAssetId: mbank.cashAssetId,
      targetAssetId: bondsAccount.cashAssetId,
      amount: 15000,
      date: warsawDate(181),
    });
    await addBondThroughApi(request, token, {
      accountId: bondsAccount.id,
      name: 'EDO0525',
      type: BOND_TYPE_EDO,
      bondCount: 100,
      purchaseDate: warsawDate(sixMonthsAgo),
      firstPeriodRatePercent: 5.6,
      marginPercent: 2,
    });
    await addBondThroughApi(request, token, {
      accountId: bondsAccount.id,
      name: 'COI0525',
      type: BOND_TYPE_COI,
      bondCount: 50,
      purchaseDate: warsawDate(sixMonthsAgo),
      firstPeriodRatePercent: 5,
      marginPercent: 1.5,
    });
    await wealthTab(page, BONDS);
    await expect(page.getByRole('row').filter({ hasText: 'EDO0525' })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: 'COI0525' })).toBeVisible();
  });

  await test.step('Krok 5: 10 g of gold bought for 2 000 zł a month ago from the Portfel cash place is on the Metale list', async () => {
    const place = await request.post('/api/portfolio/cash-places', {
      headers,
      data: { name: 'Portfel', currencies: ['PLN'] },
    });
    expect(place.ok()).toBeTruthy();
    const walletAssetId = ((await place.json()) as { balances: { cashAssetId: string }[] })
      .balances[0]?.cashAssetId as string;
    await depositCashThroughApi(request, token, walletAssetId, {
      quantity: 5000,
      date: warsawDate(35),
    });
    const metal = await request.post('/api/portfolio/metals', {
      headers,
      data: { name: 'Złoto 10 g', metal: 0, fineWeight: 10, weightUnit: 'Gram' },
    });
    expect(metal.ok(), await metal.text()).toBeTruthy();
    const metalId = ((await metal.json()) as { assetId: string }).assetId;
    const buy = await request.post(`/api/portfolio/assets/${metalId}/transactions`, {
      headers,
      data: {
        type: TYPE_BUY,
        quantity: 1,
        totalAmount: 2000,
        date: warsawDate(30),
        cashAssetId: walletAssetId,
      },
    });
    expect(buy.ok(), await buy.text()).toBeTruthy();
    await wealthTab(page, METALS);
    await expect(page.getByRole('row').filter({ hasText: 'Złoto 10 g' })).toBeVisible();
  });

  await test.step('Przygotowanie 4: the sync button on Ustawienia fetches the prices', async () => {
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Ustawienia', exact: true })
      .click();
    const button = page.getByRole('button', { name: 'Synchronizuj teraz' });
    await button.click();
    await expect(button).toBeEnabled();
  });

  await test.step('Krok 6: Wszystko shows only collapsed section headers with sums, gain on Akcje i ETF, Obligacje and Metale only', async () => {
    await expectProjected(
      () => refetchWealth(page),
      async () => {
        await expect(section(page, STOCKS)).toContainText(pln('4 250,00'), { timeout: 3_000 });
        await expect(section(page, METALS)).toContainText(pln('12 000,00'), { timeout: 3_000 });
        for (const name of [STOCKS, BONDS, METALS]) {
          await expect(sectionChange(page, name)).toContainText(/zł/, { timeout: 3_000 });
        }
        await expect(page.getByTestId('total-gain')).toContainText(/zł/, { timeout: 3_000 });
      },
    );
    for (const name of [
      'Rachunki inwestycyjne',
      'Konta bankowe',
      'Gotówka',
      STOCKS,
      BONDS,
      METALS,
    ]) {
      await expect(toggle(page, name)).toHaveAttribute('aria-expanded', 'false');
    }
    await expect(page.locator('tr.wealth-all__row:visible')).toHaveCount(0);
    for (const name of ['Rachunki inwestycyjne', 'Konta bankowe', 'Gotówka']) {
      await expect(sectionChange(page, name)).toHaveCount(0);
    }
    await expect(sectionChange(page, STOCKS)).toContainText(/[-−]850,00\szł/);
  });

  await test.step('Krok 7: the total gain is the sum of the three section gains and its percent is that gain over their costs', async () => {
    const stocks = await gainOf(sectionChange(page, STOCKS));
    const bonds = await gainOf(sectionChange(page, BONDS));
    const metals = await gainOf(sectionChange(page, METALS));
    const total = await gainOf(page.getByTestId('total-gain'));
    expect(total.grosze).toBe(stocks.grosze + bonds.grosze + metals.grosze);
    const expectedPercent = (total.grosze * 100) / (COST_STOCKS + COST_BONDS + COST_METALS);
    expect(Math.abs(total.percent - expectedPercent)).toBeLessThan(0.02);
  });

  await test.step('Krok 8: clicking the Obligacje header expands and collapses only that section', async () => {
    await toggle(page, BONDS).click();
    await expect(toggle(page, BONDS)).toHaveAttribute('aria-expanded', 'true');
    for (const name of ['Rachunki inwestycyjne', 'Konta bankowe', 'Gotówka', STOCKS, METALS]) {
      await expect(toggle(page, name)).toHaveAttribute('aria-expanded', 'false');
    }
    await toggle(page, BONDS).click();
    await expect(toggle(page, BONDS)).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator('tr.wealth-all__row:visible')).toHaveCount(0);
  });

  await test.step('Krok 9: expanded Akcje i ETF shows VWCE.DE with value 4 250,00 zł and the loss under it without an icon, percent next to the amount', async () => {
    await expand(page, STOCKS);
    const vwce = row(page, 'VWCE.DE');
    await expect(vwce.locator('.wealth-all__num app-money')).toContainText(pln('4 250,00'));
    const change = vwce.locator('.wealth-all__change');
    await expect(change).toContainText(/[-−]850,00\szł/);
    await expect(change.locator('.gain__percent')).toContainText(/[-−]16,67/);
    await expect(change.locator('.gain--negative')).toBeVisible();
    await expect(change.locator('mat-icon')).toHaveCount(0);
  });

  await test.step('Krok 10: expanded Obligacje shows EDO and COI valued above the cost with the gain over the cost', async () => {
    await expand(page, BONDS);
    for (const [name, cost] of [
      ['EDO0525', 1_000_000],
      ['COI0525', 500_000],
    ] as const) {
      const value = await valueOf(row(page, name));
      const gain = await gainOf(row(page, name).locator('.wealth-all__change'));
      expect(value, name).toBeGreaterThan(cost);
      expect(gain.grosze, name).toBe(value - cost);
      expect(Math.abs(gain.percent - (gain.grosze * 100) / cost), name).toBeLessThan(0.01);
    }
  });

  await test.step('Krok 11: expanded Metale shows gold worth 12 000,00 zł with the gain +10 000,00 zł', async () => {
    await expand(page, METALS);
    const gold = row(page, 'Złoto');
    await expect(gold.locator('.wealth-all__num app-money')).toContainText(pln('12 000,00'));
    const gain = await gainOf(gold.locator('.wealth-all__change'));
    expect(gain.grosze).toBe(1_000_000);
    expect(Math.abs(gain.percent - 500)).toBeLessThan(0.01);
  });

  await test.step('Krok 12: an EDO bought today shows the gain 0,00 zł (0,00 %) without a sign', async () => {
    await transferThroughApi(request, token, {
      sourceAssetId: mbank.cashAssetId,
      targetAssetId: bondsAccount.cashAssetId,
      amount: 1000,
      date: warsawDate(),
    });
    await addBondThroughApi(request, token, {
      accountId: bondsAccount.id,
      name: 'EDO0226',
      type: BOND_TYPE_EDO,
      bondCount: 10,
      purchaseDate: warsawDate(),
      firstPeriodRatePercent: 5.6,
      marginPercent: 2,
    });
    await expectProjected(
      async () => {
        await refetchWealth(page);
        await expand(page, BONDS);
      },
      async () => {
        await expect(row(page, 'EDO0226').locator('.wealth-all__change')).toHaveText(
          /^\s*0,00\szł\s*\(0(?:,00)?\s%\)\s*$/,
          { timeout: 3_000 },
        );
      },
    );
  });

  await test.step('Krok 13: searching VWCE leaves only the expanded Akcje i ETF section and the total and gain stay', async () => {
    await expectProjected(
      () => refetchWealth(page),
      async () => {
        await expect(section(page, STOCKS)).toContainText(pln('4 250,00'), { timeout: 3_000 });
        await expect(section(page, METALS)).toContainText(pln('12 000,00'), { timeout: 3_000 });
        await expect(page.getByTestId('total-gain')).toContainText(/zł/, { timeout: 3_000 });
        await expect(section(page, BONDS)).toContainText(/zł/, { timeout: 3_000 });
      },
    );
    totalText = ((await page.getByTestId('total').textContent()) ?? '').trim();
    const totalGainText = ((await page.getByTestId('total-gain').textContent()) ?? '').trim();
    await page.getByLabel('Szukaj').fill('VWCE');
    await expect(page.locator('tbody[data-section]')).toHaveCount(1);
    await expect(toggle(page, STOCKS)).toHaveAttribute('aria-expanded', 'true');
    await expect(row(page, 'VWCE.DE')).toBeVisible();
    await expect(page.getByTestId('total')).toHaveText(totalText);
    await expect(page.getByTestId('total-gain')).toHaveText(totalGainText);
  });

  await test.step('Krok 14: collapsing Akcje i ETF by hand keeps it collapsed until the search text changes', async () => {
    await toggle(page, STOCKS).click();
    await expect(toggle(page, STOCKS)).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByLabel('Szukaj')).toHaveValue('VWCE');
    await page.getByLabel('Szukaj').fill('VWC');
    await expect(toggle(page, STOCKS)).toHaveAttribute('aria-expanded', 'true');
  });

  await test.step('Krok 15: clearing the search brings every section back collapsed', async () => {
    await page.getByLabel('Szukaj').fill('');
    await expect(page.locator('tbody[data-section]')).toHaveCount(6);
    await expect(page.locator('.wealth-all__toggle[aria-expanded="true"]')).toHaveCount(0);
  });

  await test.step('Krok 16: after a reload the sections are collapsed again', async () => {
    await expand(page, BONDS);
    await page.reload();
    await expect(page.locator('tbody[data-section]')).toHaveCount(6);
    await expect(page.locator('.wealth-all__toggle[aria-expanded="true"]')).toHaveCount(0);
  });

  await test.step('Krok 17: the Akcje i ETF table shows the VWCE.DE gain without an icon and with the same percent', async () => {
    await openEtfs(page);
    const cell = page.getByRole('row').filter({ hasText: 'VWCE.DE' }).locator('.mat-column-gain');
    await expect(cell).toContainText(/[-−]850,00\szł/);
    await expect(cell.locator('.gain__percent')).toContainText(/[-−]16,67/);
    await expect(cell.locator('mat-icon')).toHaveCount(0);
  });

  await test.step('Krok 18: the VWCE.DE transactions tile Zysk/strata shows the same amount and percent without an icon', async () => {
    await page.getByRole('link', { name: 'VWCE.DE', exact: true }).click();
    const tile = page.locator('.transactions-page__kpi').filter({ hasText: 'Zysk/strata' });
    await expect(tile).toContainText(/[-−]850,00\szł/);
    await expect(tile.locator('.gain__percent')).toContainText(/[-−]16,67/);
    await expect(tile.locator('mat-icon')).toHaveCount(0);
  });

  await test.step('Krok 19: the Pulpit net worth equals the Wszystko total and the bonds structure line includes the accrued interest', async () => {
    await expectProjected(
      async () => {
        await page.goto('/dashboard');
      },
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(textPattern(totalText), {
          timeout: 3_000,
        });
        const bonds = page
          .locator('.dashboard-page__structure li')
          .filter({ has: page.getByRole('link', { name: BONDS, exact: true }) });
        expect(parseGrosze((await bonds.textContent()) ?? '')).toBeGreaterThan(1_600_000);
      },
    );
  });

  await test.step('Krok 20: the 1Y history ends today at the net worth sum (the gradual growth of the bonds is only manual)', async () => {
    await page
      .getByRole('radiogroup', { name: 'Zakres wykresu' })
      .getByRole('radio', { name: '1Y' })
      .click();
    let last = 0;
    await expectProjected(
      async () => {
        const response = await request.get('/api/reporting/net-worth-history?range=1Y', {
          headers,
        });
        expect(response.ok()).toBeTruthy();
        const points = ((await response.json()) as { points: { netWorthPln: number }[] }).points;
        last = Math.round((points.at(-1)?.netWorthPln ?? 0) * 100);
      },
      async () => {
        expect(Math.abs(last - parseGrosze(totalText))).toBeLessThanOrEqual(1);
      },
      { timeout: 60_000 },
    );
  });
});
