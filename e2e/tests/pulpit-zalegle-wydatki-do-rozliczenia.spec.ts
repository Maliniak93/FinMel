import { type Locator, type Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test, warsawDate, wealthTab } from './fixtures';
import {
  addExpense,
  choose,
  dateInText,
  eur,
  openAddDialog,
  openAddExpenseDialog,
  openMonth,
  pln,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TRANSACTION_DEPOSIT = 2;

const MBANK = 'mBank bieżące';
const REVOLUT = 'Revolut EUR';
const RENT = 'Czynsz';
const SUBSCRIPTION = 'Abonament';
const INSURANCE = 'Ubezpieczenie OC';
const BONUS = 'Premia';

const MAX_DAY_OF_MONTH = 28;

function monthOf(iso: string): string {
  return iso.slice(0, 7);
}

function dayOf(iso: string): number {
  return Number(iso.slice(8, 10));
}

function dayNumber(iso: string): number {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return Date.UTC(year, month - 1, day) / 86_400_000;
}

function nextMonth(iso: string): string {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month, day)).toISOString().slice(0, 10);
}

function agoWithDayUpTo28(minimum: number, step: 1 | -1): number {
  let offset = minimum;
  while (dayOf(warsawDate(offset)) > MAX_DAY_OF_MONTH) offset += step;
  return offset;
}

const TODAY = warsawDate();
const OCTOBER_5 = warsawDate(agoWithDayUpTo28(33, 1));
const NOVEMBER_5 = nextMonth(OCTOBER_5);
const RENT_DAY = dayOf(OCTOBER_5);
const RENT_SECOND_AGO = dayNumber(TODAY) - dayNumber(NOVEMBER_5);
const SUBSCRIPTION_AGO = RENT_SECOND_AGO === 4 ? 6 : 4;
const NOVEMBER_3 = warsawDate(SUBSCRIPTION_AGO);
const INSURANCE_IN = agoWithDayUpTo28(-5, 1) * -1;
const NOVEMBER_12 = warsawDate(-INSURANCE_IN);
const BONUS_DATE = warsawDate(23);
const SMALL_NAMES = ['Drobiazg 1', 'Drobiazg 2', 'Drobiazg 3', 'Drobiazg 4', 'Drobiazg 5'];
const SMALL_DATES = [12, 11, 10, 9, 8].map((ago) => warsawDate(ago));

const NO_FUNDS = "The source of funds can't cover this amount";
const PHONE = { width: 390, height: 844 };
const MIN_TOUCH_TARGET = 44;

function looseDate(iso: string): RegExp {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new RegExp(`(?=.*\\b0?${day}\\b)(?=.*\\b0?${month}\\b)(?=.*${year})`);
}

