import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test, warsawDate } from './fixtures';
import { addContribution } from './groups';
import {
  addIncome,
  choose,
  day,
  exactly,
  expectPlan,
  occurrences,
  openAddDialog,
  openMonth,
  pln,
  money,
  settle,
  shiftMonth,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TYPE_DEPOSIT = 2;

const A = 'A';
const B = 'B';
const P1 = 'P1';
const E1 = 'E1';
const RETIREMENT = 'Emerytura';
const CUSHION = 'Poduszka';
const SALARY = 'Pensja';
const BONUS = 'Premia';
const REFUND = 'Zwrot';

const NOVEMBER = warsawDate().slice(0, 7);
const TODAY = warsawDate();
const OCTOBER = shiftMonth(NOVEMBER, -1);

interface Created {
  id: string;
  cashAssetId: string;
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function exactMoney(amount: string): RegExp {
  return new RegExp(`^\\s*${money(amount, 'zł').source}\\s*$`);
}

async function bankAccount(
  request: APIRequestContext,
  token: string,
  name: string,
): Promise<Created> {
  const response = await request.post('/api/portfolio/accounts', {
    headers: authorization(token),
    data: {
      name,
      currency: 'PLN',
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as Created;
}

async function openingDeposit(
  request: APIRequestContext,
  token: string,
  account: Created,
  quantity: number,
): Promise<void> {
  const response = await request.post(`/api/portfolio/assets/${account.cashAssetId}/transactions`, {
    headers: authorization(token),
    data: { type: TYPE_DEPOSIT, quantity, unitPrice: 1, date: day(OCTOBER, 1) },
  });
  expect(response.ok()).toBeTruthy();
}

async function transfer(
  request: APIRequestContext,
  token: string,
  from: Created,
  to: Created,
  amount: number,
  date: string,
): Promise<void> {
  const response = await request.post('/api/portfolio/transfers', {
    headers: authorization(token),
    data: { sourceAssetId: from.cashAssetId, targetAssetId: to.cashAssetId, amount, date },
  });
  expect(response.ok()).toBeTruthy();
}

async function openGroups(page: Page): Promise<void> {
  await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
  await expect(page.getByTestId('add-group')).toBeVisible();
}

function groupCard(page: Page, name: string): Locator {
  return page
    .getByTestId('group-card')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

async function createGroup(page: Page, name: string, color: string): Promise<void> {
  await page.getByTestId('add-group').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Dodaj grupę' })).toBeVisible();
  await dialog.getByLabel('Nazwa').fill(name);
  await dialog.getByRole('radio', { name: color }).click();
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
  await expect(groupCard(page, name)).toBeVisible();
}

async function assignAccount(page: Page, group: string, account: string): Promise<void> {
  await groupCard(page, group).getByTestId('select-group').click();
  const panel = page.getByTestId('assets-panel');
  await expect(panel).toBeVisible();
  await panel
    .getByRole('row')
    .filter({ has: page.getByRole('cell', { name: account, exact: true }) })
    .getByRole('checkbox')
    .check();
  await panel.getByTestId('save-assignment').click();
  await expect(panel.getByTestId('assets-panel-title')).toHaveCount(0);
}

async function editGroup(page: Page, name: string): Promise<Locator> {
  await groupCard(page, name).getByTestId('group-menu').click();
  await page.getByTestId('edit-group').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj grupę' })).toBeVisible();
  return dialog;
}

function contributionRow(page: Page, name: string): Locator {
  return page
    .getByTestId('contribution-row')
    .filter({ has: page.getByTestId('row-name').filter({ hasText: exactly(name) }) });
}

async function expectStatus(page: Page, group: string, status: string): Promise<void> {
  const row = contributionRow(page, group);
  await expect(row).toHaveCount(1);
  await expect(row.getByTestId('contribution-status')).toContainText(status);
}

function depositRow(page: Page, group: string): Locator {
  return page.getByTestId('group-deposit-row').filter({ hasText: `Wpłata do grupy: ${group}` });
}

async function expectDeposit(page: Page, group: string, amount: string): Promise<void> {
  const row = depositRow(page, group);
  await expect(row).toHaveCount(1);
  await expect(row.locator('.cascade__amount')).toHaveText(exactMoney(amount));
}

function excessRow(page: Page, group: string): Locator {
  return page.getByTestId('excess-row').filter({ hasText: `${group} — Zrealizowane` });
}

async function expectFree(page: Page, figures: { plan?: string; actual?: string }): Promise<void> {
  if (figures.plan) {
    await expect(page.getByTestId('free-funds-plan').locator('.cascade__amount')).toHaveText(
      exactMoney(figures.plan),
    );
  }
  if (figures.actual) {
    await expect(page.getByTestId('free-funds-actual').locator('.cascade__amount')).toHaveText(
      exactMoney(figures.actual),
    );
  }
}

async function expectActualIncome(page: Page, amount: string): Promise<void> {
  await expect(page.getByTestId('balance-actual').locator('dd')).toContainText(pln(amount));
}

function oneOffIncomeRow(page: Page, name: string): Locator {
  return page.getByTestId('one-off-income-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

async function addOneOffIncome(
  page: Page,
  income: { name: string; amount: string; account: string; date: string; planned: boolean },
): Promise<void> {
  const dialog = await openAddDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${income.account}`),
  );
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
  const planned = dialog.getByRole('checkbox', { name: 'Planowany' });
  if (!income.planned) {
    await planned.uncheck();
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function setPlanned(page: Page, name: string, planned: boolean): Promise<void> {
  await oneOffIncomeRow(page, name).getByTestId('edit-one-off-income').click();
  const dialog = page.getByRole('dialog');
  const checkbox = dialog.getByRole('checkbox', { name: 'Planowany' });
  if (planned) {
    await checkbox.check();
  } else {
    await checkbox.uncheck();
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

test('wpłaty do grup: contributions from income accounts, transfers realising them in every month and unplanned incomes', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  await page.goto('/login');
  await logInThroughUi(page, user);
  const accounts = new Map<string, Created>();
  const account = (name: string): Created => accounts.get(name)!;

  await test.step('Krok 1: bank accounts A, B, P1 and E1 are on the list', async () => {
    for (const name of [A, B, P1, E1]) {
      accounts.set(name, await bankAccount(request, token, name));
    }
    await openingDeposit(request, token, account(A), 10_000);
    await openingDeposit(request, token, account(B), 5_000);
    const listed = await request.get('/api/portfolio/accounts', { headers: authorization(token) });
    const names = ((await listed.json()) as { name: string }[]).map((entry) => entry.name);
    expect(names.sort()).toEqual([A, B, E1, P1]);
  });

  await test.step('Krok 2: groups Emerytura (account E1) and Poduszka (account P1) have one account each', async () => {
    await openGroups(page);
    await createGroup(page, RETIREMENT, 'Fioletowy');
    await createGroup(page, CUSHION, 'Niebieski');
    await assignAccount(page, RETIREMENT, E1);
    await assignAccount(page, CUSHION, P1);
    await expect(page.getByTestId('group-card')).toHaveCount(2);
    await expect(groupCard(page, RETIREMENT).getByTestId('group-count')).toContainText('Aktywa: 1');
    await expect(groupCard(page, CUSHION).getByTestId('group-count')).toContainText('Aktywa: 1');
  });

  await test.step('Krok 3: Emerytura gets two contributions of 2 000 zł from A and B and the card shows 4 000 zł', async () => {
    const dialog = await editGroup(page, RETIREMENT);
    await addContribution(page, dialog, { amount: '2000', account: A });
    await addContribution(page, dialog, { amount: '2000', account: B });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(groupCard(page, RETIREMENT).getByTestId('group-contribution')).toContainText(
      pln('4 000,00'),
    );
  });

  await test.step('Krok 4: the recurring income Pensja 10 000 zł on A, day 10, gives Plan 10 000 zł', async () => {
    await openMonth(page, NOVEMBER);
    await addIncome(page, {
      name: SALARY,
      amount: '10000',
      account: A,
      start: day(NOVEMBER, 1),
      interval: '1',
      day: 10,
    });
    await expect(occurrences(page, SALARY)).toHaveCount(1);
    await expectPlan(page, '10 000,00');
  });

  await test.step('Krok 5: Plan free funds drop by 2 000 zł only and Emerytura is Nierozliczona', async () => {
    await openMonth(page, NOVEMBER);
    await expectFree(page, { plan: '8 000,00' });
    await expectStatus(page, RETIREMENT, 'Nierozliczona');
  });

  await test.step('Krok 6: settling Pensja at 10 000 zł on A gives Faktycznie incomes 10 000 zł', async () => {
    await settle(page, SALARY, '10000');
    await openMonth(page, NOVEMBER);
    await expectActualIncome(page, '10 000,00');
  });

  await test.step('Krok 7: a transfer of 2 000 zł from A to E1 dated today is saved', async () => {
    await transfer(request, token, account(A), account(E1), 2000, TODAY);
  });

  await test.step('Krok 8: Emerytura is Częściowa, the Faktycznie row is 2 000 zł and free funds 8 000 zł', async () => {
    await openMonth(page, NOVEMBER);
    await expectStatus(page, RETIREMENT, 'Częściowa');
    await expectDeposit(page, RETIREMENT, '2 000,00');
    await expectFree(page, { actual: '8 000,00' });
  });

  await test.step('Krok 9: a transfer of 2 000 zł from B to E1 makes Emerytura Zrealizowana, row 4 000 zł, free funds still 8 000 zł', async () => {
    await transfer(request, token, account(B), account(E1), 2000, TODAY);
    await openMonth(page, NOVEMBER);
    await expectStatus(page, RETIREMENT, 'Zrealizowana');
    await expectDeposit(page, RETIREMENT, '4 000,00');
    await expectFree(page, { actual: '8 000,00' });
  });

  await test.step('Krok 10: a transfer of 2 000 zł from A to P1 gives the undated Poduszka — Zrealizowane 2 000 zł, row 2 000 zł, free funds 6 000 zł', async () => {
    await transfer(request, token, account(A), account(P1), 2000, TODAY);
    await openMonth(page, NOVEMBER);
    await expect(excessRow(page, CUSHION)).toHaveCount(1);
    await expect(excessRow(page, CUSHION).locator('.day-list__amount')).toHaveText(
      exactMoney('2 000,00'),
    );
    await expect(
      page.getByTestId('no-date-block').getByTestId('excess-row').filter({ hasText: CUSHION }),
    ).toHaveCount(1);
    await expectDeposit(page, CUSHION, '2 000,00');
    await expectFree(page, { actual: '6 000,00' });
  });

  await test.step('Krok 11: a transfer of 500 zł from E1 to P1 leaves Zrealizowane at 2 000 zł, row 2 500 zł, free funds 6 000 zł', async () => {
    await transfer(request, token, account(E1), account(P1), 500, TODAY);
    await openMonth(page, NOVEMBER);
    await expect(excessRow(page, CUSHION).locator('.day-list__amount')).toHaveText(
      exactMoney('2 000,00'),
    );
    await expectDeposit(page, CUSHION, '2 500,00');
    await expectFree(page, { actual: '6 000,00' });
  });

  await test.step('Krok 12: in the previous month a transfer of 2 000 zł from A to E1 makes the contribution Częściowa with a 2 000 zł row', async () => {
    await transfer(request, token, account(A), account(E1), 2000, day(OCTOBER, 15));
    await openMonth(page, OCTOBER);
    await expectStatus(page, RETIREMENT, 'Częściowa');
    await expectDeposit(page, RETIREMENT, '2 000,00');
  });

  await test.step('Krok 13: an unplanned one-off Premia 3 000 zł on A, 15th, keeps Plan 10 000 zł and has the chip and the calendar mark', async () => {
    await openMonth(page, NOVEMBER);
    await addOneOffIncome(page, {
      name: BONUS,
      amount: '3000',
      account: A,
      date: day(NOVEMBER, 15),
      planned: false,
    });
    await openMonth(page, NOVEMBER);
    await expectPlan(page, '10 000,00');
    await expect(occurrences(page, BONUS).getByTestId('unplanned-mark')).toHaveText('Nieplanowany');
    await page.getByTestId('layout-calendar').click();
    await expect(
      page
        .locator(`[data-testid="calendar-cell"][data-date="${day(NOVEMBER, 15)}"]`)
        .getByTestId('unplanned-mark'),
    ).toHaveCount(1);
    await page.getByTestId('layout-list').click();
  });

  await test.step('Krok 14: settling Premia at 3 000 zł gives Faktycznie incomes 13 000 zł and difference 3 000 zł', async () => {
    await settle(page, BONUS, '3000');
    await openMonth(page, NOVEMBER);
    await expectActualIncome(page, '13 000,00');
    await expect(page.getByTestId('balance-difference').locator('dd')).toContainText(
      pln('3 000,00'),
    );
  });

  await test.step('Krok 15: marking the settled Premia Planowany gives Plan 13 000 zł, Faktycznie 13 000 zł and no chip', async () => {
    await setPlanned(page, BONUS, true);
    await openMonth(page, NOVEMBER);
    await expectPlan(page, '13 000,00');
    await expectActualIncome(page, '13 000,00');
    await expect(occurrences(page, BONUS).getByTestId('unplanned-mark')).toHaveCount(0);
  });

  await test.step('Krok 16: clearing Planowany again gives Plan 10 000 zł and Faktycznie 13 000 zł', async () => {
    await setPlanned(page, BONUS, false);
    await openMonth(page, NOVEMBER);
    await expectPlan(page, '10 000,00');
    await expectActualIncome(page, '13 000,00');
  });

  await test.step('Krok 17: an unsettled unplanned Zwrot 1 000 zł on B changes neither Plan nor Faktycznie and free funds in Plan stay 8 000 zł', async () => {
    await addOneOffIncome(page, {
      name: REFUND,
      amount: '1000',
      account: B,
      date: day(NOVEMBER, 20),
      planned: false,
    });
    await openMonth(page, NOVEMBER);
    await expect(occurrences(page, REFUND)).toHaveCount(1);
    await expectPlan(page, '10 000,00');
    await expectActualIncome(page, '13 000,00');
    await expectFree(page, { plan: '8 000,00' });
  });

  await test.step('Krok 18: a 1 000 zł contribution without an account in Poduszka lowers Plan by 1 000 zł, Zrealizowane shows 1 000 zł and Poduszka is Zrealizowana', async () => {
    await openGroups(page);
    const dialog = await editGroup(page, CUSHION);
    await addContribution(page, dialog, { amount: '1000' });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await openMonth(page, NOVEMBER);
    await expectFree(page, { plan: '7 000,00' });
    await expect(excessRow(page, CUSHION).locator('.day-list__amount')).toHaveText(
      exactMoney('1 000,00'),
    );
    await expectStatus(page, CUSHION, 'Zrealizowana');
  });
});
