import { type APIRequestContext, type Locator, type Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
} from './fixtures';
import {
  MBANK,
  addExpense,
  addIncome,
  choose,
  dateInText,
  exactly,
  expectCost,
  expectFreeFunds,
  money,
  occurrences,
  openMonth,
  pln,
  settle,
  shiftMonth,
  type IncomeInput,
} from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BONDS = 1;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const WRAPPER_IKE = 1;
const WRAPPER_IKZE = 2;
const ASSET_CLASS_CASH = 0;
const ASSET_CLASS_STOCK = 2;
const ASSET_CLASS_PRECIOUS_METAL = 6;
const TYPE_BUY = 0;
const BOND_TYPE_OTS = 0;
const VARIANT_CURRENT = 0;
const METAL_GOLD = 0;

const MAKLER = 'Makler';
const BONDS_ACCOUNT = 'Obligacje';
const IKE_ACCOUNT = 'IKE mBank';
const IKZE_ACCOUNT = 'IKZE Obligacje';
const SERIES = 'OTS0127';
const CUSHION = 'Poduszka';
const HOLIDAY = 'Wakacje';
const IKE = 'IKE';
const IKZE = 'IKZE';
const INVESTMENTS = 'Inwestycje';

const M = warsawDate().slice(0, 7);

const INCOMES: IncomeInput[] = [
  {
    name: 'Wypłata Dominik',
    amount: '9200',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 'last',
  },
  {
    name: 'Wypłata żona',
    amount: '6500',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 10,
    shift: 'Poprzedni dzień roboczy',
  },
  {
    name: '800+',
    amount: '800',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 10,
  },
];

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function exactPln(amount: string): RegExp {
  return new RegExp(`^\\s*${pln(amount).source}\\s*$`);
}

function amount(value: string): string {
  return `${value.replace(/\s/g, '')}zł`;
}

function percentCell(value: string): RegExp {
  const pattern = value.endsWith(',0') ? `${value.slice(0, -2)}(?:,0)?` : value;
  return new RegExp(`^\\s*${pattern}\\s%\\s*$`);
}

