import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughUi,
  registerThroughUi,
  test,
  warsawDate,
  type Credentials,
} from './fixtures';
import {
  ASSET_CLASS,
  ASSET_CLASS_BANK_ACCOUNT,
  ASSET_CLASS_DEPOSIT,
  expectClassesProjected,
} from './cash-class';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BONDS = 1;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_CASH = 0;
const ASSET_CLASS_STOCK = 2;
const ASSET_CLASS_PRECIOUS_METAL = 6;
const ASSET_CLASS_ETF = 3;
const TYPE_BUY = 0;
const BOND_TYPE_OTS = 0;
const VARIANT_CURRENT = 0;
const METAL_GOLD = 0;
const TERM_UNIT_MONTHS = 1;
const CAPITALIZATION_AT_MATURITY = 0;

const SERIES = 'OTS0127';

function money(amount: string, unit: string): RegExp {
  const sign = amount.startsWith('+') ? '\\+' : amount.startsWith('-') ? '[-\\u2212]' : '';
  const digits = amount.replace(/^[+-]/, '').replace(/ /g, '\\s?');
  return new RegExp(`${sign}${digits}\\s${unit}`);
}

function pln(amount: string): RegExp {
  return money(amount, 'zł');
}

function exactly(pattern: RegExp): RegExp {
  return new RegExp(`^\\s*${pattern.source}\\s*$`);
}

function percent(value: string): RegExp {
  return new RegExp(`^\\s*${value}\\s%\\s*$`);
}

function toGrosze(text: string): number {
  const cleaned = text
    .replace(/[\s  ]/g, '')
    .replace('−', '-')
    .replace('zł', '')
    .replace('+', '')
    .replace(',', '.');
  return Math.round(Number(cleaned) * 100);
}

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

async function openDashboard(page: Page): Promise<void> {
  await navigation(page)
    .getByRole('link', { name: /^(Pulpit|Dashboard)$/ })
    .click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^(Pulpit|Dashboard)$/);
}

async function openAllocation(page: Page): Promise<void> {
  await navigation(page)
    .getByRole('link', { name: /^(Alokacja|Allocation)$/ })
    .click();
  await expect(page).toHaveURL(/\/allocation$/);
}

function card(page: Page): Locator {
  return page.locator('.allocation-card');
}

function editorRow(page: Page, index: number): Locator {
  return page.getByTestId('editor-row').nth(index);
}

function groupRow(page: Page, index: number): Locator {
  return page.getByTestId('group-row').nth(index);
}

async function startEditing(page: Page): Promise<void> {
  await page.getByTestId('edit').click();
  await expect(page.getByTestId('allocation-editor')).toBeVisible();
}

async function pickClass(page: Page, row: Locator, label: string): Promise<void> {
  await row.getByTestId('group-classes').click();
  await page.getByRole('option', { name: label, exact: true }).click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('option', { name: label, exact: true })).toBeHidden();
}

async function addGroup(page: Page, group: { name: string; assetClass: string; target: string }) {
  const before = await page.getByTestId('editor-row').count();
  await page.getByRole('button', { name: 'Dodaj grupę' }).click();
  const row = editorRow(page, before);
  await row.getByTestId('group-name').fill(group.name);
  await pickClass(page, row, group.assetClass);
  await row.getByTestId('group-target').fill(group.target);
}

async function expectGroup(
  page: Page,
  index: number,
  group: {
    name: string;
    value: string;
    current: string;
    target: string;
    band: string;
    deviationPp: string;
    deviationPln: string;
    outOfBand: boolean;
  },
): Promise<void> {
  const row = groupRow(page, index);
  const cells = row.locator('td');
  await expect(row.locator('.allocation-page__group-name')).toHaveText(group.name);
  await expect(cells.nth(1)).toHaveText(exactly(pln(group.value)));
  await expect(cells.nth(2)).toHaveText(percent(group.current));
  await expect(cells.nth(3)).toHaveText(percent(group.target));
  await expect(cells.nth(4)).toHaveText(
    new RegExp(`^\\s*${group.band.replace('–', '[–-]')}\\s%\\s*$`),
  );
  await expect(cells.nth(5)).toContainText(money(group.deviationPp, 'pp'));
  await expect(cells.nth(6)).toHaveText(exactly(pln(group.deviationPln)));
  await expect(row.getByTestId('out-of-band')).toHaveCount(group.outOfBand ? 1 : 0);
  if (group.outOfBand) {
    await expect(row.getByTestId('out-of-band')).toHaveText('Poza pasmem');
  }
}

