import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, openSelect, test, warsawDate } from './fixtures';
import { addContribution, clearContributions } from './groups';
import {
  addExpense,
  addIncome,
  exactly,
  expensePayments,
  money,
  occurrences,
  openAddExpenseDialog,
  openMonth,
  pln,
  settle,
  shiftMonth,
} from './planning';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const ASSET_CLASS_STOCK = 2;
const TYPE_BUY = 0;
const TYPE_DEPOSIT = 2;
const TYPE_WITHDRAW = 3;

const RETIREMENT = 'Emerytura';
const HOME = 'Dom';
const HOLIDAY = 'Wakacje';
const MAIN = 'Główne';
const RESERVE = 'Rezerwa';
const BROKER = 'Maklerskie';
const SALARY = 'Wypłata';
const RENT = 'Czynsz';
const REPAIR = 'Naprawa';

const CURRENT = warsawDate().slice(0, 7);
const PREVIOUS = shiftMonth(CURRENT, -1);

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function amountOf(text: string): number {
  return Number(
    text
      .replace(/[^\d,\-−]/g, '')
      .replace('−', '-')
      .replace(',', '.'),
  );
}

function plnText(value: number): string {
  const [whole, cents] = Math.abs(value).toFixed(2).split('.') as [string, string];
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  return `${value < 0 ? '-' : ''}${grouped},${cents}`;
}

function exactMoney(amount: string): RegExp {
  return new RegExp(`^\\s*${money(amount, 'zł').source}\\s*$`);
}

function progress(funded: string, plan: string): RegExp {
  return new RegExp(`^\\s*${pln(funded).source}\\s/\\s${pln(plan).source}\\s*$`);
}

