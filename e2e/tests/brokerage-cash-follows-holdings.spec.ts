import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
  openSelect,
  wealthTab,
} from './fixtures';
import {
  ASSET_CLASS,
  ASSET_CLASS_BANK_ACCOUNT,
  between,
  expectAllEqual,
  expectClassesProjected,
  fetchHistory,
  type HistoryPoint,
} from './cash-class';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_STOCK = 2;
const ASSET_CLASS_ETF = 3;
const TYPE_BUY = 0;
const TYPE_SELL = 1;
const VARIANT_CURRENT = 0;

const BROKER = 'Broker';
const BONDS = 'Obligacje';
const EMPTY = 'Pusty';

function shiftDay(date: string, days: number): string {
  const shifted = new Date(`${date}T00:00:00Z`);
  shifted.setUTCDate(shifted.getUTCDate() + days);
  return shifted.toISOString().slice(0, 10);
}

// The stub quotes only weekdays, so a purchase on a weekend would have no price until Monday.
function tradingDayOnOrBefore(date: string, weekdays: number[]): string {
  let day = date;
  while (!weekdays.includes(new Date(`${day}T00:00:00Z`).getUTCDay())) {
    day = shiftDay(day, -1);
  }
  return day;
}

const D90 = warsawDate(90);
const D60 = tradingDayOnOrBefore(warsawDate(60), [1, 2, 3, 4, 5]);
const D61 = shiftDay(D60, -1);
const D30 = tradingDayOnOrBefore(warsawDate(30), [2, 3, 4, 5]);
const D31 = shiftDay(D30, -1);
const TODAY = warsawDate();
const YESTERDAY = warsawDate(1);

function digits(amount: string): string {
  return amount.replace(/ /g, '\\s?');
}

function exactPln(amount: string): RegExp {
  return new RegExp(`^\\s*${digits(amount)}\\szł\\s*$`);
}

function legendPln(amount: string): RegExp {
  return new RegExp(`—\\s${digits(amount)}\\szł`);
}

function pln(amount: string): RegExp {
  return new RegExp(`${digits(amount)}\\szł`);
}

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function legendItem(page: Page, label: string): Locator {
  return page.getByRole('button', { name: new RegExp(`^${label} — `) });
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

async function openDashboard(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ustawienia', level: 1 })).toBeVisible();
  await navigation(page).getByRole('link', { name: 'Pulpit', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pulpit', level: 1 })).toBeVisible();
}

async function openAccounts(page: Page): Promise<void> {
  await wealthTab(page, 'Rachunki inwestycyjne');
  await expect(
    page.getByRole('heading', { name: 'Rachunki inwestycyjne', level: 1 }),
  ).toBeVisible();
}

async function syncPrices(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ustawienia', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Synchronizuj teraz' }).click();
  await expect(page.getByText(/Rozpoczęto/).first()).toBeVisible({ timeout: 30_000 });
}

async function expectLegend(page: Page, expected: Record<string, string | null>): Promise<void> {
  await expectProjected(
    () => openDashboard(page),
    async () => {
      for (const [label, amount] of Object.entries(expected)) {
        const entry = legendItem(page, label);
        if (amount === null) {
          if ((await entry.count()) > 0) {
            await expect(entry).toContainText(legendPln('0,00'), { timeout: 1_000 });
          }
        } else {
          await expect(entry).toContainText(legendPln(amount), { timeout: 1_000 });
        }
      }
    },
  );
}

async function expectNetWorth(page: Page, amount: string): Promise<void> {
  await expect(page.getByTestId('net-worth')).toContainText(pln(amount));
}

async function createAccountThroughUi(page: Page, name: string, type: string): Promise<void> {
  await openAccounts(page);
  await page
    .getByRole('button', { name: /^(Nowy rachunek|Utwórz pierwszy rachunek)$/ })
    .first()
    .click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(name);
  await openSelect(dialog.getByRole('combobox', { name: 'Typ' }));
  await page.getByRole('option', { name: type, exact: true }).click();
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
}

async function accountIds(
  request: APIRequestContext,
  token: string,
): Promise<Map<string, { id: string; cashAssetId: string }>> {
  const response = await request.get('/api/portfolio/accounts', {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(response.ok()).toBeTruthy();
  const accounts = (await response.json()) as { id: string; name: string; cashAssetId: string }[];
  return new Map(accounts.map((account) => [account.name, account]));
}

async function expectAccountCash(page: Page, name: string, amount: string): Promise<void> {
  await openAccounts(page);
  await accountRow(page, name).getByRole('link', { name, exact: true }).click();
  await expect(page.getByTestId('account-cash')).toHaveText(exactPln(amount));
}

async function expectAccountRowAmounts(
  page: Page,
  name: string,
  amounts: { cash: string; value: string },
): Promise<void> {
  await expectProjected(
    () => openAccounts(page),
    async () => {
      const cells = accountRow(page, name).locator('td');
      await expect(cells.nth(3)).toHaveText(exactPln(amounts.cash), { timeout: 2_000 });
      await expect(cells.nth(5)).toHaveText(exactPln(amounts.value), { timeout: 2_000 });
    },
  );
}

async function findInstrumentId(
  request: APIRequestContext,
  token: string,
  security: { query: string; ticker: string; assetClassName: string },
): Promise<string> {
  const search = await request.get(
    `/api/marketdata/instruments/search?q=${security.query}&assetClass=${security.assetClassName}`,
    { headers: { Authorization: `Bearer ${token}` } },
  );
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === security.ticker);
  expect(found?.id).toBeTruthy();
  return found?.id as string;
}

// The first buy freezes the FX rate of its day, which MarketData serves only after its first FX sync.
async function buySecurityThroughApi(
  request: APIRequestContext,
  token: string,
  security: {
    accountId: string;
    ticker: string;
    query: string;
    assetClass: number;
    assetClassName: string;
    currency: string;
    quantity: number;
    unitPrice: number;
    date: string;
  },
): Promise<string> {
  const instrumentId = await findInstrumentId(request, token, security);
  let assetId = '';
  await expect(async () => {
    const created = await request.post('/api/portfolio/assets', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        accountId: security.accountId,
        assetClass: security.assetClass,
        name: security.ticker,
        currency: security.currency,
        instrumentId,
        initialTransaction: {
          type: TYPE_BUY,
          quantity: security.quantity,
          unitPrice: security.unitPrice,
          date: security.date,
        },
      },
    });
    expect(created.ok()).toBeTruthy();
    assetId = ((await created.json()) as { id: string }).id;
  }).toPass({ timeout: 90_000, intervals: [2_000] });
  return assetId;
}