async function createBankAccount(
  request: APIRequestContext,
  token: string,
  name: string,
): Promise<{ id: string; cashAssetId: string }> {
  const response = await request.post('/api/portfolio/accounts', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as { id: string; cashAssetId: string };
}

async function createSecurityThroughApi(
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
  },
): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const search = await request.get(
    `/api/marketdata/instruments/search?q=${security.query}&assetClass=${security.assetClassName}`,
    { headers },
  );
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === security.ticker);
  expect(found?.id).toBeTruthy();

  // The first buy freezes the FX rate of its day, which MarketData serves only after its first FX sync.
  await expect(async () => {
    const created = await request.post('/api/portfolio/assets', {
      headers,
      data: {
        accountId: security.accountId,
        assetClass: security.assetClass,
        name: security.ticker,
        currency: security.currency,
        instrumentId: found?.id,
        initialTransaction: {
          type: TYPE_BUY,
          quantity: security.quantity,
          unitPrice: security.unitPrice,
          date: warsawDate(),
        },
      },
    });
    expect(created.ok()).toBeTruthy();
  }).toPass({ timeout: 60_000, intervals: [2_000] });
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
      bondCount: 500,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 2,
      earlyRedemptionFeePerBond: 0,
    },
  });
  expect(created.ok()).toBeTruthy();
}

async function createDepositThroughApi(
  request: APIRequestContext,
  token: string,
  sourceAccountId: string,
): Promise<void> {
  const created = await request.post('/api/portfolio/deposits', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name: 'Lokata',
      sourceAccountId,
      principal: 18000,
      startDate: warsawDate(),
      termLength: 12,
      termUnit: TERM_UNIT_MONTHS,
      annualInterestRatePercent: 5,
      capitalization: CAPITALIZATION_AT_MATURITY,
    },
  });
  expect(created.ok()).toBeTruthy();
}

async function createGoldBarThroughApi(request: APIRequestContext, token: string): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const place = await request.post('/api/portfolio/cash-places', {
    headers,
    data: { name: 'Sejf', currencies: ['PLN'] },
  });
  expect(place.ok()).toBeTruthy();
  const cashAssetId = ((await place.json()) as { balances: { cashAssetId: string }[] }).balances[0]
    ?.cashAssetId as string;
  await depositCashThroughApi(request, token, cashAssetId, {
    quantity: 12000,
    date: warsawDate(),
  });
  await expectNetWorthProjected(request, token, 220000);

  const holding = await request.post('/api/portfolio/metals', {
    headers,
    data: { name: 'Sztabka 10 g', metal: METAL_GOLD, fineWeight: 10, weightUnit: 'Gram' },
  });
  expect(holding.ok()).toBeTruthy();
  const assetId = ((await holding.json()) as { assetId: string }).assetId;

  const bought = await request.post(`/api/portfolio/assets/${assetId}/transactions`, {
    headers,
    data: {
      type: TYPE_BUY,
      quantity: 1,
      totalAmount: 12000,
      date: warsawDate(),
      cashAssetId,
    },
  });
  expect(bought.ok()).toBeTruthy();
  await expectGoldProjected(request, token);
}

