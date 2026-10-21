import { type Locator, type Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test } from './fixtures';
import {
  addIncome,
  choose,
  confirmDelete,
  dateInText,
  exactly,
  expectCost,
  expectFreeFunds,
  expectPlan,
  expensePayments,
  occurrences,
  openAddDialog,
  openAddExpenseDialog,
  openMonth,
  pln,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;

const BANK = 'mBank';
const SALARY = 'Wypłata';
const BONUS = 'Premia roczna';
const REFUND = 'Zwrot podatku';
const SERVICE = 'Przegląd auta';
const OCTOBER = '2026-10';
const NOVEMBER = '2026-11';
const DECEMBER = '2026-12';

function oneOffIncomeRow(page: Page, name: string): Locator {
  return page.getByTestId('one-off-income-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

function oneOffExpenseRow(page: Page, name: string): Locator {
  return page.getByTestId('one-off-expense-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

async function expectBalance(
  page: Page,
  balance: { plan: string; actual?: string; pending?: string },
): Promise<void> {
  await expectPlan(page, balance.plan);
  if (balance.actual) {
    await expect(page.getByTestId('balance-actual').locator('dd')).toContainText(
      pln(balance.actual),
    );
  }
  if (balance.pending) {
    await expect(page.getByTestId('balance-pending').locator('dd')).toContainText(
      pln(balance.pending),
    );
  }
}

function modeToggle(dialog: Locator, label: string): Locator {
  return dialog.getByRole('radio', { name: label, exact: true });
}

async function switchToOneOff(dialog: Locator): Promise<void> {
  await modeToggle(dialog, 'Jednorazowy').click();
  await expect(modeToggle(dialog, 'Jednorazowy')).toBeChecked();
}

async function expectOneOffIncomeFields(dialog: Locator): Promise<void> {
  await expect(dialog.getByLabel('Nazwa')).toBeVisible();
  await expect(dialog.getByRole('spinbutton', { name: 'Kwota' })).toBeVisible();
  await expect(dialog.getByRole('combobox', { name: 'Konto', exact: true })).toBeVisible();
  await expect(dialog.getByLabel('Data', { exact: true })).toBeVisible();
}

async function expectNoRuleFields(dialog: Locator): Promise<void> {
  await expect(dialog.getByRole('spinbutton', { name: 'Powtarzaj co (miesięcy)' })).toHaveCount(0);
  await expect(dialog.getByRole('combobox', { name: 'Dzień', exact: true })).toHaveCount(0);
  await expect(dialog.getByRole('spinbutton', { name: 'Dzień miesiąca' })).toHaveCount(0);
  await expect(dialog.getByRole('combobox', { name: 'Przesunięcie na dzień roboczy' })).toHaveCount(
    0,
  );
}

async function fillOneOffIncome(
  page: Page,
  dialog: Locator,
  income: { name: string; amount: string; date: string },
): Promise<void> {
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${BANK}`),
  );
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
}

async function addOneOffIncome(
  page: Page,
  income: { name: string; amount: string; date: string },
): Promise<void> {
  const dialog = await openAddDialog(page);
  await switchToOneOff(dialog);
  await fillOneOffIncome(page, dialog, income);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
  await expect(oneOffIncomeRow(page, income.name)).toHaveCount(1);
}

async function openOneOffExpenseDialog(page: Page): Promise<Locator> {
  const dialog = await openAddExpenseDialog(page);
  await switchToOneOff(dialog);
  return dialog;
}

async function openBankAccount(page: Page, name: string): Promise<void> {
  await page
    .locator('mat-sidenav')
    .getByRole('link', { name: 'Konta bankowe', exact: true })
    .click();
  await page.getByRole('link', { name, exact: true }).click();
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
}

function cash(page: Page): Locator {
  return page.getByTestId('account-cash');
}

function snackBar(page: Page): Locator {
  return page.locator('mat-snack-bar-container');
}

function overdueRows(page: Page): Locator {
  return page.getByTestId('overdue-row').filter({ hasText: REFUND });
}

function calendarCell(page: Page, date: string): Locator {
  return page.locator(`[data-testid="calendar-cell"][data-date="${date}"]`);
}

function yearRow(page: Page, month: string): Locator {
  return page.getByTestId('year-row').nth(Number(month.slice(5)) - 1);
}

test('one-off planning: one-off incomes and expenses next to the recurring ones', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(600_000);

  const token = await logInThroughApi(request, user);
  const created = await request.post('/api/portfolio/accounts', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name: BANK,
      currency: 'PLN',
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(created.ok()).toBeTruthy();

  await page.goto('/login');
  await logInThroughUi(page, user);
  await openMonth(page, OCTOBER);
  await addIncome(page, {
    name: SALARY,
    amount: '6000',
    account: BANK,
    start: '2026-10-01',
    interval: '1',
    day: 10,
  });

  await test.step('Krok 1: październik 2026 shows Wypłata 6 000 zł in Plan and Pending, Actual 0', async () => {
    await openMonth(page, OCTOBER);
    await expect(occurrences(page, SALARY)).toContainText(pln('6 000,00'));
    await expectBalance(page, { plan: '6 000,00', pending: '6 000,00', actual: '0,00' });
  });

  await test.step('Krok 2: Dodaj wpływ has the switch Cykliczny / Jednorazowy, Cykliczny by default, with the rule fields', async () => {
    const dialog = await openAddDialog(page);
    await expect(modeToggle(dialog, 'Cykliczny')).toBeChecked();
    await expect(modeToggle(dialog, 'Jednorazowy')).not.toBeChecked();
    await expect(dialog.getByRole('spinbutton', { name: 'Powtarzaj co (miesięcy)' })).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Dzień', exact: true })).toBeVisible();
    await expect(
      dialog.getByRole('combobox', { name: 'Przesunięcie na dzień roboczy' }),
    ).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 3: Jednorazowy keeps the name, amount, account and date and drops the rule fields', async () => {
    const dialog = await openAddDialog(page);
    await switchToOneOff(dialog);
    await expectOneOffIncomeFields(dialog);
    await expectNoRuleFields(dialog);
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 4: Premia roczna 8 000 zł on 15.12.2026 is on the incomes list marked jednorazowy with its date instead of a rule', async () => {
    await addOneOffIncome(page, { name: BONUS, amount: '8000', date: '2026-12-15' });
    const row = oneOffIncomeRow(page, BONUS);
    await expect(row.getByTestId('one-off-mark')).toHaveText('jednorazowy');
    await expect(row.getByTestId('one-off-date')).toHaveText(dateInText('2026-12-15'));
    await expect(row).toContainText(pln('8 000,00'));
    await expect(row.locator('.planning-definition__rule')).toHaveCount(0);
  });

  await test.step('Krok 5: grudzień 2026 has the bonus 8 000 zł in the occurrences, Plan 14 000 zł and Pending 14 000 zł', async () => {
    await openMonth(page, DECEMBER);
    await expect(occurrences(page, BONUS)).toContainText(pln('8 000,00'));
    await expectBalance(page, { plan: '14 000,00', pending: '14 000,00' });
  });

  await test.step('Krok 6: listopad 2026 has no bonus in the occurrences, Plan 6 000 zł and the bonus stays on the incomes list', async () => {
    await openMonth(page, NOVEMBER);
    await expect(occurrences(page, BONUS)).toHaveCount(0);
    await expectPlan(page, '6 000,00');
    await expect(oneOffIncomeRow(page, BONUS)).toHaveCount(1);
  });

  await test.step('Krok 7: Zwrot podatku 2 500 zł from 05.10.2026 gives październik Plan 8 500 zł, Pending 8 500 zł, an overdue row and a task on the dashboard card', async () => {
    await openMonth(page, OCTOBER);
    await addOneOffIncome(page, { name: REFUND, amount: '2500', date: '2026-10-05' });
    await expectBalance(page, { plan: '8 500,00', pending: '8 500,00' });
    await expect(overdueRows(page)).toHaveCount(1);
    const todo = page.waitForResponse(
      (response) => response.url().includes('/api/portfolio/planning/todo') && response.ok(),
    );
    await page.goto('/dashboard');
    await todo;
    const item = page.locator('app-month-card').getByTestId('month-card-todo-item');
    await expect(item.filter({ hasText: REFUND })).toHaveCount(1);
    await expect(item.filter({ hasText: REFUND })).toContainText(pln('2 500,00'));
  });

  await test.step('Krok 8: settling Zwrot podatku with the deposit gives rozliczone, Actual 2 500 zł, Pending 6 000 zł, no overdue row and 2 500 zł more on mBank', async () => {
    await openBankAccount(page, BANK);
    await expect(cash(page)).toContainText(pln('0,00'));
    await openMonth(page, OCTOBER);
    const row = occurrences(page, REFUND);
    await row.getByTestId('settle-occurrence').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill('2500');
    await dialog.getByRole('textbox', { name: 'Faktyczna data' }).fill('2026-10-05');
    await dialog.getByRole('checkbox', { name: 'Utwórz wpłatę na konto' }).check();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(row.getByTestId('occurrence-status')).toHaveText('rozliczone');
    await expectBalance(page, { plan: '8 500,00', actual: '2 500,00', pending: '6 000,00' });
    await expect(overdueRows(page)).toHaveCount(0);
    await openBankAccount(page, BANK);
    await expect(cash(page)).toContainText(pln('2 500,00'));
  });

  await test.step('Krok 9: editing and deleting the settled Zwrot podatku are blocked with the undo hint', async () => {
    await openMonth(page, OCTOBER);
    const row = oneOffIncomeRow(page, REFUND);
    await expect(row.getByTestId('edit-one-off-income')).toBeDisabled();
    await expect(row.getByTestId('delete-one-off-income')).toBeDisabled();
    await expect(row.getByTestId('one-off-locked-hint')).toHaveText(
      'Cofnij rozliczenie, aby edytować lub usunąć wpis.',
    );
  });

  await test.step('Krok 10: undoing the settlement brings back Pending and the overdue row and takes the deposit off mBank', async () => {
    const occurrence = occurrences(page, REFUND);
    await occurrence.getByTestId('undo-occurrence').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(occurrence.getByTestId('occurrence-status')).toHaveText('oczekuje');
    await expectBalance(page, { plan: '8 500,00', actual: '0,00', pending: '8 500,00' });
    await expect(overdueRows(page)).toHaveCount(1);
    await openBankAccount(page, BANK);
    await expect(cash(page)).toContainText(pln('0,00'));
  });

  await test.step('Krok 11: editing Zwrot podatku to 2 600 zł gives październik Plan 8 600 zł, and the edit window has no mode switch', async () => {
    await openMonth(page, OCTOBER);
    await oneOffIncomeRow(page, REFUND).getByTestId('edit-one-off-income').click();
    const dialog = page.getByRole('dialog');
    await expect(modeToggle(dialog, 'Jednorazowy')).toHaveCount(0);
    await expect(modeToggle(dialog, 'Cykliczny')).toHaveCount(0);
    await expectOneOffIncomeFields(dialog);
    await expectNoRuleFields(dialog);
    await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('2600');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expectPlan(page, '8 600,00');
  });

  await test.step('Krok 12: a one-off expense has name, amount and date without an account, and Przegląd auta 1 200 zł on 10.11.2026 is on the expenses list with its mark and date', async () => {
    const dialog = await openOneOffExpenseDialog(page);
    await expect(dialog.getByLabel('Nazwa')).toBeVisible();
    await expect(dialog.getByRole('spinbutton', { name: 'Kwota (PLN)' })).toBeVisible();
    await expect(dialog.getByLabel('Data', { exact: true })).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Konto', exact: true })).toHaveCount(0);
    await expectNoRuleFields(dialog);
    await dialog.getByLabel('Nazwa').fill(SERVICE);
    await dialog.getByRole('spinbutton', { name: 'Kwota (PLN)' }).fill('1200');
    await dialog.getByLabel('Data', { exact: true }).fill('2026-11-10');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    const row = oneOffExpenseRow(page, SERVICE);
    await expect(row).toHaveCount(1);
    await expect(row.getByTestId('one-off-mark')).toHaveText('jednorazowy');
    await expect(row.getByTestId('one-off-date')).toHaveText(dateInText('2026-11-10'));
    await expect(row).toContainText(pln('1 200,00'));
  });

  await test.step('Krok 13: listopad 2026 has the expense cost 1 200 zł, free funds 4 800 zł and no settling or paid status on the expense', async () => {
    await openMonth(page, NOVEMBER);
    await expectCost(page, '1 200,00');
    await expectFreeFunds(page, '4 800,00');
    const payment = expensePayments(page, SERVICE);
    await expect(payment).toHaveCount(1);
    await expect(payment.getByRole('button')).toHaveCount(0);
    await expect(payment).not.toContainText('zapłacon');
    await expect(payment).not.toContainText('rozliczone');
  });

  await test.step('Krok 14: październik 2026 has the expense cost 0 zł because a one-off expense reserves nothing earlier', async () => {
    await openMonth(page, OCTOBER);
    await expectCost(page, '0,00');
  });

  await test.step('Krok 15: the Rok tab shows 1 200 zł of cost in listopad and the plan 14 000 zł (8 000 zł more) in grudzień', async () => {
    await page.getByRole('tab', { name: 'Rok', exact: true }).click();
    await expect(page).toHaveURL(/tab=year/);
    await expect(page.getByTestId('year-label')).toHaveText('2026');
    await expect(page.getByTestId('year-row')).toHaveCount(12);
    const november = yearRow(page, NOVEMBER).locator('td');
    await expect(november.nth(0)).toContainText(pln('6 000,00'));
    await expect(november.nth(3)).toContainText(pln('1 200,00'));
    await expect(yearRow(page, DECEMBER).locator('td').nth(0)).toContainText(pln('14 000,00'));
  });

  await test.step('Krok 16: moving Przegląd auta to 12.12.2026 takes it out of listopad and puts it in grudzień', async () => {
    await openMonth(page, OCTOBER);
    await oneOffExpenseRow(page, SERVICE).getByTestId('edit-one-off-expense').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Data', { exact: true }).fill('2026-12-12');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await openMonth(page, NOVEMBER);
    await expect(expensePayments(page, SERVICE)).toHaveCount(0);
    await expectCost(page, '0,00');
    await openMonth(page, DECEMBER);
    await expect(expensePayments(page, SERVICE)).toHaveCount(1);
    await expectCost(page, '1 200,00');
  });

  await test.step('Krok 17: a one-off income dated 01.01.2100 shows a validation error at the date and is not created', async () => {
    await openMonth(page, OCTOBER);
    const dialog = await openAddDialog(page);
    await switchToOneOff(dialog);
    await fillOneOffIncome(page, dialog, {
      name: 'Zła data',
      amount: '100',
      date: '2100-01-01',
    });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(
      dialog.getByText('Data musi mieścić się między 2011-01-01 a 2099-12-31.'),
    ).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(oneOffIncomeRow(page, 'Zła data')).toHaveCount(0);
  });

  await test.step('Krok 18: the calendar of grudzień 2026 shows the bonus on 15.12 and Przegląd auta on 12.12', async () => {
    await openMonth(page, DECEMBER);
    await page.getByTestId('layout-calendar').click();
    await expect(page.getByTestId('planning-calendar')).toBeVisible();
    const bonus = calendarCell(page, '2026-12-15')
      .getByTestId('calendar-item')
      .filter({ hasText: BONUS });
    await expect(bonus).toHaveCount(1);
    await expect(bonus).toContainText(pln('8 000,00'));
    const service = calendarCell(page, '2026-12-12')
      .getByTestId('calendar-item')
      .filter({ hasText: SERVICE });
    await expect(service).toHaveCount(1);
    await expect(service).toContainText(pln('1 200,00'));
  });

  await test.step('Krok 19: deleting Przegląd auta removes it from the list and from grudzień', async () => {
    await page.getByTestId('layout-list').click();
    await expect(page.getByTestId('layout-list')).toHaveAttribute('aria-pressed', 'true');
    await oneOffExpenseRow(page, SERVICE).getByTestId('delete-one-off-expense').click();
    await confirmDelete(page);
    await expect(oneOffExpenseRow(page, SERVICE)).toHaveCount(0);
    await expect(expensePayments(page, SERVICE)).toHaveCount(0);
    await expectCost(page, '0,00');
  });

  await test.step('Krok 20: deleting the account mBank is refused because incomes point at it', async () => {
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Konta bankowe', exact: true })
      .click();
    await page.getByRole('button', { name: `Akcje dla: ${BANK}`, exact: true }).click();
    await page.getByRole('menuitem', { name: 'Usuń', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(snackBar(page)).toContainText(/target of a (recurring|one-off) income/);
    await expect(page.getByRole('link', { name: BANK, exact: true })).toBeVisible();
  });
});