async function recordTradeThroughApi(
  request: APIRequestContext,
  token: string,
  assetId: string,
  trade: { type: number; quantity: number; unitPrice: number; date: string },
): Promise<string> {
  let transactionId = '';
  await expect(async () => {
    const response = await request.post(`/api/portfolio/assets/${assetId}/transactions`, {
      headers: { Authorization: `Bearer ${token}` },
      data: trade,
    });
    expect(response.ok()).toBeTruthy();
    transactionId = ((await response.json()) as { id: string }).id;
  }).toPass({ timeout: 90_000, intervals: [2_000] });
  return transactionId;
}

async function expectHistoryProjected(
  request: APIRequestContext,
  token: string,
  assetClass: number,
  assertion: (points: HistoryPoint[]) => void,
): Promise<void> {
  let points: HistoryPoint[] = [];
  await expectProjected(
    async () => {
      points = await fetchHistory(request, token, assetClass);
    },
    async () => {
      assertion(points);
    },
    { timeout: 90_000 },
  );
}

test('brokerage cash follows holdings: the free cash of a brokerage account counts under the class of the latest held purchase', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(1_800_000);

  await test.step('Przygotowanie 3: a fresh account', async () => {
    await logInThroughApi(request, user);
    await page.goto('/login');
    await logInThroughUi(page, user);
  });

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;

  let broker = { id: '', cashAssetId: '' };
  let bonds = { id: '', cashAssetId: '' };
  let cdrId = '';
  let vwceId = '';
  let secondCdrBuyId = '';

  await test.step('Krok 1: the brokerage account Broker appears on the list with 0,00 zł', async () => {
    await createAccountThroughUi(page, BROKER, 'Maklerski');
    await expect(accountRow(page, BROKER)).toContainText(pln('0,00'));
    broker = (await accountIds(request, token)).get(BROKER) ?? broker;
    expect(broker.id).not.toBe('');
  });

  await test.step('Krok 2: depositing 10 000 zł 90 days ago gives the free cash 10 000,00 zł and the whole Broker in Gotówka', async () => {
    await depositCashThroughApi(request, token, broker.cashAssetId, {
      quantity: 10000,
      date: D90,
    });
    await expectClassesProjected(request, token, { [ASSET_CLASS.cash]: 10000 });
    await expectAccountCash(page, BROKER, '10 000,00');
    await expectLegend(page, { Gotówka: '10 000,00' });
  });

  await test.step('Krok 3: the bonds account Obligacje with 5 000 zł puts them in Obligacje, not in Gotówka, and shows the free cash 5 000,00 zł', async () => {
    await createAccountThroughUi(page, BONDS, 'Obligacji');
    bonds = (await accountIds(request, token)).get(BONDS) ?? bonds;
    expect(bonds.id).not.toBe('');
    await depositCashThroughApi(request, token, bonds.cashAssetId, {
      quantity: 5000,
      date: D90,
    });
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.cash]: 10000,
      [ASSET_CLASS.bond]: 5000,
    });
    await expectLegend(page, { Obligacje: '5 000,00', Gotówka: '10 000,00' });
    await expectAccountCash(page, BONDS, '5 000,00');
  });

  await test.step('Krok 4: buying 20 CDR.WA 60 days ago lowers the free cash to 8 000,00 zł and counts it under Akcje', async () => {
    cdrId = await buySecurityThroughApi(request, token, {
      accountId: broker.id,
      ticker: 'CDR.WA',
      query: 'CDR',
      assetClass: ASSET_CLASS_STOCK,
      assetClassName: 'Stock',
      currency: 'PLN',
      quantity: 20,
      unitPrice: 100,
      date: D60,
    });
    await syncPrices(page);
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.stock]: 10000,
      [ASSET_CLASS.cash]: 0,
      [ASSET_CLASS.bond]: 5000,
    });
    await expectAccountCash(page, BROKER, '8 000,00');
    await expectLegend(page, { Akcje: '10 000,00', Gotówka: null, Obligacje: '5 000,00' });
  });

  await test.step('Krok 5: buying 18 VWCE.DE 30 days ago leaves the free cash 350,00 zł, counted under ETF and not under Gotówka', async () => {
    await expectAccountRowAmounts(page, BROKER, { cash: '8 000,00', value: '10 000,00' });
    vwceId = await buySecurityThroughApi(request, token, {
      accountId: broker.id,
      ticker: 'VWCE.DE',
      query: 'VWCE',
      assetClass: ASSET_CLASS_ETF,
      assetClassName: 'Etf',
      currency: 'EUR',
      quantity: 18,
      unitPrice: 100,
      date: D30,
    });
    await syncPrices(page);
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.etf]: 8000,
      [ASSET_CLASS.stock]: 2000,
      [ASSET_CLASS.cash]: 0,
    });
    await expectAccountCash(page, BROKER, '350,00');
    await expectLegend(page, { ETF: '8 000,00', Akcje: '2 000,00', Gotówka: null });
  });

  await test.step('Krok 6: the value of Broker and the net worth are unchanged by the ETF purchase', async () => {
    await expectAccountRowAmounts(page, BROKER, { cash: '350,00', value: '10 000,00' });
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expectNetWorth(page, '15 000,00');
      },
    );
  });

  await test.step('Krok 7: the history with the ETF class has 350 zł more than the position from the purchase date and nothing before it', async () => {
    await openDashboard(page);
    await page
      .getByRole('listbox', { name: 'Klasa aktywów' })
      .getByRole('option', { name: 'ETF' })
      .click();
    await expect(
      page.getByRole('listbox', { name: 'Klasa aktywów' }).getByRole('option', { name: 'ETF' }),
    ).toHaveAttribute('aria-selected', 'true');
    await expectHistoryProjected(request, token, ASSET_CLASS.etf, (points) => {
      expectAllEqual(between(points, D90, D31), 0, 'ETF before the purchase');
      expectAllEqual(between(points, D30, YESTERDAY), 8000, 'ETF from the purchase');
    });
  });

  await test.step('Krok 8: the history with the Gotówka class holds the free cash from the deposit to the CDR.WA purchase and then drops to 0', async () => {
    await expectHistoryProjected(request, token, ASSET_CLASS.cash, (points) => {
      expectAllEqual(between(points, D90, D61), 10000, 'Gotówka from the deposit');
      expectAllEqual(between(points, D60, YESTERDAY), 0, 'Gotówka from the CDR.WA purchase');
    });
  });

  await test.step('Krok 9: buying one more CDR.WA today moves the free cash to Akcje and leaves only the position in ETF', async () => {
    secondCdrBuyId = await recordTradeThroughApi(request, token, cdrId, {
      type: TYPE_BUY,
      quantity: 1,
      unitPrice: 100,
      date: TODAY,
    });
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.stock]: 2350,
      [ASSET_CLASS.etf]: 7650,
      [ASSET_CLASS.cash]: 0,
    });
    await expectLegend(page, { Akcje: '2 350,00', ETF: '7 650,00' });
    await expectAccountCash(page, BROKER, '250,00');
  });

  await test.step('Krok 10: moving that purchase to the day before the ETF purchase returns the free cash to ETF and recalculates the history', async () => {
    const moved = await request.put(
      `/api/portfolio/assets/${cdrId}/transactions/${secondCdrBuyId}`,
      {
        headers: { Authorization: `Bearer ${token}` },
        data: { type: TYPE_BUY, quantity: 1, unitPrice: 100, date: D31 },
      },
    );
    expect(moved.ok()).toBeTruthy();
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.etf]: 7900,
      [ASSET_CLASS.stock]: 2100,
    });
    await expectLegend(page, { ETF: '7 900,00', Akcje: '2 100,00' });
    await expectHistoryProjected(request, token, ASSET_CLASS.etf, (points) => {
      expectAllEqual(between(points, D30, YESTERDAY), 7900, 'ETF after the date change');
    });
    await expectHistoryProjected(request, token, ASSET_CLASS.stock, (points) => {
      expectAllEqual(between(points, D60, D31), 10000, 'Akcje until the changed date');
      expectAllEqual(between(points, D30, YESTERDAY), 2100, 'Akcje after the ETF purchase');
    });
  });

  await test.step('Krok 11: selling the whole CDR.WA keeps the free cash in ETF', async () => {
    await recordTradeThroughApi(request, token, cdrId, {
      type: TYPE_SELL,
      quantity: 21,
      unitPrice: 100,
      date: TODAY,
    });
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.etf]: 10000,
      [ASSET_CLASS.stock]: 0,
      [ASSET_CLASS.cash]: 0,
    });
    await expectLegend(page, { ETF: '10 000,00', Akcje: null, Gotówka: null });
  });

  await test.step('Krok 12: selling the whole VWCE.DE returns the free cash to Gotówka in the current class split', async () => {
    await recordTradeThroughApi(request, token, vwceId, {
      type: TYPE_SELL,
      quantity: 18,
      unitPrice: 100,
      date: TODAY,
    });
    await expectClassesProjected(request, token, {
      [ASSET_CLASS.cash]: 10000,
      [ASSET_CLASS.etf]: 0,
      [ASSET_CLASS.stock]: 0,
    });
    await expectLegend(page, { Gotówka: '10 000,00', ETF: null, Akcje: null });
  });

  await test.step('Krok 13: depositing 1 000 zł on the new brokerage account Pusty keeps it in Gotówka', async () => {
    await createAccountThroughUi(page, EMPTY, 'Maklerski');
    const empty = (await accountIds(request, token)).get(EMPTY);
    expect(empty).toBeTruthy();
    await depositCashThroughApi(request, token, empty?.cashAssetId as string, {
      quantity: 1000,
      date: TODAY,
    });
    await expectClassesProjected(request, token, { [ASSET_CLASS.cash]: 11000 });
    await expectLegend(page, { Gotówka: '11 000,00' });
    await expectAccountCash(page, EMPTY, '1 000,00');
  });

  await test.step('Krok 14: the class lines have no jumps outside the purchase and sale dates and the classes sum to the net worth every day', async () => {
    await openDashboard(page);
    await page
      .getByRole('listbox', { name: 'Klasa aktywów' })
      .getByRole('option', { name: 'Gotówka' })
      .click();
    await expect(
      page.getByRole('listbox', { name: 'Klasa aktywów' }).getByRole('option', { name: 'Gotówka' }),
    ).toHaveAttribute('aria-selected', 'true');

    const series: Record<string, HistoryPoint[]> = {};
    await expectProjected(
      async () => {
        series['total'] = await fetchHistory(request, token);
        for (const [name, assetClass] of Object.entries(ASSET_CLASS)) {
          series[name] = await fetchHistory(request, token, assetClass);
        }
      },
      async () => {
        const total = series['total'] ?? [];
        expect(total.length).toBeGreaterThan(0);
        for (const [index, point] of total.entries()) {
          const sum = Object.keys(ASSET_CLASS).reduce(
            (acc, name) => acc + (series[name]?.[index]?.value ?? 0),
            0,
          );
          expect(sum, `classes on ${point.date}`).toBe(point.value);
        }
        expectAllEqual(between(series['cash'] ?? [], D90, D61), 10000, 'Gotówka');
        expectAllEqual(between(series['cash'] ?? [], D60, YESTERDAY), 0, 'Gotówka after D60');
        expectAllEqual(between(series['stock'] ?? [], D60, D31), 10000, 'Akcje until D31');
        expectAllEqual(between(series['stock'] ?? [], D30, YESTERDAY), 2100, 'Akcje from D30');
        expectAllEqual(between(series['etf'] ?? [], D90, D31), 0, 'ETF before D30');
        expectAllEqual(between(series['etf'] ?? [], D30, YESTERDAY), 7900, 'ETF from D30');
        expectAllEqual(between(series['bond'] ?? [], D90, YESTERDAY), 5000, 'Obligacje');
      },
      { timeout: 90_000 },
    );
  });

  await test.step('Krok 15: the dashboard card shows the value of each account and the free cash is a separate Wolne środki figure on the list and on the account', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        const card = page.locator('.accounts-card');
        const rows = card.locator('.accounts-card__row');
        await expect(rows.filter({ hasText: BROKER })).toContainText(pln('10 000,00'), {
          timeout: 2_000,
        });
        await expect(rows.filter({ hasText: BONDS })).toContainText(pln('5 000,00'), {
          timeout: 2_000,
        });
        await expect(rows.filter({ hasText: EMPTY })).toContainText(pln('1 000,00'), {
          timeout: 2_000,
        });
      },
    );
    await openAccounts(page);
    await expect(page.getByRole('columnheader', { name: 'Wolne środki' })).toBeVisible();
    await expectAccountRowAmounts(page, BROKER, { cash: '10 000,00', value: '10 000,00' });
    await expectAccountRowAmounts(page, EMPTY, { cash: '1 000,00', value: '1 000,00' });
    await accountRow(page, EMPTY).getByRole('link', { name: EMPTY, exact: true }).click();
    await expect(
      page.locator('.account-detail__totals dt', { hasText: 'Wolne środki' }),
    ).toBeVisible();
    await expect(page.getByTestId('account-cash')).toHaveText(exactPln('1 000,00'));
  });
});

