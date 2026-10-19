import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
} from './fixtures';
import { ASSET_CLASS, expectClassesProjected } from './cash-class';
import { expectProjected } from './projection';

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

const TICKER = 'CDR.WA';
const SERIES = 'OTS0127';
const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BONDS = 1;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_STOCK = 2;
const TYPE_BUY = 0;
const BOND_TYPE_OTS = 0;

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

async function createStockThroughApi(
  request: APIRequestContext,
  token: string,
  accountId: string,
): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const search = await request.get('/api/marketdata/instruments/search?q=CDR&assetClass=Stock', {
    headers,
  });
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === TICKER);
  expect(found?.id).toBeTruthy();

  const created = await request.post('/api/portfolio/assets', {
    headers,
    data: {
      accountId,
      assetClass: ASSET_CLASS_STOCK,
      name: TICKER,
      currency: 'PLN',
      instrumentId: found?.id,
      initialTransaction: { type: TYPE_BUY, quantity: 10, unitPrice: 100, date: warsawDate() },
    },
  });
  expect(created.ok()).toBeTruthy();
}

async function createBondThroughApi(
  request: APIRequestContext,
  token: string,
  accountId: string,
): Promise<void> {
  const created = await request.post('/api/portfolio/bonds', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      accountId,
      name: SERIES,
      seriesCode: SERIES,
      type: BOND_TYPE_OTS,
      purchaseDate: warsawDate(),
      bondCount: 10,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 2,
      earlyRedemptionFeePerBond: 0,
    },
  });
  expect(created.ok()).toBeTruthy();
}

function legendItem(page: Page, label: string): Locator {
  return page.getByRole('button', { name: new RegExp(`^${label} — `) });
}

function chip(page: Page, name: string): Locator {
  return page.getByRole('listbox', { name: 'Klasa aktywów' }).getByRole('option', { name });
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

// A user whose holdings were all bought today has a one-day history, which the chart answers with a message instead of a drawing.
function historyChart(page: Page): Locator {
  return page
    .getByRole('img', { name: /^Wykres historii wartości/ })
    .or(page.getByText('Za mało historii, aby narysować wykres.'));
}

test('dashboard: net worth, allocation legend, asset-class chips, chart periods and the PL to EN language switch', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(240_000);
  const token = await logInThroughApi(request, user);
  const bonds = await createAccountThroughApi(request, token, {
    name: 'Obligacje',
    type: ACCOUNT_TYPE_BONDS,
    wrapper: WRAPPER_REGULAR,
  });
  await depositCashThroughApi(request, token, bonds.cashAssetId, {
    quantity: 2000,
    date: warsawDate(),
  });
  await expectClassesProjected(request, token, { [ASSET_CLASS.bond]: 2000 });
  await createBondThroughApi(request, token, bonds.id);
  const brokerage = await createAccountThroughApi(request, token, {
    name: 'XTB',
    type: ACCOUNT_TYPE_BROKERAGE,
    wrapper: WRAPPER_REGULAR,
  });
  await depositCashThroughApi(request, token, brokerage.cashAssetId, {
    quantity: 1000,
    date: warsawDate(),
  });
  await expectClassesProjected(request, token, {
    [ASSET_CLASS.bond]: 2000,
    [ASSET_CLASS.cash]: 1000,
  });
  await createStockThroughApi(request, token, brokerage.id);
  await expectClassesProjected(request, token, {
    [ASSET_CLASS.bond]: 2000,
    [ASSET_CLASS.stock]: 1000,
    [ASSET_CLASS.cash]: 0,
  });

  await page.goto('/login');
  await logInThroughUi(page, user);

  const netWorth = page.getByTestId('net-worth');
  const heading = page.getByRole('heading', { level: 1 });

  async function openDashboard(): Promise<void> {
    await navigation(page).getByRole('link', { name: 'Pulpit', exact: true }).click();
    await expect(heading).toHaveText('Pulpit');
  }

  await test.step('Krok 1: after Synchronizuj teraz the last synchronization status is refreshed', async () => {
    await navigation(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Ustawienia', level: 1 })).toBeVisible();
    await page.getByRole('button', { name: 'Synchronizuj teraz' }).click();
    await expect(page.getByText(/Rozpoczęto/).first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('Synchronizacja jeszcze się nie odbyła.')).toBeHidden();
  });

  await test.step('Krok 2: the dashboard shows the net worth 3 000,00 zł', async () => {
    await expectProjected(openDashboard, async () => {
      await expect(netWorth).toContainText(pln('3 000,00'), { timeout: 2_000 });
    });
  });

  await test.step('Krok 3: the allocation legend shows Obligacje at 2 000,00 zł (the bond and its free cash) and Akcje at 1 000,00 zł and nothing is highlighted', async () => {
    await expect(legendItem(page, 'Obligacje')).toContainText(pln('2 000,00'));
    await expect(legendItem(page, 'Akcje')).toContainText(pln('1 000,00'));
    for (const label of ['Obligacje', 'Akcje']) {
      await expect(legendItem(page, label)).toHaveAttribute('aria-pressed', 'false');
    }
  });

  await test.step('Krok 4: the Akcje chip filters the chart and highlights Akcje while the net worth stays 3 000,00 zł', async () => {
    await chip(page, 'Akcje').click();
    await expect(chip(page, 'Akcje')).toHaveAttribute('aria-selected', 'true');
    await expect(legendItem(page, 'Akcje')).toHaveAttribute('aria-pressed', 'true');
    await expect(legendItem(page, 'Obligacje')).toHaveAttribute('aria-pressed', 'false');
    await expect(historyChart(page)).toBeVisible();
    await expect(netWorth).toContainText(pln('3 000,00'));
  });

  await test.step('Krok 5: the Wszystko chip clears the highlight', async () => {
    await chip(page, 'Wszystko').click();
    await expect(chip(page, 'Wszystko')).toHaveAttribute('aria-selected', 'true');
    for (const label of ['Obligacje', 'Akcje']) {
      await expect(legendItem(page, label)).toHaveAttribute('aria-pressed', 'false');
    }
  });

  await test.step('Krok 6: the 1Y range is active and the history chart is visible', async () => {
    await rangeToggle(page, '1Y').click();
    await expect(rangeToggle(page, '1Y')).toBeChecked();
    await expect(historyChart(page)).toBeVisible();
  });

  await test.step('Krok 7: the MAX range is active and the history chart is visible', async () => {
    await rangeToggle(page, 'MAX').click();
    await expect(rangeToggle(page, 'MAX')).toBeChecked();
    await expect(historyChart(page)).toBeVisible();
  });

  await test.step('Krok 8: switching to English shows Dashboard, Net worth and PLN 3,000.00', async () => {
    await page.getByRole('button', { name: 'Język' }).click();
    await page.getByRole('menuitemradio', { name: 'English' }).click();
    await expect(heading).toHaveText('Dashboard');
    await expect(page.getByText('Net worth', { exact: true })).toBeVisible();
    await expect(netWorth).toContainText(/PLN\s?3,000\.00/);
  });

  await test.step('Krok 9: switching back to Polski shows Pulpit, Wartość majątku and 3 000,00 zł', async () => {
    await page.getByRole('button', { name: 'Language' }).click();
    await page.getByRole('menuitemradio', { name: 'Polski' }).click();
    await expect(heading).toHaveText('Pulpit');
    await expect(page.getByText('Wartość majątku', { exact: true })).toBeVisible();
    await expect(netWorth).toContainText(pln('3 000,00'));
  });
});
