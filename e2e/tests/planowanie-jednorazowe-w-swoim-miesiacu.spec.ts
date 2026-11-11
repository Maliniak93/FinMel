import { type Locator, type Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test } from './fixtures';
import {
  choose,
  confirmDelete,
  dateInText,
  exactly,
  expectCost,
  expectPlan,
  MBANK,
  openAddDialog,
  openAddExpenseDialog,
  openMonth,
  pln,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;

const OCTOBER = '2026-10';
const NOVEMBER = '2026-11';
const DECEMBER = '2026-12';
const JANUARY = '2027-01';

const NO_INCOMES = 'Brak wpływów cyklicznych.';
const NO_EXPENSES = 'Brak wydatków cyklicznych.';

function incomeRow(page: Page, name: string): Locator {
  return page.getByTestId('one-off-income-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

function expenseRow(page: Page, name: string): Locator {
  return page.getByTestId('one-off-expense-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

async function addOneOffIncome(
  page: Page,
  income: { name: string; amount: string; date: string },
): Promise<void> {
  const dialog = await openAddDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(page, dialog.getByRole('combobox', { name: 'Konto', exact: true }), /^mBank ROR/);
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function addOneOffExpense(
  page: Page,
  expense: { name: string; amount: string; date: string },
): Promise<void> {
  const dialog = await openAddExpenseDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(expense.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota', exact: true }).fill(expense.amount);
  await dialog.getByLabel('Data', { exact: true }).fill(expense.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function expectNoIncomeRows(page: Page): Promise<void> {
  await expect(page.getByTestId('one-off-income-row')).toHaveCount(0);
  await expect(page.getByText(NO_INCOMES)).toBeVisible();
}

async function expectBalance(
  page: Page,
  values: { plan?: string; pending?: string; actual?: string; difference?: string },
): Promise<void> {
  if (values.plan) {
    await expectPlan(page, values.plan);
  }
  for (const [key, amount] of Object.entries(values)) {
    if (key !== 'plan' && amount) {
      await expect(page.getByTestId(`balance-${key}`).locator('dd')).toContainText(pln(amount));
    }
  }
}

test('planowanie: jednorazowy wpis tylko w swoim miesiącu i Różnica bez kreski', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(600_000);

  const token = await logInThroughApi(request, user);
  const created = await request.post('/api/portfolio/accounts', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name: MBANK,
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

  await test.step('Krok 1: listopad 2026 has no incomes message and Plan 0,00 zł', async () => {
    await openMonth(page, NOVEMBER);
    await expectNoIncomeRows(page);
    await expectBalance(page, { plan: '0,00' });
  });

  await test.step('Krok 2: Premia 3 000 zł on 15.11.2026 is on the listopad list with its mark and date', async () => {
    await addOneOffIncome(page, { name: 'Premia', amount: '3000', date: '2026-11-15' });
    const row = incomeRow(page, 'Premia');
    await expect(row).toHaveCount(1);
    await expect(row.getByTestId('one-off-mark')).toHaveText('jednorazowy');
    await expect(row.getByTestId('one-off-date')).toHaveText(dateInText('2026-11-15'));
    await expect(row).toContainText(pln('3 000,00'));
    await expectBalance(page, { plan: '3 000,00', pending: '3 000,00' });
  });

  await test.step('Krok 3: grudzień 2026 has no Premia, the empty message and Plan 0,00 zł', async () => {
    await openMonth(page, DECEMBER);
    await expect(incomeRow(page, 'Premia')).toHaveCount(0);
    await expectNoIncomeRows(page);
    await expectBalance(page, { plan: '0,00' });
  });

  await test.step('Krok 4: styczeń 2027 has no Premia', async () => {
    await openMonth(page, JANUARY);
    await expect(incomeRow(page, 'Premia')).toHaveCount(0);
  });

  await test.step('Krok 5: październik 2026 has no Premia', async () => {
    await openMonth(page, OCTOBER);
    await expect(incomeRow(page, 'Premia')).toHaveCount(0);
  });

  await test.step('Krok 6: Zwrot 500 zł on 01.12.2026 added in listopad closes the window, stays on listopad and is not listed there', async () => {
    await openMonth(page, NOVEMBER);
    await addOneOffIncome(page, { name: 'Zwrot', amount: '500', date: '2026-12-01' });
    await expect(page.getByTestId('month-label')).toHaveText('listopad 2026');
    await expect(page.locator('mat-snack-bar-container')).toHaveCount(0);
    await expect(incomeRow(page, 'Zwrot')).toHaveCount(0);
    await expect(incomeRow(page, 'Premia')).toHaveCount(1);
    await expectBalance(page, { plan: '3 000,00' });
  });

  await test.step('Krok 7: grudzień 2026 lists Zwrot 500 zł dated 01.12.2026 and Plan 500 zł', async () => {
    await openMonth(page, DECEMBER);
    const row = incomeRow(page, 'Zwrot');
    await expect(row).toHaveCount(1);
    await expect(row).toContainText(pln('500,00'));
    await expect(row.getByTestId('one-off-date')).toHaveText(dateInText('2026-12-01'));
    await expectBalance(page, { plan: '500,00' });
  });

  await test.step('Krok 8: Ostatni dzień 100 zł on 30.11.2026 is on the listopad list and Plan is 3 100 zł', async () => {
    await openMonth(page, NOVEMBER);
    await addOneOffIncome(page, { name: 'Ostatni dzień', amount: '100', date: '2026-11-30' });
    await expect(incomeRow(page, 'Ostatni dzień')).toHaveCount(1);
    await expectBalance(page, { plan: '3 100,00' });
  });

  await test.step('Krok 9: grudzień 2026 lists only Zwrot', async () => {
    await openMonth(page, DECEMBER);
    await expect(page.getByTestId('one-off-income-row')).toHaveCount(1);
    await expect(incomeRow(page, 'Zwrot')).toHaveCount(1);
    await expect(incomeRow(page, 'Ostatni dzień')).toHaveCount(0);
    await expect(incomeRow(page, 'Premia')).toHaveCount(0);
  });

  await test.step('Krok 10: Zaległa faktura 200 zł on 10.10.2026 is on the październik list and Plan is 200 zł', async () => {
    await openMonth(page, OCTOBER);
    await addOneOffIncome(page, { name: 'Zaległa faktura', amount: '200', date: '2026-10-10' });
    await expect(incomeRow(page, 'Zaległa faktura')).toHaveCount(1);
    await expectBalance(page, { plan: '200,00' });
  });

  await test.step('Krok 11: listopad 2026 lists only Premia and Ostatni dzień', async () => {
    await openMonth(page, NOVEMBER);
    await expect(page.getByTestId('one-off-income-row')).toHaveCount(2);
    await expect(incomeRow(page, 'Premia')).toHaveCount(1);
    await expect(incomeRow(page, 'Ostatni dzień')).toHaveCount(1);
    await expect(incomeRow(page, 'Zaległa faktura')).toHaveCount(0);
    await expect(incomeRow(page, 'Zwrot')).toHaveCount(0);
  });

  await test.step('Krok 12: editing Premia to 3 500 zł gives Plan 3 600 zł, Pending 3 600 zł, Actual 0 zł, Różnica 0 zł', async () => {
    await incomeRow(page, 'Premia').getByTestId('edit-one-off-income').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('3500');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(incomeRow(page, 'Premia')).toContainText(pln('3 500,00'));
    await expectBalance(page, {
      plan: '3 600,00',
      pending: '3 600,00',
      actual: '0,00',
      difference: '0,00',
    });
  });

  await test.step('Krok 13: deleting Zwrot in grudzień leaves the empty message and Plan 0 zł', async () => {
    await openMonth(page, DECEMBER);
    await incomeRow(page, 'Zwrot').getByTestId('delete-one-off-income').click();
    await confirmDelete(page);
    await expect(incomeRow(page, 'Zwrot')).toHaveCount(0);
    await expectNoIncomeRows(page);
    await expectBalance(page, { plan: '0,00' });
  });

  await test.step('Krok 14: Przegląd auta 1 200 zł on 20.11.2026 is on the listopad expenses list with its mark, date and cost', async () => {
    await openMonth(page, NOVEMBER);
    await addOneOffExpense(page, { name: 'Przegląd auta', amount: '1200', date: '2026-11-20' });
    const row = expenseRow(page, 'Przegląd auta');
    await expect(row).toHaveCount(1);
    await expect(row.getByTestId('one-off-mark')).toHaveText('jednorazowy');
    await expect(row.getByTestId('one-off-date')).toHaveText(dateInText('2026-11-20'));
    await expectCost(page, '1 200,00');
  });

  await test.step('Krok 15: grudzień 2026 has no Przegląd auta, the empty expenses message and cost 0 zł', async () => {
    await openMonth(page, DECEMBER);
    await expect(expenseRow(page, 'Przegląd auta')).toHaveCount(0);
    await expect(page.getByText(NO_EXPENSES)).toBeVisible();
    await expectCost(page, '0,00');
  });

  await test.step('Krok 16: październik 2026 has no Przegląd auta', async () => {
    await openMonth(page, OCTOBER);
    await expect(expenseRow(page, 'Przegląd auta')).toHaveCount(0);
  });

  await test.step('Krok 17: Różnica has no top border and the same height, padding and font as Oczekujące', async () => {
    await openMonth(page, NOVEMBER);
    await expect(page.getByTestId('month-balance')).toBeVisible();
    const measure = (testId: string) =>
      page.getByTestId(testId).evaluate((node) => {
        const style = getComputedStyle(node);
        return {
          borderTop: parseFloat(style.borderTopWidth),
          marginTop: style.marginTop,
          paddingTop: style.paddingTop,
          height: node.getBoundingClientRect().height,
          fontSize: style.fontSize,
          fontFamily: style.fontFamily,
        };
      });
    const difference = await measure('balance-difference');
    const pending = await measure('balance-pending');
    expect(difference.borderTop).toBe(0);
    expect(difference).toEqual(pending);
    await expect(page.getByTestId('balance-difference').locator('dd')).toContainText(pln('0,00'));
  });
});