async function expectNetWorthProjected(
  request: APIRequestContext,
  token: string,
  netWorthPln: number,
): Promise<void> {
  let current: number | undefined;
  await expectProjected(
    async () => {
      const response = await request.get('/api/reporting/dashboard', {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(response.ok()).toBeTruthy();
      current = ((await response.json()) as { netWorthPln: number }).netWorthPln;
    },
    async () => {
      expect(current).toBe(netWorthPln);
    },
  );
}

async function expectGoldProjected(request: APIRequestContext, token: string): Promise<void> {
  let byClass: { assetClass: number; valuePln: number }[] = [];
  await expectProjected(
    async () => {
      const response = await request.get('/api/reporting/dashboard', {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(response.ok()).toBeTruthy();
      byClass = ((await response.json()) as { byAssetClass: typeof byClass }).byAssetClass;
    },
    async () => {
      expect(
        byClass.find((entry) => entry.assetClass === ASSET_CLASS_PRECIOUS_METAL)?.valuePln,
      ).toBe(12000);
      expect(byClass.find((entry) => entry.assetClass === ASSET_CLASS_CASH)?.valuePln ?? 0).toBe(0);
    },
  );
}

async function syncPrices(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ustawienia', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Synchronizuj teraz' }).click();
  await expect(page.getByText(/Rozpoczęto/).first()).toBeVisible({ timeout: 30_000 });
}

async function expectNetWorth(page: Page, amount: string): Promise<void> {
  await expectProjected(
    () => openDashboard(page),
    async () => {
      await expect(page.getByTestId('net-worth')).toContainText(pln(amount), { timeout: 2_000 });
    },
  );
}

test('target allocation: the editor, the deviations table, the dashboard card, English and a user without a valuation', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  await test.step('Przygotowanie 2: a new account', async () => {
    await registerThroughUi(page, user);
    await logInThroughUi(page, user);
  });

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const accessToken = ((await login.json()) as { accessToken: string }).accessToken;

  await test.step('Przygotowanie 3: Makler with 120 000 zł, 200 × VWCE.DE and 350 × CDR.WA leaves 0 zł of cash', async () => {
    const broker = await createAccountThroughApi(request, accessToken, {
      name: 'Makler',
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_REGULAR,
    });
    await depositCashThroughApi(request, accessToken, broker.cashAssetId, {
      quantity: 120000,
      date: warsawDate(),
    });
    await expectClassesProjected(request, accessToken, { [ASSET_CLASS.cash]: 120000 });
    await createSecurityThroughApi(request, accessToken, {
      accountId: broker.id,
      ticker: 'VWCE.DE',
      query: 'VWCE',
      assetClass: ASSET_CLASS_ETF,
      assetClassName: 'Etf',
      currency: 'EUR',
      quantity: 200,
      unitPrice: 100,
    });
    await createSecurityThroughApi(request, accessToken, {
      accountId: broker.id,
      ticker: 'CDR.WA',
      query: 'CDR',
      assetClass: ASSET_CLASS_STOCK,
      assetClassName: 'Stock',
      currency: 'PLN',
      quantity: 350,
      unitPrice: 100,
    });
    await expectClassesProjected(request, accessToken, {
      [ASSET_CLASS.etf]: 85000,
      [ASSET_CLASS.stock]: 35000,
      [ASSET_CLASS.cash]: 0,
    });
  });

  await test.step('Przygotowanie 4: the bonds account holds 500 × OTS0127 worth 50 000 zł', async () => {
    const bonds = await createAccountThroughApi(request, accessToken, {
      name: 'Obligacje',
      type: ACCOUNT_TYPE_BONDS,
      wrapper: WRAPPER_REGULAR,
    });
    await depositCashThroughApi(request, accessToken, bonds.cashAssetId, {
      quantity: 50000,
      date: warsawDate(),
    });
    await expectClassesProjected(request, accessToken, { [ASSET_CLASS.bond]: 50000 });
    await createBondThroughApi(request, accessToken, bonds.id);
  });

  await test.step('Przygotowanie 5: the bank account Konto deposits 38 000 zł and 18 000 zł of it goes to a term deposit', async () => {
    const bank = await createBankAccount(request, accessToken, 'Konto');
    await depositCashThroughApi(request, accessToken, bank.cashAssetId, {
      quantity: 38000,
      date: warsawDate(),
    });
    await expectClassesProjected(request, accessToken, { [ASSET_CLASS_BANK_ACCOUNT]: 38000 });
    await createDepositThroughApi(request, accessToken, bank.id);
    await expectClassesProjected(request, accessToken, {
      [ASSET_CLASS_BANK_ACCOUNT]: 20000,
      [ASSET_CLASS_DEPOSIT]: 18000,
    });
  });

  await test.step('Przygotowanie 6: one 10 g gold bar is worth 12 000 zł', async () => {
    await createGoldBarThroughApi(request, accessToken);
  });

  await test.step('Przygotowanie 7: after the price synchronization the net worth is 220 000,00 zł', async () => {
    await syncPrices(page);
    await expectNetWorth(page, '220 000,00');
  });

  await test.step('Krok 1: the dashboard card says Ustaw alokację docelową, the chart legend keeps its classes and the card leads to /allocation', async () => {
    await expect(card(page)).toContainText('Ustaw alokację docelową');
    await expect(page.getByRole('button', { name: /^ETF — / })).toContainText(pln('85 000,00'));
    await expect(page.getByRole('button', { name: /^Akcje — / })).toContainText(pln('35 000,00'));
    await expect(page.getByRole('button', { name: /^Obligacje — / })).toContainText(
      pln('50 000,00'),
    );
    await card(page).getByRole('link', { name: 'Alokacja' }).click();
    await expect(page).toHaveURL(/\/allocation$/);
    await expect(page.getByRole('heading', { name: 'Alokacja docelowa', level: 1 })).toBeVisible();
  });

  await test.step('Krok 2: the menu opens the editor with the proposal and the live values per group', async () => {
    await openDashboard(page);
    await openAllocation(page);
    const editor = page.getByTestId('allocation-editor');
    await expect(editor).toBeVisible();
    await expect(page.getByTestId('editor-row')).toHaveCount(3);

    const proposal = [
      { name: 'Akcje/ETF', classes: ['Akcje', 'ETF'], value: '120 000,00', share: '60' },
      { name: 'Obligacje', classes: ['Obligacje', 'Lokata'], value: '68 000,00', share: '34' },
      { name: 'Kruszce', classes: ['Metale szlachetne'], value: '12 000,00', share: '6' },
    ];
    for (const [index, group] of proposal.entries()) {
      const row = editorRow(page, index);
      await expect(row.getByTestId('group-name')).toHaveValue(group.name);
      for (const label of group.classes) {
        await expect(row.getByTestId('group-classes')).toContainText(label);
      }
      await expect(row.getByTestId('group-band')).toHaveValue('5');
      await expect(row.locator('.allocation-page__live')).toContainText(pln(group.value));
      await expect(row.locator('.allocation-page__live')).toContainText(
        new RegExp(`${group.share}\\s%`),
      );
    }
    await expect(page.getByTestId('outside-row')).toHaveCount(0);
  });

  await test.step('Krok 3: targets 50, 40 and 9,9 sum to 99,9 % and block the save', async () => {
    await editorRow(page, 0).getByTestId('group-target').fill('50');
    await editorRow(page, 1).getByTestId('group-target').fill('40');
    await editorRow(page, 2).getByTestId('group-target').fill('9.9');
    await expect(page.getByTestId('sum')).toContainText(/99,9\s%/);
    await expect(page.getByTestId('save')).toBeDisabled();
  });

  await test.step('Krok 4: Kruszce at 10 sums to 100 % and enables the save', async () => {
    await editorRow(page, 2).getByTestId('group-target').fill('10');
    await expect(page.getByTestId('sum')).toContainText(/100\s%/);
    await expect(page.getByTestId('save')).toBeEnabled();
  });

  await test.step('Krok 5: the class picker of Kruszce disables Akcje and ETF, which belong to Akcje/ETF', async () => {
    await editorRow(page, 2).getByTestId('group-classes').click();
    await expect(page.getByRole('option', { name: 'Akcje', exact: true })).toBeDisabled();
    await expect(page.getByRole('option', { name: 'ETF', exact: true })).toBeDisabled();
    await expect(
      page.getByRole('option', { name: 'Metale szlachetne', exact: true }),
    ).toBeEnabled();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('option', { name: 'Akcje', exact: true })).toBeHidden();
  });

  await test.step('Krok 6: saving shows the deviations of the three groups', async () => {
    await page.getByTestId('save').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(3);
    await expectGroup(page, 0, {
      name: 'Akcje/ETF',
      value: '120 000,00',
      current: '60',
      target: '50',
      band: '45–55',
      deviationPp: '+10,0',
      deviationPln: '+20 000,00',
      outOfBand: true,
    });
    await expectGroup(page, 1, {
      name: 'Obligacje',
      value: '68 000,00',
      current: '34',
      target: '40',
      band: '35–45',
      deviationPp: '-6,0',
      deviationPln: '-12 000,00',
      outOfBand: true,
    });
    await expectGroup(page, 2, {
      name: 'Kruszce',
      value: '12 000,00',
      current: '6',
      target: '10',
      band: '5–15',
      deviationPp: '-4,0',
      deviationPln: '-8 000,00',
      outOfBand: false,
    });
  });

  await test.step('Krok 7: below the table Poza alokacją shows 20 000,00 zł without a share, the summary has the base and no advice words', async () => {
    const outside = page.getByTestId('outside-row');
    await expect(outside).toContainText('Poza alokacją');
    await expect(outside).toContainText(pln('20 000,00'));
    await expect(outside).not.toContainText('%');
    await expect(page.getByTestId('summary')).toContainText(
      new RegExp(`baza\\s${pln('200 000,00').source}\\sz\\s${pln('220 000,00').source}`),
    );
    await expect(page.locator('.allocation-page')).not.toContainText(/kup|sprzedaj/i);
  });

  await test.step('Krok 8: the dashboard card says 2 z 3 grup poza pasmem', async () => {
    await openDashboard(page);
    await expect(card(page)).toContainText('2 z 3 grup poza pasmem');
  });

  await test.step('Krok 9: the band of Obligacje at ±6 puts the group in the band at its 34 % edge', async () => {
    await openAllocation(page);
    await startEditing(page);
    await editorRow(page, 1).getByTestId('group-band').fill('6');
    await page.getByTestId('save').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expectGroup(page, 1, {
      name: 'Obligacje',
      value: '68 000,00',
      current: '34',
      target: '40',
      band: '34–46',
      deviationPp: '-6,0',
      deviationPln: '-12 000,00',
      outOfBand: false,
    });
  });

  await test.step('Krok 10: the dashboard card says 1 z 3 grup poza pasmem', async () => {
    await openDashboard(page);
    await expect(card(page)).toContainText('1 z 3 grup poza pasmem');
  });

  await test.step('Krok 11: a Gotówka group at 10 % and Akcje/ETF at 40 % show their deviations and the amounts sum to 0', async () => {
    await openAllocation(page);
    await startEditing(page);
    await addGroup(page, { name: 'Gotówka', assetClass: 'Gotówka', target: '10' });
    await editorRow(page, 0).getByTestId('group-target').fill('40');
    await page.getByTestId('save').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(4);
    await expectGroup(page, 3, {
      name: 'Gotówka',
      value: '0,00',
      current: '0',
      target: '10',
      band: '5–15',
      deviationPp: '-10,0',
      deviationPln: '-20 000,00',
      outOfBand: true,
    });
    await expectGroup(page, 0, {
      name: 'Akcje/ETF',
      value: '120 000,00',
      current: '60',
      target: '40',
      band: '35–45',
      deviationPp: '+20,0',
      deviationPln: '+40 000,00',
      outOfBand: true,
    });
    const deviations = await page
      .getByTestId('group-row')
      .locator('td:nth-child(7)')
      .allInnerTexts();
    expect(deviations).toHaveLength(4);
    expect(deviations.map(toGrosze).reduce((sum, value) => sum + value, 0)).toBe(0);
  });

  await test.step('Krok 12: renaming Kruszce to akcje/etf blocks the save as a taken name and Anuluj leaves the view unchanged', async () => {
    await startEditing(page);
    await editorRow(page, 2).getByTestId('group-name').fill('akcje/etf');
    await expect(page.getByRole('alert')).toContainText('Nazwy grup muszą się różnić.');
    await expect(page.getByTestId('save')).toBeDisabled();
    await page.getByTestId('cancel').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(4);
    await expect(groupRow(page, 2).locator('.allocation-page__group-name')).toHaveText('Kruszce');
  });

  await test.step('Krok 13: removing Gotówka without fixing the targets gives 90 % and blocks the save, Anuluj keeps four groups', async () => {
    await startEditing(page);
    await editorRow(page, 3).getByRole('button', { name: 'Usuń grupę' }).click();
    await expect(page.getByTestId('editor-row')).toHaveCount(3);
    await expect(page.getByTestId('sum')).toContainText(/90\s%/);
    await expect(page.getByTestId('save')).toBeDisabled();
    await page.getByTestId('cancel').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(4);
  });

  await test.step('Krok 14: the dashboard card says 2 z 4 grup poza pasmem', async () => {
    await openDashboard(page);
    await expect(card(page)).toContainText('2 z 4 grup poza pasmem');
  });

  await test.step('Krok 15: switching to English translates /allocation and the card and keeps the group names', async () => {
    await page.getByRole('button', { name: 'Język' }).click();
    await page.getByRole('menuitemradio', { name: 'English' }).click();
    await expect(card(page)).toContainText('2 of 4 groups outside the band');
    await expect(card(page).getByRole('heading', { name: 'Target allocation' })).toBeVisible();
    await openAllocation(page);
    await expect(page.getByRole('heading', { name: 'Target allocation', level: 1 })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Group' })).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(4);
    for (const [index, name] of ['Akcje/ETF', 'Obligacje', 'Kruszce', 'Gotówka'].entries()) {
      await expect(groupRow(page, index).locator('.allocation-page__group-name')).toHaveText(name);
    }
    await page.getByRole('button', { name: 'Language' }).click();
    await page.getByRole('menuitemradio', { name: 'Polski' }).click();
    await expect(page.getByRole('heading', { name: 'Alokacja docelowa', level: 1 })).toBeVisible();
  });

  const second: Credentials = {
    email: `second-${user.email}`,
    password: user.password,
    displayName: `${user.displayName} 2`,
  };

  await test.step('Krok 16: a new account without a valuation gets the proposal without amounts and the card Ustaw alokację docelową', async () => {
    await page.getByRole('button', { name: 'Wyloguj' }).click();
    await expect(page).toHaveURL(/\/login$/);
    await registerThroughUi(page, second);
    await logInThroughUi(page, second);
    await openAllocation(page);
    await expect(page.getByTestId('valuation-note')).toHaveText(/Aktualna wartość: brak wyceny\./);
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(page.getByTestId('editor-row')).toHaveCount(3);
    await expect(page.locator('.allocation-page__live')).toHaveCount(0);
    await expect(page.getByTestId('outside-row')).toHaveCount(0);
    await openDashboard(page);
    await expect(card(page)).toContainText('Ustaw alokację docelową');
  });

  await test.step('Krok 17: with only a 5 000 zł bank account, saving Akcje/ETF at 100 % shows brak aktywów w alokacji and Poza alokacją 5 000,00 zł, and so does the card', async () => {
    const secondLogin = await request.post('/api/identity/login', {
      data: { email: second.email, password: second.password },
    });
    expect(secondLogin.ok()).toBeTruthy();
    const secondToken = ((await secondLogin.json()) as { accessToken: string }).accessToken;
    const bank = await createBankAccount(request, secondToken, 'Konto');
    await depositCashThroughApi(request, secondToken, bank.cashAssetId, {
      quantity: 5000,
      date: warsawDate(),
    });
    await syncPrices(page);
    await expectNetWorth(page, '5 000,00');

    await openAllocation(page);
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(page.getByTestId('valuation-note')).not.toContainText('brak wyceny');
    await editorRow(page, 0).getByTestId('group-target').fill('100');
    await editorRow(page, 1).getByTestId('group-target').fill('0');
    await editorRow(page, 2).getByTestId('group-target').fill('0');
    await page.getByTestId('save').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByTestId('valuation-note')).toHaveText(
      /Odchylenia: brak aktywów w alokacji\./,
    );
    await expect(page.getByTestId('summary')).toHaveCount(0);
    await expect(page.getByTestId('group-row').first().locator('td')).toHaveCount(3);
    await expect(page.getByTestId('outside-row')).toContainText('Poza alokacją');
    await expect(page.getByTestId('outside-row')).toContainText(pln('5 000,00'));
    await openDashboard(page);
    await expect(card(page)).toContainText('brak aktywów w alokacji');
  });
});