async function createGroup(
  page: Page,
  group: { name: string; color: string; contribution: string },
): Promise<void> {
  await page.getByTestId('add-group').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Dodaj grupę' })).toBeVisible();
  await dialog.getByLabel('Nazwa').fill(group.name);
  await dialog.getByRole('radio', { name: group.color }).click();
  await addContribution(page, dialog, { amount: group.contribution });
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

function groupRow(page: Page, name: string): Locator {
  return page
    .getByTestId('group-card')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

function contributionRow(page: Page, name: string): Locator {
  return page
    .getByTestId('contribution-row')
    .filter({ has: page.getByTestId('row-name').filter({ hasText: exactly(name) }) });
}

async function expectContribution(
  page: Page,
  name: string,
  funded: string,
  plan: string,
  status: string,
): Promise<void> {
  const row = contributionRow(page, name);
  await expect(row).toHaveCount(1);
  await expect(row.locator('.day-list__amount')).toHaveText(progress(funded, plan));
  await expect(row.getByTestId('contribution-status')).toContainText(status);
}

async function expectFigures(
  page: Page,
  figures: { plan?: string; actual: string },
): Promise<void> {
  if (figures.plan) {
    await expect(page.getByTestId('free-funds-plan').locator('.cascade__amount')).toHaveText(
      exactMoney(figures.plan),
    );
  }
  await expect(page.getByTestId('free-funds-actual').locator('.cascade__amount')).toHaveText(
    exactMoney(figures.actual),
  );
}

async function figure(page: Page, testId: string): Promise<number> {
  return amountOf(await page.getByTestId(testId).locator('.cascade__amount').innerText());
}

interface Created {
  id: string;
  cashAssetId: string;
}

async function post(
  request: APIRequestContext,
  token: string,
  url: string,
  data: unknown,
): Promise<Created> {
  const response = await request.post(url, { headers: authorization(token), data });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as Created;
}

async function bankAccount(
  request: APIRequestContext,
  token: string,
  name: string,
  groupId?: string,
): Promise<Created> {
  return post(request, token, '/api/portfolio/accounts', {
    name,
    currency: 'PLN',
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
    groupId,
  });
}

async function cashTransaction(
  request: APIRequestContext,
  token: string,
  assetId: string,
  transaction: { type: number; quantity: number; date: string },
): Promise<string> {
  const created = await post(request, token, `/api/portfolio/assets/${assetId}/transactions`, {
    ...transaction,
    unitPrice: 1,
  });
  return created.id;
}

async function transferFromMain(
  request: APIRequestContext,
  token: string,
  main: Created,
  target: Created,
  amount: number,
): Promise<string> {
  const created = await post(request, token, '/api/portfolio/transfers', {
    sourceAssetId: main.cashAssetId,
    targetAssetId: target.cashAssetId,
    amount,
    date: warsawDate(),
  });
  return (created as unknown as { transferId: string }).transferId;
}

async function settleExpense(page: Page, name: string, amount: string): Promise<void> {
  const row = expensePayments(page, name);
  await row.getByTestId('settle-expense').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill(amount);
  const save = dialog.getByRole('button', { name: 'Zapisz' });
  const account = dialog.getByRole('combobox', { name: 'Konto', exact: true });
  if ((await save.isDisabled()) && (await account.count()) > 0) {
    await openSelect(account);
    await page.getByRole('option', { name: new RegExp(`^${MAIN}`) }).click();
  }
  await save.click();
  await expect(dialog).toBeHidden();
  await expect(row.getByTestId('expense-status').locator('.status-chip__label')).toHaveText(
    'rozliczone',
  );
}

test('rozliczanie wpłat do grup: group funding, status and Faktyczne free funds in the month, year and dashboard views', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  await page.goto('/login');
  await logInThroughUi(page, user);
  const today = warsawDate();
  const groupIds = new Map<string, string>();
  let main: Created = { id: '', cashAssetId: '' };
  let reserve: Created = { id: '', cashAssetId: '' };
  let broker: Created = { id: '', cashAssetId: '' };
  let extraTransferId = '';

  await test.step('Krok 1: groups Emerytura 1000 zł, Dom 500 zł and Wakacje 300 zł are saved', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    await createGroup(page, { name: RETIREMENT, color: 'Fioletowy', contribution: '1000' });
    await createGroup(page, { name: HOME, color: 'Niebieski', contribution: '500' });
    await createGroup(page, { name: HOLIDAY, color: 'Zielony', contribution: '300' });
    await expect(page.getByTestId('group-card')).toHaveCount(3);
    await expect(groupRow(page, HOLIDAY).getByTestId('group-contribution')).toContainText(
      pln('300,00'),
    );
    const listed = await request.get('/api/portfolio/asset-groups', {
      headers: authorization(token),
    });
    expect(listed.ok()).toBeTruthy();
    for (const group of (await listed.json()) as { id: string; name: string }[]) {
      groupIds.set(group.name, group.id);
    }
  });

  await test.step('Krok 2: bank accounts Główne and Rezerwa (group Dom) and the brokerage Maklerskie (group Emerytura) exist', async () => {
    main = await bankAccount(request, token, MAIN);
    reserve = await bankAccount(request, token, RESERVE, groupIds.get(HOME));
    broker = await post(request, token, '/api/portfolio/accounts', {
      name: BROKER,
      currency: 'PLN',
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_REGULAR,
      groupId: groupIds.get(RETIREMENT),
    });
    const listed = await request.get('/api/portfolio/accounts', { headers: authorization(token) });
    const names = ((await listed.json()) as { name: string }[]).map((account) => account.name);
    expect(names.sort()).toEqual([MAIN, BROKER, RESERVE].sort());
  });

  await test.step('Krok 3: deposits dated last month give Główne 12 000 zł and Rezerwa 1 000 zł', async () => {
    const lastMonth = `${PREVIOUS}-15`;
    await cashTransaction(request, token, main.cashAssetId, {
      type: TYPE_DEPOSIT,
      quantity: 12000,
      date: lastMonth,
    });
    await cashTransaction(request, token, reserve.cashAssetId, {
      type: TYPE_DEPOSIT,
      quantity: 1000,
      date: lastMonth,
    });
    const listed = await request.get('/api/portfolio/accounts', { headers: authorization(token) });
    const accounts = (await listed.json()) as { name: string; cashBalance: number }[];
    expect(accounts.find((account) => account.name === MAIN)?.cashBalance).toBe(12000);
    expect(accounts.find((account) => account.name === RESERVE)?.cashBalance).toBe(1000);
  });

  await test.step('Krok 4: Wypłata 8000 zł, Czynsz 2000 zł and the one-off Naprawa 500 zł show in the month view', async () => {
    await openMonth(page, CURRENT);
    await addIncome(page, {
      name: SALARY,
      amount: '8000',
      account: MAIN,
      start: `${CURRENT}-01`,
      interval: '1',
      day: 10,
    });
    await addExpense(page, {
      name: RENT,
      amount: '2000',
      start: `${CURRENT}-01`,
      interval: '1',
      day: 1,
    });
    const dialog = await openAddExpenseDialog(page);
    await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
    await dialog.getByLabel('Nazwa').fill(REPAIR);
    await dialog.getByRole('spinbutton', { name: 'Kwota', exact: true }).fill('500');
    await dialog.getByLabel('Data', { exact: true }).fill(`${CURRENT}-20`);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(occurrences(page, SALARY)).toHaveCount(1);
    await expect(expensePayments(page, RENT)).toHaveCount(1);
    await expect(expensePayments(page, REPAIR)).toHaveCount(1);
  });

  await test.step('Krok 5: free funds show Plan 3 700 zł and Faktyczne 0 zł without Prognoza, every group is 0 / plan and Nierozliczona', async () => {
    await openMonth(page, CURRENT);
    await expectFigures(page, { plan: '3 700,00', actual: '0,00' });
    await expect(page.getByTestId('free-funds-forecast')).toHaveCount(0);
    await expect(page.getByTestId('free-funds').getByText('Prognoza')).toHaveCount(0);
    await expectContribution(page, RETIREMENT, '0,00', '1 000,00', 'Nierozliczona');
    await expectContribution(page, HOME, '0,00', '500,00', 'Nierozliczona');
    await expectContribution(page, HOLIDAY, '0,00', '300,00', 'Nierozliczona');
  });

  let plan = 0;

  await test.step('Krok 6: settling Wypłata at 8000 zł and Czynsz at 2000 zł gives Faktyczne 6 000 zł and keeps Plan', async () => {
    await settle(page, SALARY, '8000');
    await settleExpense(page, RENT, '2000');
    await openMonth(page, CURRENT);
    await expectFigures(page, { plan: '3 700,00', actual: '6 000,00' });
  });

  await test.step('Krok 7: a 600 zł transfer from Główne to Maklerskie gives Emerytura 600 / 1 000 zł Częściowa and Faktyczne 5 400 zł', async () => {
    await transferFromMain(request, token, main, broker, 600);
    await openMonth(page, CURRENT);
    await expectContribution(page, RETIREMENT, '600,00', '1 000,00', 'Częściowa');
    await expectFigures(page, { actual: '5 400,00' });
  });

  await test.step('Krok 8: buying a 600 zł paper in Emerytura from the same account keeps 600 / 1 000 zł and Faktyczne 5 400 zł', async () => {
    const search = await request.get('/api/marketdata/instruments/search?q=CDR&assetClass=Stock', {
      headers: authorization(token),
    });
    expect(search.ok()).toBeTruthy();
    const found = (
      (await search.json()) as { results: { id: string | null; ticker: string }[] }
    ).results.find((result) => result.ticker === 'CDR.WA');
    expect(found?.id).toBeTruthy();
    await expect(async () => {
      const created = await request.post('/api/portfolio/assets', {
        headers: authorization(token),
        data: {
          accountId: broker.id,
          assetClass: ASSET_CLASS_STOCK,
          name: 'CDR.WA',
          currency: 'PLN',
          instrumentId: found?.id,
          groupId: groupIds.get(RETIREMENT),
          initialTransaction: { type: TYPE_BUY, quantity: 6, unitPrice: 100, date: today },
        },
      });
      expect(created.ok()).toBeTruthy();
    }).toPass({ timeout: 90_000, intervals: [2_000] });
    await openMonth(page, CURRENT);
    await expectContribution(page, RETIREMENT, '600,00', '1 000,00', 'Częściowa');
    await expectFigures(page, { actual: '5 400,00' });
  });

  await test.step('Krok 9: another 900 zł transfer gives Emerytura 1 000 / 1 000 zł Zrealizowana, the excess 500 zł and Faktyczne 4 500 zł', async () => {
    extraTransferId = await transferFromMain(request, token, main, broker, 900);
    await openMonth(page, CURRENT);
    await expectContribution(page, RETIREMENT, '1 000,00', '1 000,00', 'Zrealizowana');
    await expect(
      page.getByTestId('excess-row').filter({ hasText: `${RETIREMENT} — Zrealizowane` }),
    ).toContainText(pln('500,00'));
    await expectFigures(page, { actual: '4 500,00' });
  });

  await test.step('Krok 10: transferring 500 zł from Główne to Rezerwa gives Dom 500 / 500 zł Zrealizowana and Faktyczne 4 000 zł', async () => {
    await post(request, token, '/api/portfolio/transfers', {
      sourceAssetId: main.cashAssetId,
      targetAssetId: reserve.cashAssetId,
      amount: 500,
      date: today,
    });
    await openMonth(page, CURRENT);
    await expectContribution(page, HOME, '500,00', '500,00', 'Zrealizowana');
    await expectFigures(page, { actual: '4 000,00' });
  });

  await test.step('Krok 11: withdrawing 200 zł from Rezerwa changes nothing: Dom 500 / 500 zł Zrealizowana and Faktyczne 4 000 zł', async () => {
    await cashTransaction(request, token, reserve.cashAssetId, {
      type: TYPE_WITHDRAW,
      quantity: 200,
      date: today,
    });
    await openMonth(page, CURRENT);
    await expectContribution(page, HOME, '500,00', '500,00', 'Zrealizowana');
    await expectFigures(page, { actual: '4 000,00' });
  });

  await test.step('Krok 12: withdrawing another 600 zł changes nothing: Dom 500 / 500 zł Zrealizowana and Faktyczne 4 000 zł', async () => {
    await cashTransaction(request, token, reserve.cashAssetId, {
      type: TYPE_WITHDRAW,
      quantity: 600,
      date: today,
    });
    await openMonth(page, CURRENT);
    await expectContribution(page, HOME, '500,00', '500,00', 'Zrealizowana');
    await expectFigures(page, { actual: '4 000,00' });
  });

  await test.step('Krok 13: settling Naprawa at 450 zł lowers Faktyczne to 3 550 zł', async () => {
    await settleExpense(page, REPAIR, '450');
    await openMonth(page, CURRENT);
    await expectFigures(page, { actual: '3 550,00' });
    plan = await figure(page, 'free-funds-plan');
  });

  await test.step('Krok 14: setting the Wakacje contribution to 0 zł removes it from the list and raises Plan by 300 zł', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    await groupRow(page, HOLIDAY).getByTestId('group-menu').click();
    await page.getByTestId('edit-group').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Edytuj grupę' })).toBeVisible();
    await clearContributions(dialog);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await openMonth(page, CURRENT);
    await expect(contributionRow(page, HOLIDAY)).toHaveCount(0);
    await expect(page.getByTestId('free-funds-plan').locator('.cascade__amount')).toHaveText(
      exactMoney(plnText(plan + 300)),
    );
    await expectFigures(page, { actual: '3 550,00' });
  });

  await test.step('Krok 15: deleting the 900 zł transfer returns Emerytura to 600 / 1 000 zł Częściowa and raises Faktyczne by 900 zł', async () => {
    const removed = await request.delete(`/api/portfolio/transfers/${extraTransferId}`, {
      headers: authorization(token),
    });
    expect(removed.ok()).toBeTruthy();
    await openMonth(page, CURRENT);
    await expectContribution(page, RETIREMENT, '600,00', '1 000,00', 'Częściowa');
    await expectFigures(page, { actual: '4 450,00' });
  });

  await test.step('Krok 16: October has the contributions of Emerytura and Dom, both Nierozliczona, and no Prognoza', async () => {
    await openMonth(page, PREVIOUS);
    await expect(page.getByTestId('contribution-row')).toHaveCount(2);
    await expectContribution(page, RETIREMENT, '0,00', '1 000,00', 'Nierozliczona');
    await expectContribution(page, HOME, '0,00', '500,00', 'Nierozliczona');
    await expect(page.getByTestId('free-funds-forecast')).toHaveCount(0);
  });

  await test.step('Krok 17: the year view shows Wolne środki faktyczne instead of Prognoza and the current month matches the month view', async () => {
    await openMonth(page, CURRENT);
    const monthActual = await figure(page, 'free-funds-actual');
    await page.getByRole('tab', { name: 'Rok', exact: true }).click();
    await expect(page).toHaveURL(/tab=year/);
    await expect(page.getByRole('columnheader', { name: 'Wolne środki faktyczne' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: /Prognoza/ })).toHaveCount(0);
    await expect(page.getByTestId('year-chart-actual').first()).toBeAttached();
    const row = page.locator('[data-testid="year-row"][aria-current="date"]');
    await expect(row).toHaveCount(1);
    await expect(row.locator('td').nth(5)).toHaveText(exactMoney(plnText(monthActual)));
  });

  await test.step('Krok 18: the dashboard month card shows the Faktyczne free funds of the month view', async () => {
    await page.goto('/dashboard');
    const card = page.getByTestId('month-card-actual');
    await expect(card).toContainText('Faktyczne wolne środki:');
    await expect(card).toContainText(pln('4 450,00'));
  });
});