test('brokerage cash follows holdings: a bank account stays in Konta bankowe and a cash place in Gotówka', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(300_000);

  const token = await logInThroughApi(request, user);
  await page.goto('/login');
  await logInThroughUi(page, user);

  await test.step('Krok 16: a bank account with 2 000 zł stays in Konta bankowe and the cash place Sejf with 300 zł is in Gotówka', async () => {
    const created = await request.post('/api/portfolio/accounts', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'Konto',
        type: ACCOUNT_TYPE_BANK,
        wrapper: WRAPPER_REGULAR,
        currency: 'PLN',
        variant: VARIANT_CURRENT,
        annualInterestRatePercent: 0,
      },
    });
    expect(created.ok()).toBeTruthy();
    const bank = (await created.json()) as { id: string; cashAssetId: string };
    await depositCashThroughApi(request, token, bank.cashAssetId, {
      quantity: 2000,
      date: warsawDate(),
    });
    const place = await request.post('/api/portfolio/cash-places', {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'Sejf', currencies: ['PLN'] },
    });
    expect(place.ok()).toBeTruthy();
    const cashAssetId = ((await place.json()) as { balances: { cashAssetId: string }[] })
      .balances[0]?.cashAssetId as string;
    await depositCashThroughApi(request, token, cashAssetId, {
      quantity: 300,
      date: warsawDate(),
    });
    await expectClassesProjected(request, token, {
      [ASSET_CLASS_BANK_ACCOUNT]: 2000,
      [ASSET_CLASS.cash]: 300,
    });
    await expectLegend(page, { 'Konta bankowe': '2 000,00', Gotówka: '300,00' });
  });
});