function bucket(page: Page, name: string): Locator {
  return page.getByTestId('allocation-bucket-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

async function expectBucket(page: Page, name: string, value: string): Promise<void> {
  await expect(bucket(page, name).locator('.planning-allocation__amount')).toHaveText(
    exactPln(value),
  );
}

async function expectToInvest(page: Page, value: string): Promise<void> {
  await expect(page.getByTestId('allocation-to-invest').locator('dd')).toHaveText(exactPln(value));
}

async function openPlanDialog(page: Page): Promise<Locator> {
  await page.getByTestId('edit-allocation').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj podział' })).toBeVisible();
  return dialog;
}

interface BucketInput {
  name: string;
  kind?: string;
  share: string;
  fixed?: boolean;
  remainder?: boolean;
  account?: string;
  contributed?: string;
}

async function addBucket(page: Page, dialog: Locator, input: BucketInput): Promise<void> {
  await dialog.getByTestId('add-row').click();
  const row = dialog.getByTestId('plan-row').last();
  await row.getByTestId('row-name').fill(input.name);
  if (input.kind) {
    await choose(page, row.getByRole('combobox', { name: 'Rodzaj', exact: true }), input.kind);
  }
  await row.getByTestId('row-share').fill(input.share);
  if (input.contributed) {
    await row.getByTestId('row-contributed').fill(input.contributed);
  }
  if (input.account) {
    await choose(page, row.getByRole('combobox', { name: 'Konto', exact: true }), input.account);
  }
  if (input.remainder) {
    await row.getByRole('checkbox', { name: 'Reszta' }).check();
  }
  if (input.fixed) {
    await choose(
      page,
      row.getByRole('combobox', { name: 'Typ udziału', exact: true }),
      'Kwota stała',
    );
  }
}

function allocationRow(page: Page, index: number): Locator {
  return page.getByTestId('editor-row').nth(index);
}

async function openAllocation(page: Page): Promise<void> {
  await navigation(page)
    .getByRole('link', { name: /^(Alokacja|Allocation)$/ })
    .click();
  await expect(page).toHaveURL(/\/allocation$/);
}

function planRows(page: Page): Locator {
  return page.getByTestId('transfer-batch').getByTestId('transfer-plan-row');
}

function topUpRows(page: Page): Locator {
  return page.getByTestId('transfer-top-up').getByTestId('transfer-plan-row');
}

interface ExpectedRow {
  bucket: string;
  account: string;
  group: string;
  amount: string;
  marker?: string;
}

function describeRow(row: ExpectedRow): string {
  return [row.bucket, row.account, row.group, amount(row.amount), row.marker ?? ''].join('|');
}

async function readRows(rows: Locator): Promise<string[]> {
  const cells = await rows.evaluateAll((items) =>
    items.map((item) =>
      Array.from(item.querySelectorAll(':scope > span')).map((span) =>
        (span.textContent ?? '').replace(/\s+/g, ' ').trim(),
      ),
    ),
  );
  return cells
    .map((row) =>
      row.map((cell, index) => (index === 3 ? cell.replace(/\s/g, '') : cell)).join('|'),
    )
    .sort();
}

async function expectRows(rows: Locator, expected: ExpectedRow[], timeout = 10_000): Promise<void> {
  const wanted = expected.map(describeRow).sort();
  await expect.poll(() => readRows(rows), { timeout }).toEqual(wanted);
}

async function expectRowsProjected(
  page: Page,
  rows: Locator,
  expected: ExpectedRow[],
): Promise<void> {
  await expectProjected(
    () => openMonth(page, M),
    () => expectRows(rows, expected, 2_000),
  );
}

async function expectStatus(page: Page, status: string): Promise<void> {
  await expect(page.getByTestId('transfer-plan-status')).toHaveText(status);
}

interface ExpectedGroup {
  name: string;
  current: string;
  after: string;
  target: string;
}

async function expectGroups(page: Page, groups: ExpectedGroup[]): Promise<void> {
  const rows = page.getByTestId('transfer-group-row');
  await expect(rows).toHaveCount(groups.length);
  for (const [index, group] of groups.entries()) {
    const cells = rows.nth(index).locator('td');
    await expect(cells.nth(0)).toHaveText(group.name);
    await expect(cells.nth(1)).toHaveText(percentCell(group.current));
    await expect(cells.nth(2)).toHaveText(percentCell(group.after));
    await expect(cells.nth(3)).toHaveText(percentCell(group.target));
  }
}

async function setMinimum(page: Page, value: string): Promise<void> {
  const input = page.getByTestId('transfer-minimum-input');
  await input.fill(value);
  await input.press('Enter');
}

async function confirmUndo(page: Page): Promise<void> {
  await page.getByTestId('transfer-undo').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Cofnij przyjęcie', exact: true }).click();
  await expect(dialog).toBeHidden();
}

function transferRow(page: Page, name: string): Locator {
  return planRows(page).filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

const STEP_3_ROWS: ExpectedRow[] = [
  {
    bucket: IKE,
    account: IKE_ACCOUNT,
    group: 'Akcje/ETF',
    amount: '2 260,00',
    marker: 'powyżej celu',
  },
  { bucket: IKZE, account: IKZE_ACCOUNT, group: 'Obligacje', amount: '2 304,00' },
  { bucket: INVESTMENTS, account: 'bez konta', group: 'Obligacje', amount: '1 300,27' },
  { bucket: INVESTMENTS, account: 'bez konta', group: 'Kruszce', amount: '679,00' },
];

const STEP_3_GROUPS: ExpectedGroup[] = [
  { name: 'Akcje/ETF', current: '60,4', after: '59,5', target: '50' },
  { name: 'Obligacje', current: '33,0', after: '33,7', target: '40' },
  { name: 'Kruszce', current: '6,6', after: '6,7', target: '10' },
];

async function createBankAccount(
  request: APIRequestContext,
  token: string,
  name: string,
): Promise<void> {
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
  ).results.find((result) => result.ticker === 'CDR.WA');
  expect(found?.id).toBeTruthy();

  // The first buy freezes the FX rate of its day, which MarketData serves only after its first FX sync.
  await expect(async () => {
    const created = await request.post('/api/portfolio/assets', {
      headers,
      data: {
        accountId,
        assetClass: ASSET_CLASS_STOCK,
        name: 'CDR.WA',
        currency: 'PLN',
        instrumentId: found?.id,
        initialTransaction: {
          type: TYPE_BUY,
          quantity: 1100,
          unitPrice: 100,
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
      bondCount: 600,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 2,
      earlyRedemptionFeePerBond: 0,
    },
  });
  expect(created.ok()).toBeTruthy();
}

interface AccountValue {
  accountId: string | null;
  valuePln: number;
  cashPln: number;
}

async function expectAccountProjected(
  request: APIRequestContext,
  token: string,
  accountId: string,
  expected: { valuePln: number; cashPln: number },
): Promise<void> {
  let current: AccountValue | undefined;
  await expectProjected(
    async () => {
      const response = await request.get('/api/reporting/dashboard', {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(response.ok()).toBeTruthy();
      current = ((await response.json()) as { byAccount: AccountValue[] }).byAccount.find(
        (account) => account.accountId === accountId,
      );
    },
    async () => {
      expect(current?.valuePln).toBe(expected.valuePln);
      expect(current?.cashPln).toBe(expected.cashPln);
    },
  );
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
  await expectNetWorthProjected(request, token, 182000);

  const holding = await request.post('/api/portfolio/metals', {
    headers,
    data: { name: 'Sztabka 10 g', metal: METAL_GOLD, fineWeight: 10, weightUnit: 'Gram' },
  });
  expect(holding.ok()).toBeTruthy();
  const assetId = ((await holding.json()) as { assetId: string }).assetId;

  const bought = await request.post(`/api/portfolio/assets/${assetId}/transactions`, {
    headers,
    data: { type: TYPE_BUY, quantity: 1, totalAmount: 12000, date: warsawDate(), cashAssetId },
  });
  expect(bought.ok()).toBeTruthy();

  let byClass: { assetClass: number; valuePln: number }[] = [];
  await expectProjected(
    async () => {
      const response = await request.get('/api/reporting/dashboard', { headers });
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

async function expectNetWorth(page: Page, value: string): Promise<void> {
  await expectProjected(
    async () => {
      await navigation(page)
        .getByRole('link', { name: /^(Pulpit|Dashboard)$/ })
        .click();
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^(Pulpit|Dashboard)$/);
    },
    async () => {
      await expect(page.getByTestId('net-worth')).toContainText(pln(value), { timeout: 2_000 });
    },
  );
}

test('transfer plan: preview, minimum, skip, accept, check off, undo, top-up and English', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(1_200_000);

  await test.step('Przygotowanie 2: a new account', async () => {
    await logInThroughApi(request, user);
    await page.goto('/login');
    await logInThroughUi(page, user);
  });

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;

  await test.step('Przygotowanie 4: accounts mBank ROR, Makler, Obligacje, IKE mBank and IKZE Obligacje', async () => {
    await createBankAccount(request, token, MBANK);
    await createAccountThroughApi(request, token, {
      name: IKE_ACCOUNT,
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_IKE,
    });
    await createAccountThroughApi(request, token, {
      name: IKZE_ACCOUNT,
      type: ACCOUNT_TYPE_BONDS,
      wrapper: WRAPPER_IKZE,
    });
  });

  await test.step('Przygotowanie 5: Makler holds 1 100 × CDR.WA, Obligacje 600 × OTS0127 and the gold bar 12 000 zł, after the price synchronization the net worth is 182 000,00 zł', async () => {
    const broker = await createAccountThroughApi(request, token, {
      name: MAKLER,
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_REGULAR,
    });
    await depositCashThroughApi(request, token, broker.cashAssetId, {
      quantity: 110000,
      date: warsawDate(),
    });
    await expectAccountProjected(request, token, broker.id, {
      valuePln: 110000,
      cashPln: 110000,
    });
    await createStockThroughApi(request, token, broker.id);
    await expectAccountProjected(request, token, broker.id, { valuePln: 110000, cashPln: 0 });

    const bonds = await createAccountThroughApi(request, token, {
      name: BONDS_ACCOUNT,
      type: ACCOUNT_TYPE_BONDS,
      wrapper: WRAPPER_REGULAR,
    });
    await depositCashThroughApi(request, token, bonds.cashAssetId, {
      quantity: 60000,
      date: warsawDate(),
    });
    await expectAccountProjected(request, token, bonds.id, { valuePln: 60000, cashPln: 60000 });
    await createBondThroughApi(request, token, bonds.id);
    await expectAccountProjected(request, token, bonds.id, { valuePln: 60000, cashPln: 0 });

    await createGoldBarThroughApi(request, token);
    await syncPrices(page);
    await expectNetWorth(page, '182 000,00');
  });

  await test.step('Przygotowanie 6: incomes and expenses from 2026-01-01 on mBank ROR', async () => {
    await openMonth(page, M);
    for (const income of INCOMES) {
      await addIncome(page, income);
    }
    await addExpense(page, {
      name: 'Życie',
      amount: '3000',
      start: '2026-01-01',
      interval: '1',
      day: 1,
    });
    await addExpense(page, {
      name: 'Rachunki',
      amount: '1000',
      start: '2026-01-01',
      interval: '1',
      day: 10,
    });
    await addExpense(page, {
      name: 'OC Toyota',
      amount: '1200',
      start: `${shiftMonth(M, 5)}-15`,
      interval: '12',
      day: 15,
    });
  });

  await test.step('Przygotowanie 7: settling Wypłata Dominik at 9 143,27 zł and Wypłata żona at 6 500 zł gives forecast 12 243,27 zł', async () => {
    await settle(page, 'Wypłata Dominik', '9143.27');
    await settle(page, 'Wypłata żona', '6500');
    await expectFreeFunds(page, '12 300,00', '12 243,27');
    await expectCost(page, '4 200,00');
  });

  await test.step('Przygotowanie 8: the split from M gives IKE 2 260 zł, IKZE 2 304 zł, Inwestycje 1 979,27 zł and to invest 6 543,27 zł', async () => {
    const dialog = await openPlanDialog(page);
    await dialog.getByTestId('add-cushion').click();
    const cushion = dialog.getByTestId('plan-row').first();
    await cushion.getByTestId('row-name').fill(CUSHION);
    await cushion.getByTestId('row-target-months').fill('6');
    await cushion.getByTestId('row-starting-balance').fill('20000');
    await addBucket(page, dialog, {
      name: HOLIDAY,
      kind: 'Oszczędności',
      share: '500',
      fixed: true,
    });
    await addBucket(page, dialog, {
      name: IKE,
      kind: 'IKE',
      share: '50',
      contributed: '26000',
      account: IKE_ACCOUNT,
    });
    await addBucket(page, dialog, {
      name: IKZE,
      kind: 'IKZE',
      share: '20',
      contributed: '9000',
      account: IKZE_ACCOUNT,
    });
    await addBucket(page, dialog, { name: INVESTMENTS, share: '100', remainder: true });
    await dialog.getByTestId('plan-row').last().getByTestId('row-share').fill('30');
    await dialog.getByTestId('save').click();
    await expect(dialog).toBeHidden();
    await expectBucket(page, IKE, '2 260,00');
    await expectBucket(page, IKZE, '2 304,00');
    await expectBucket(page, INVESTMENTS, '1 979,27');
    await expectToInvest(page, '6 543,27');
  });

  await test.step('Krok 1: Plan przelewów under Podział is a preview with the minimum 100 zł, rows without groups and the link Ustaw alokację docelową', async () => {
    await openMonth(page, M);
    const section = page.getByTestId('transfer-plan-section');
    await expect(section.getByRole('heading', { name: 'Plan przelewów' })).toBeVisible();
    await expectStatus(page, 'Podgląd');
    await expect(page.getByTestId('transfer-minimum-input')).toHaveValue('100');
    await expectRows(planRows(page), [
      { bucket: IKE, account: IKE_ACCOUNT, group: '', amount: '2 260,00' },
      { bucket: IKZE, account: IKZE_ACCOUNT, group: '', amount: '2 304,00' },
      { bucket: INVESTMENTS, account: 'bez konta', group: '', amount: '1 979,27' },
    ]);
    await expect(page.getByTestId('transfer-plan-set-target-link')).toHaveText(
      'Ustaw alokację docelową',
    );
  });

  await test.step('Krok 2: the link opens /allocation and the targets 50, 40 and 10 show the deviations', async () => {
    await page.getByTestId('transfer-plan-set-target-link').click();
    await expect(page).toHaveURL(/\/allocation$/);
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(page.getByTestId('editor-row')).toHaveCount(3);
    for (const [index, name] of ['Akcje/ETF', 'Obligacje', 'Kruszce'].entries()) {
      await expect(allocationRow(page, index).getByTestId('group-name')).toHaveValue(name);
    }
    await allocationRow(page, 0).getByTestId('group-target').fill('50');
    await allocationRow(page, 1).getByTestId('group-target').fill('40');
    await allocationRow(page, 2).getByTestId('group-target').fill('10');
    await page.getByTestId('save').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(3);
    const values = ['110 000,00', '60 000,00', '12 000,00'];
    for (const [index, value] of values.entries()) {
      await expect(page.getByTestId('group-row').nth(index).locator('td').nth(1)).toHaveText(
        exactPln(value),
      );
    }
  });

  await test.step('Krok 3: back on M the rows go to groups, IKE is above target and the valuation date is today', async () => {
    await expectRowsProjected(page, planRows(page), STEP_3_ROWS);
    await expect(page.getByTestId('transfer-valuation-date')).toContainText(
      dateInText(warsawDate()),
    );
  });

  await test.step('Krok 4: the groups table shows the shares before and after the plan and the section has no kup or sprzedaj', async () => {
    await expectGroups(page, STEP_3_GROUPS);
    await expect(page.getByTestId('transfer-plan-section')).not.toContainText(/kup|sprzedaj/i);
  });

  await test.step('Krok 5: the minimum 900 zł leaves one Obligacje row of 1 979,27 zł for Inwestycje', async () => {
    await setMinimum(page, '900');
    await expectRows(planRows(page), [
      STEP_3_ROWS[0]!,
      STEP_3_ROWS[1]!,
      { bucket: INVESTMENTS, account: 'bez konta', group: 'Obligacje', amount: '1 979,27' },
    ]);
    await expectGroups(page, [
      { name: 'Akcje/ETF', current: '60,4', after: '59,5', target: '50' },
      { name: 'Obligacje', current: '33,0', after: '34,1', target: '40' },
      { name: 'Kruszce', current: '6,6', after: '6,4', target: '10' },
    ]);
  });

  await test.step('Krok 6: the minimum 2 000 zł marks the Inwestycje row poniżej minimum', async () => {
    await setMinimum(page, '2000');
    await expectRows(planRows(page), [
      STEP_3_ROWS[0]!,
      STEP_3_ROWS[1]!,
      {
        bucket: INVESTMENTS,
        account: 'bez konta',
        group: 'Obligacje',
        amount: '1 979,27',
        marker: 'poniżej minimum',
      },
    ]);
  });

  await test.step('Krok 7: the minimum 100 zł brings back the rows of Krok 3', async () => {
    await setMinimum(page, '100');
    await expectRows(planRows(page), STEP_3_ROWS);
  });

  await test.step('Krok 8: Pomiń skips the month without rows and Cofnij pominięcie brings back the preview', async () => {
    await page.getByTestId('transfer-skip').click();
    await expectStatus(page, 'Pominięty');
    await expect(page.getByTestId('transfer-plan-skipped')).toBeVisible();
    await expect(planRows(page)).toHaveCount(0);
    await page.getByTestId('transfer-unskip').click();
    await expectStatus(page, 'Podgląd');
    await expectRows(planRows(page), STEP_3_ROWS);
  });

  await test.step('Krok 9: Przyjmij plan freezes the rows with check boxes and the valuation date', async () => {
    await page.getByTestId('transfer-accept').click();
    await expectStatus(page, 'Przyjęty');
    await expectRows(planRows(page), STEP_3_ROWS);
    await expect(page.getByTestId('transfer-row-check')).toHaveCount(4);
    await expect(page.getByTestId('transfer-valuation-date')).toContainText(
      dateInText(warsawDate()),
    );
  });

  await test.step('Krok 10: Cofnij przyjęcie goes back to the preview and Przyjmij plan accepts again', async () => {
    await confirmUndo(page);
    await expectStatus(page, 'Podgląd');
    await expect(page.getByTestId('transfer-row-check')).toHaveCount(0);
    await page.getByTestId('transfer-accept').click();
    await expectStatus(page, 'Przyjęty');
  });

  await test.step('Krok 11: after renaming the group Kruszce to Metale and the account IKE mBank to IKE the accepted rows still show Kruszce and IKE mBank', async () => {
    await openAllocation(page);
    await page.getByTestId('edit').click();
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await allocationRow(page, 2).getByTestId('group-name').fill('Metale');
    await page.getByTestId('save').click();
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(
      page.getByTestId('group-row').nth(2).locator('.allocation-page__group-name'),
    ).toHaveText('Metale');

    await navigation(page)
      .getByRole('link', { name: 'Rachunki inwestycyjne', exact: true })
      .click();
    await page
      .getByRole('row')
      .filter({ has: page.getByRole('link', { name: IKE_ACCOUNT, exact: true }) })
      .getByRole('button', { name: `Akcje dla: ${IKE_ACCOUNT}`, exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Edytuj', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill('IKE');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('link', { name: 'IKE', exact: true })).toBeVisible();

    await openMonth(page, M);
    await expectStatus(page, 'Przyjęty');
    await expectRows(planRows(page), STEP_3_ROWS);
  });

  await test.step('Krok 12: checking the IKE row blocks Cofnij przyjęcie and the status stays Przyjęty', async () => {
    await transferRow(page, IKE).getByTestId('transfer-row-check').check();
    await expect(page.getByTestId('transfer-undo')).toBeDisabled();
    await expect(page.getByTestId('transfer-undo-hint')).toBeVisible();
    await expectStatus(page, 'Przyjęty');
  });

  await test.step('Krok 13: settling 800+ at 1 000 zł gives Inwestycje 2 179,27 zł, the difference +200 zł and the top-up preview of 200 zł', async () => {
    await settle(page, '800+', '1000');
    await expectBucket(page, INVESTMENTS, '2 179,27');
    await expect(page.getByTestId('transfer-difference')).toHaveCount(1);
    await expect(page.getByTestId('transfer-difference')).toContainText(INVESTMENTS);
    await expect(page.getByTestId('transfer-difference-amount')).toContainText(pln('200,00'));
    await expectRows(topUpRows(page), [
      { bucket: INVESTMENTS, account: 'bez konta', group: 'Obligacje', amount: '200,00' },
    ]);
    await expectStatus(page, 'Przyjęty');
  });

  await test.step('Krok 14: Przyjmij dopłatę adds the separate top-up rows and the IKE row stays checked', async () => {
    await page.getByTestId('transfer-top-up-accept').click();
    await expect(page.getByTestId('transfer-top-up')).toHaveCount(0);
    await expect(page.getByTestId('transfer-batch-title')).toHaveText(['Plan', 'Dopłata 1']);
    await expectRows(planRows(page), [
      ...STEP_3_ROWS,
      { bucket: INVESTMENTS, account: 'bez konta', group: 'Obligacje', amount: '200,00' },
    ]);
    await expect(transferRow(page, IKE).getByTestId('transfer-row-check')).toBeChecked();
    await expectStatus(page, 'Przyjęty');
  });

  await test.step('Krok 15: Cofnij przyjęcie removes only the top-up and Przyjmij dopłatę brings it back', async () => {
    await confirmUndo(page);
    await expect(page.getByTestId('transfer-batch-title')).toHaveText(['Plan']);
    await expectRows(planRows(page), STEP_3_ROWS);
    await expectRows(topUpRows(page), [
      { bucket: INVESTMENTS, account: 'bez konta', group: 'Obligacje', amount: '200,00' },
    ]);
    await page.getByTestId('transfer-top-up-accept').click();
    await expect(page.getByTestId('transfer-batch-title')).toHaveText(['Plan', 'Dopłata 1']);
    await expect(page.getByTestId('transfer-top-up')).toHaveCount(0);
  });

  await test.step('Krok 16: checking the remaining rows completes the plan', async () => {
    const checks = page.getByTestId('transfer-row-check');
    await expect(checks).toHaveCount(5);
    for (let index = 0; index < 5; index++) {
      await checks.nth(index).check();
    }
    await expectStatus(page, 'Zrealizowany');
  });

  await test.step('Krok 17: unchecking the top-up row gives Przyjęty and checking it again Zrealizowany', async () => {
    const topUpCheck = page.getByTestId('transfer-batch').nth(1).getByTestId('transfer-row-check');
    await topUpCheck.uncheck();
    await expectStatus(page, 'Przyjęty');
    await topUpCheck.check();
    await expectStatus(page, 'Zrealizowany');
  });

  await test.step('Krok 18: undoing the settlement of 800+ gives Inwestycje 1 979,27 zł and the difference -200 zł as information only', async () => {
    await occurrences(page, '800+').getByTestId('undo-occurrence').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expectBucket(page, INVESTMENTS, '1 979,27');
    await expect(page.getByTestId('transfer-difference')).toHaveCount(1);
    await expect(page.getByTestId('transfer-difference-amount')).toContainText(
      money('-200,00', 'zł'),
    );
    await expect(page.getByTestId('transfer-difference-info')).toBeVisible();
    await expect(page.getByTestId('transfer-top-up')).toHaveCount(0);
    await expectStatus(page, 'Zrealizowany');
  });

  await test.step('Krok 19: M+1 has no transfer plan', async () => {
    await openMonth(page, shiftMonth(M, 1));
    await expect(page.getByTestId('allocation-section')).toBeVisible();
    await expect(page.getByTestId('transfer-plan-section')).toHaveCount(0);
  });

  await test.step('Krok 20: in English the section is translated and the bucket, account and group names stay', async () => {
    await openMonth(page, M);
    await page.getByRole('button', { name: 'Język' }).click();
    await page.getByRole('menuitemradio', { name: 'English' }).click();
    const section = page.getByTestId('transfer-plan-section');
    await expect(section.getByRole('heading', { name: 'Transfer plan' })).toBeVisible();
    await expectStatus(page, 'Completed');
    const ike = transferRow(page, IKE);
    await expect(ike).toContainText('IKE mBank');
    await expect(ike).toContainText('Akcje/ETF');
    const investments = planRows(page).filter({
      has: page.locator('.planning-definition__name', { hasText: exactly(INVESTMENTS) }),
    });
    await expect(investments.filter({ hasText: 'Kruszce' })).toHaveCount(1);
    await expect(section).toContainText('no account');
  });
});