async function createBankAccount(
  request: Parameters<typeof logInThroughApi>[0],
  token: string,
  account: { name: string; currency: string; opening: number },
): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const created = await request.post('/api/portfolio/accounts', {
    headers,
    data: {
      name: account.name,
      currency: account.currency,
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(created.ok()).toBeTruthy();
  const body = (await created.json()) as { cashAssetId: string };
  const deposit = await request.post(`/api/portfolio/assets/${body.cashAssetId}/transactions`, {
    headers,
    data: {
      type: TRANSACTION_DEPOSIT,
      quantity: account.opening,
      unitPrice: 1,
      date: '2026-01-01',
    },
  });
  expect(deposit.ok()).toBeTruthy();
}

async function openDashboard(page: Page): Promise<void> {
  const todo = page.waitForResponse(
    (response) => response.url().includes('/api/portfolio/planning/todo') && response.ok(),
  );
  await page.goto('/dashboard');
  await todo;
  await expect(page.getByRole('heading', { name: 'Do rozliczenia' })).toBeVisible();
}

function todoItems(page: Page): Locator {
  return page.locator('app-todo-card').getByTestId('todo-card-item');
}

function todoItem(page: Page, name: string): Locator {
  return todoItems(page).filter({ hasText: name });
}

function badge(page: Page): Locator {
  return page.getByTestId('todo-badge');
}

function accountRow(page: Page, name: string): Locator {
  return page.locator('.accounts-card__row').filter({ hasText: name });
}

async function expectRow(
  row: Locator,
  expected: { name: string; meta: string; date: string; amount: string; settle: boolean },
): Promise<void> {
  await expect(row.locator('.todo-card__name')).toHaveText(expected.name);
  await expect(row.locator('.todo-card__meta')).toContainText(expected.meta);
  await expect(row.locator('.todo-card__meta')).toContainText(dateInText(expected.date));
  await expect(row.locator('.todo-card__amount')).toContainText(pln(expected.amount));
  await expect(row.getByRole('button', { name: 'Rozlicz', exact: true })).toHaveCount(
    expected.settle ? 1 : 0,
  );
}

function accountSelect(dialog: Locator): Locator {
  return dialog.getByRole('combobox', { name: 'Konto', exact: true });
}

function withdrawalSwitch(dialog: Locator): Locator {
  return dialog.getByRole('checkbox', { name: 'Zapisz wypłatę na koncie' });
}

async function openSettleFromCard(row: Locator, page: Page): Promise<Locator> {
  await row.getByRole('button', { name: 'Rozlicz', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
  return dialog;
}

// The EUR rate reaches MarketData asynchronously, so a refused save is retried.
async function saveWithRetry(dialog: Locator): Promise<void> {
  await expect(async () => {
    await dialog.getByRole('button', { name: 'Zapisz' }).click({ timeout: 3_000 });
    await expect(dialog).toBeHidden({ timeout: 8_000 });
  }).toPass({ timeout: 90_000, intervals: [1_000, 3_000, 5_000] });
}

function payment(page: Page, name: string, iso: string): Locator {
  return page
    .locator(`[data-testid="day-group"][data-date="${iso}"]`)
    .getByTestId('expense-payment-row')
    .filter({ has: page.getByTestId('row-name').filter({ hasText: new RegExp(`^${name}$`) }) });
}

function paymentStatus(row: Locator): Locator {
  return row.getByTestId('expense-status').locator('.status-chip__label');
}

async function openBankAccount(page: Page, name: string): Promise<void> {
  await wealthTab(page, 'Konta bankowe');
  await page.getByRole('link', { name, exact: true }).click();
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
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

async function addOneOffIncome(
  page: Page,
  income: { name: string; amount: string; date: string },
): Promise<void> {
  const dialog = await openAddDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(page, accountSelect(dialog), new RegExp(`^${MBANK}`));
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

test('pulpit: zaległe wydatki na karcie „Do rozliczenia” are listed, settled from the card and dropped when settled early', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  await createBankAccount(request, token, { name: MBANK, currency: 'PLN', opening: 10000 });
  await createBankAccount(request, token, { name: REVOLUT, currency: 'EUR', opening: 200 });

  await page.goto('/login');
  await logInThroughUi(page, user);

  await test.step('Krok 1: a fresh account shows the empty "Do rozliczenia" card', async () => {
    await openDashboard(page);
    await expect(page.getByTestId('todo-card-empty')).toHaveText('Wszystko załatwione');
    await expect(todoItems(page)).toHaveCount(0);
  });

  await test.step('Krok 2: Czynsz 2 400 zł monthly on the 5th from 5.10 on mBank bieżące and the one-off Abonament 120 zł on 3.11 without an account are added in Planowanie', async () => {
    await openMonth(page, monthOf(OCTOBER_5));
    const dialog = await openAddExpenseDialog(page);
    await dialog.getByLabel('Nazwa').fill(RENT);
    await dialog.getByRole('spinbutton', { name: 'Kwota', exact: true }).fill('2400');
    await dialog.getByLabel('Data początku').fill(OCTOBER_5);
    await dialog.getByRole('spinbutton', { name: 'Powtarzaj co (miesięcy)' }).fill('1');
    await choose(
      page,
      dialog.getByRole('combobox', { name: 'Dzień', exact: true }),
      'Dzień miesiąca',
    );
    await dialog.getByRole('spinbutton', { name: 'Dzień miesiąca' }).fill(String(RENT_DAY));
    await choose(page, accountSelect(dialog), new RegExp(`^${MBANK}`));
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await openMonth(page, monthOf(NOVEMBER_3));
    await addOneOffExpense(page, { name: SUBSCRIPTION, amount: '120', date: NOVEMBER_3 });
    await expect(payment(page, SUBSCRIPTION, NOVEMBER_3)).toHaveCount(1);
  });

  await test.step('Krok 3: the card lists three expenses from the oldest, each with Rozlicz', async () => {
    await openDashboard(page);
    await expect(todoItems(page)).toHaveCount(3);
    const dayCount = (iso: string): number => dayNumber(TODAY) - dayNumber(iso);
    const expected = [
      { name: RENT, date: OCTOBER_5, amount: '-2 400,00' },
      { name: SUBSCRIPTION, date: NOVEMBER_3, amount: '-120,00' },
      { name: RENT, date: NOVEMBER_5, amount: '-2 400,00' },
    ].sort((a, b) => a.date.localeCompare(b.date));
    for (const [index, item] of expected.entries()) {
      await expectRow(todoItems(page).nth(index), {
        name: item.name,
        meta: `zaległe: ${dayCount(item.date)} dni · wydatek`,
        date: item.date,
        amount: item.amount,
        settle: true,
      });
    }
  });

  await test.step('Krok 4: the badge at Planowanie shows 3', async () => {
    await expect(badge(page)).toHaveText('3');
  });

  await test.step('Krok 5: Rozlicz at the 5.10 rent opens the window with 2400, 5.10, mBank bieżące and the withdrawal on, the dashboard stays behind', async () => {
    await expect(accountRow(page, MBANK)).toContainText(pln('10 000,00'));
    const dialog = await openSettleFromCard(todoItems(page).nth(0), page);
    await expect(dialog.getByRole('heading', { name: 'Rozlicz „Czynsz”' })).toBeVisible();
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toHaveValue('2400');
    await expect(dialog.getByRole('textbox', { name: 'Faktyczna data' })).toHaveValue(
      looseDate(OCTOBER_5),
    );
    await expect(accountSelect(dialog)).toContainText(MBANK);
    await expect(withdrawalSwitch(dialog)).toBeChecked();
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  await test.step('Krok 6: saving removes the row, leaves Abonament and the 5.11 rent, badge 2, mBank bieżące drops by 2 400 zł without a reload', async () => {
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(todoItems(page)).toHaveCount(2);
    await expect(todoItem(page, SUBSCRIPTION)).toHaveCount(1);
    await expect(todoItem(page, RENT)).toHaveCount(1);
    await expect(todoItem(page, RENT)).toContainText(dateInText(NOVEMBER_5));
    await expect(badge(page)).toHaveText('2');
    await expect(accountRow(page, MBANK)).toContainText(pln('7 600,00'), { timeout: 30_000 });
  });

  await test.step('Krok 7: Abonament needs an account, then 25 EUR on Revolut EUR removes the row and leaves 175 EUR', async () => {
    const dialog = await openSettleFromCard(todoItem(page, SUBSCRIPTION), page);
    await expect(dialog.getByRole('button', { name: 'Zapisz' })).toBeDisabled();
    await choose(page, accountSelect(dialog), new RegExp(`^${REVOLUT}`));
    await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill('25');
    await saveWithRetry(dialog);
    await expect(todoItem(page, SUBSCRIPTION)).toHaveCount(0);
    await openBankAccount(page, REVOLUT);
    await expect(
      page.locator('tr[data-transaction-id]').filter({ hasText: eur('25,00') }),
    ).toHaveCount(1);
    await expect(page.getByTestId('account-cash')).toContainText(eur('175,00'));
  });

  await test.step('Krok 8: 500 on Revolut EUR for the 5.11 rent is refused for lack of funds, the window and the row stay, Anuluj changes nothing', async () => {
    await openDashboard(page);
    const row = todoItem(page, RENT);
    await expect(row).toHaveCount(1);
    const dialog = await openSettleFromCard(row, page);
    await choose(page, accountSelect(dialog), new RegExp(`^${REVOLUT}`));
    await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill('500');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(page.getByText(NO_FUNDS).first()).toBeVisible();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(todoItem(page, RENT)).toHaveCount(1);
    await expect(badge(page)).toHaveText('1');
  });

  await test.step('Krok 9: settling that rent on mBank bieżące empties the card', async () => {
    const dialog = await openSettleFromCard(todoItem(page, RENT), page);
    await choose(page, accountSelect(dialog), new RegExp(`^${MBANK}`));
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('todo-card-empty')).toHaveText('Wszystko załatwione');
    await expect(todoItems(page)).toHaveCount(0);
  });

  await test.step('Krok 10: the 5.10 rent is rozliczone in październik, and after the undo it is back on the card', async () => {
    await openMonth(page, monthOf(OCTOBER_5));
    const row = payment(page, RENT, OCTOBER_5);
    await expect(paymentStatus(row)).toHaveText('rozliczone');
    await row.getByTestId('undo-expense').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(paymentStatus(row)).toHaveText('oczekuje');
    await openDashboard(page);
    await expect(todoItem(page, RENT)).toHaveCount(1);
    await expect(todoItem(page, RENT)).toContainText(dateInText(OCTOBER_5));
  });

  await test.step('Krok 11: Ubezpieczenie OC 1 200 zł every 3 months, due in 5 days, is listed as upcoming without a button', async () => {
    await openMonth(page, monthOf(NOVEMBER_12));
    await addExpense(page, {
      name: INSURANCE,
      amount: '1200',
      start: NOVEMBER_12,
      interval: '3',
      day: dayOf(NOVEMBER_12),
    });
    await openDashboard(page);
    await expectRow(todoItem(page, INSURANCE), {
      name: INSURANCE,
      meta: `za ${INSURANCE_IN} dni · wydatek`,
      date: NOVEMBER_12,
      amount: '-1 200,00',
      settle: false,
    });
  });

  await test.step('Krok 12: settling that payment early in Planowanie removes it from the card', async () => {
    await openMonth(page, monthOf(NOVEMBER_12));
    const row = payment(page, INSURANCE, NOVEMBER_12);
    await row.getByTestId('settle-expense').click();
    const dialog = page.getByRole('dialog');
    await choose(page, accountSelect(dialog), new RegExp(`^${MBANK}`));
    await dialog.getByRole('textbox', { name: 'Faktyczna data' }).fill(TODAY);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(paymentStatus(row)).toHaveText('rozliczone');
    await openDashboard(page);
    await expect(todoItem(page, INSURANCE)).toHaveCount(0);
  });

  await test.step('Krok 13: Premia 1 800 zł from 23 days ago and five 10 zł expenses make the card show five oldest items, the rest after "pokaż wszystkie"', async () => {
    await openMonth(page, monthOf(BONUS_DATE));
    await addOneOffIncome(page, { name: BONUS, amount: '1800', date: BONUS_DATE });
    for (const [index, name] of SMALL_NAMES.entries()) {
      await addOneOffExpense(page, { name, amount: '10', date: SMALL_DATES[index] as string });
    }
    await openDashboard(page);
    await expect(todoItems(page)).toHaveCount(5);
    const names = [RENT, BONUS, SMALL_NAMES[0], SMALL_NAMES[1], SMALL_NAMES[2]];
    for (const [index, name] of names.entries()) {
      await expect(todoItems(page).nth(index).locator('.todo-card__name')).toHaveText(
        name as string,
      );
    }
    await page.getByTestId('todo-card-show-all').click();
    await expect(todoItems(page)).toHaveCount(7);
    await expect(todoItems(page).nth(6).locator('.todo-card__name')).toHaveText(SMALL_NAMES[4]!);
  });

  await test.step('Krok 14: Rozlicz at Premia links to Planowanie and opens no expense window', async () => {
    const link = todoItem(page, BONUS).getByRole('link', { name: 'Rozlicz', exact: true });
    await expect(link).toHaveAttribute(
      'href',
      new RegExp(`/planning[?]month=${monthOf(BONUS_DATE)}#do-rozliczenia`),
    );
    await link.click();
    await expect(page).toHaveURL(new RegExp(`/planning[?]month=${monthOf(BONUS_DATE)}`));
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  await test.step('Krok 15: at 390 px the expense row with Rozlicz fits one column and the button is at least 44 px high', async () => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    const row = todoItems(page).filter({ hasText: RENT }).first();
    await expect(row).toBeVisible();
    const button = row.getByRole('button', { name: 'Rozlicz', exact: true });
    const rowBox = (await row.boundingBox())!;
    const buttonBox = (await button.boundingBox())!;
    expect(buttonBox.height).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    expect(buttonBox.x).toBeGreaterThanOrEqual(rowBox.x - 1);
    expect(buttonBox.x + buttonBox.width).toBeLessThanOrEqual(rowBox.x + rowBox.width + 1);
    expect(rowBox.x + rowBox.width).toBeLessThanOrEqual(PHONE.width);
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
