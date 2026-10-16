import { type Locator, type Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test, warsawDate } from './fixtures';
import {
  MBANK,
  addIncome,
  confirmDelete,
  dateInText,
  exactly,
  fillRule,
  openMonth,
  occurrences,
  pln,
  type IncomeInput,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;

const LIFE = 'Życie';
const BILLS = 'Rachunki';
const CAR = 'OC Toyota';
const SERVICE = 'Serwis';
const DOWN_PAYMENT = 'Wkład własny';
const DOMINIK = 'Wypłata Dominik';
const WIFE = 'Wypłata żona';
const BENEFIT = '800+';

const CURRENT = warsawDate().slice(0, 7);

function shiftMonth(month: string, offset: number): string {
  const [year, number] = month.split('-').map(Number) as [number, number];
  const index = year * 12 + (number - 1) + offset;
  const shiftedYear = Math.floor(index / 12);
  return `${shiftedYear}-${String((index % 12) + 1).padStart(2, '0')}`;
}

function m(offset: number): string {
  return shiftMonth(CURRENT, offset);
}

function day(month: string, dayOfMonth: number): string {
  return `${month}-${String(dayOfMonth).padStart(2, '0')}`;
}

function lastDay(month: string): string {
  const [year, number] = month.split('-').map(Number) as [number, number];
  return day(month, new Date(Date.UTC(year, number, 0)).getUTCDate());
}

function monthInList(month: string): string {
  const [year, number] = month.split('-') as [string, string];
  return `${number}.${year}`;
}

interface ExpenseInput {
  name: string;
  amount: string;
  start: string;
  end?: string;
  interval: string;
  day: number;
}

function expenseDefinition(page: Page, name: string): Locator {
  return page.getByTestId('expense-definition-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

function expensePayments(page: Page, name?: string): Locator {
  const rows = page.getByTestId('expense-payment-row');
  return name ? rows.filter({ has: page.getByRole('cell', { name: exactly(name) }) }) : rows;
}

async function openAddExpenseDialog(page: Page): Promise<Locator> {
  await page.getByTestId('add-expense').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Dodaj wydatek cykliczny' })).toBeVisible();
  return dialog;
}

async function fillExpense(page: Page, dialog: Locator, expense: ExpenseInput): Promise<void> {
  await dialog.getByLabel('Nazwa').fill(expense.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota (PLN)' }).fill(expense.amount);
  await dialog.getByLabel('Data początku').fill(expense.start);
  if (expense.end) {
    await dialog.getByLabel('Data końca').fill(expense.end);
  }
  await fillRule(page, dialog, { interval: expense.interval, day: expense.day });
}

async function addExpense(page: Page, expense: ExpenseInput): Promise<void> {
  const dialog = await openAddExpenseDialog(page);
  await fillExpense(page, dialog, expense);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
  await expect(expenseDefinition(page, expense.name)).toHaveCount(1);
}

async function openEditExpense(page: Page, name: string): Promise<Locator> {
  await expenseDefinition(page, name).getByTestId('edit-expense').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj wydatek cykliczny' })).toBeVisible();
  return dialog;
}

async function editExpenseFrom(
  page: Page,
  name: string,
  month: string,
  change: { amount?: string; name?: string },
): Promise<void> {
  const dialog = await openEditExpense(page, name);
  await dialog.getByLabel('Obowiązuje od').fill(month);
  if (change.amount) {
    await dialog.getByRole('spinbutton', { name: 'Kwota (PLN)' }).fill(change.amount);
  }
  if (change.name) {
    await dialog.getByLabel('Nazwa').fill(change.name);
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function endExpense(page: Page, name: string, endDate: string): Promise<void> {
  await expenseDefinition(page, name).getByRole('button', { name: 'Zakończ', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Data końca').fill(endDate);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function deleteExpense(page: Page, name: string): Promise<void> {
  await expenseDefinition(page, name).getByRole('button', { name: 'Usuń', exact: true }).click();
  await confirmDelete(page);
  await expect(expenseDefinition(page, name)).toHaveCount(0);
}

async function expectCost(page: Page, amount: string): Promise<void> {
  await expect(page.getByTestId('expense-cost').locator('dd')).toContainText(pln(amount));
}

async function expectReserve(page: Page, name: string, amount: string): Promise<void> {
  const anchored = new RegExp(`(?<![\\d,.])(?<!\\d[\\s\\u00a0\\u202f])${pln(amount).source}`);
  await expect(expenseDefinition(page, name).getByTestId('expense-reserve')).toContainText(
    anchored,
  );
}

async function expectFreeFunds(page: Page, plan: string, forecast?: string): Promise<void> {
  await expect(page.getByTestId('free-funds-plan').locator('dd')).toContainText(pln(plan));
  if (forecast) {
    await expect(page.getByTestId('free-funds-forecast').locator('dd')).toContainText(
      pln(forecast),
    );
  }
}

async function settle(page: Page, name: string, amount: string): Promise<void> {
  const row = occurrences(page, name);
  await row.getByTestId('settle-occurrence').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill(amount);
  await dialog.getByRole('textbox', { name: 'Faktyczna data' }).fill(warsawDate());
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
  await expect(row.getByTestId('occurrence-status')).toHaveText('rozliczone');
}

const INCOMES: IncomeInput[] = [
  {
    name: DOMINIK,
    amount: '9200',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 'last',
  },
  {
    name: WIFE,
    amount: '6500',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 10,
    shift: 'Poprzedni dzień roboczy',
  },
  {
    name: BENEFIT,
    amount: '800',
    account: MBANK,
    start: '2024-06-10',
    interval: '1',
    day: 10,
  },
];

test('recurring expenses: plan fixed expenses with reserves, read free funds for any month and change them from a month', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

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

  await openMonth(page, m(0));
  for (const income of INCOMES) {
    await addIncome(page, income);
  }

  await test.step('Krok 1: the Wydatki section of the current month has no payments or definitions, cost 0 zł, free funds plan and forecast 16 500 zł', async () => {
    await openMonth(page, m(0));
    await expect(page.getByTestId('expenses-section')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Wydatki', exact: true })).toBeVisible();
    await expect(page.getByText('W tym miesiącu nie planujesz żadnych wydatków.')).toBeVisible();
    await expect(page.getByText('Brak wydatków cyklicznych.')).toBeVisible();
    await expect(expensePayments(page)).toHaveCount(0);
    await expectCost(page, '0,00');
    await expectFreeFunds(page, '16 500,00', '16 500,00');
  });

  await test.step('Krok 2: Życie 3 000 zł on the 1st without an account or currency question gives a payment of 3 000 zł, cost 3 000 zł and plan 13 500 zł', async () => {
    const dialog = await openAddExpenseDialog(page);
    await expect(dialog.getByRole('combobox', { name: 'Konto' })).toHaveCount(0);
    await expect(dialog.getByRole('combobox', { name: 'Waluta' })).toHaveCount(0);
    await fillExpense(page, dialog, {
      name: LIFE,
      amount: '3000',
      start: '2026-01-01',
      interval: '1',
      day: 1,
    });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    const payment = expensePayments(page, LIFE);
    await expect(payment).toContainText(dateInText(day(m(0), 1)));
    await expect(payment).toContainText(pln('3 000,00'));
    await expectCost(page, '3 000,00');
    await expectFreeFunds(page, '13 500,00');
  });

  await test.step('Krok 3: Rachunki 1 000 zł on the 10th gives a payment of 1 000 zł and cost 4 000 zł', async () => {
    await addExpense(page, {
      name: BILLS,
      amount: '1000',
      start: '2026-01-01',
      interval: '1',
      day: 10,
    });
    const payment = expensePayments(page, BILLS);
    await expect(payment).toContainText(dateInText(day(m(0), 10)));
    await expect(payment).toContainText(pln('1 000,00'));
    await expectCost(page, '4 000,00');
  });

  await test.step('Krok 4: a second Rachunki is rejected with the taken-name message', async () => {
    const dialog = await openAddExpenseDialog(page);
    await fillExpense(page, dialog, {
      name: BILLS,
      amount: '1000',
      start: '2026-01-01',
      interval: '1',
      day: 10,
    });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByText('Wydatek o tej nazwie już istnieje.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(expenseDefinition(page, BILLS)).toHaveCount(1);
  });

  await test.step('Krok 5: OC Toyota 1 200 zł every 12 months from the 15th of M+5 has no payment in M, reserve 200 zł/mies., next payment 15.(M+5) and cost 4 200 zł', async () => {
    await addExpense(page, {
      name: CAR,
      amount: '1200',
      start: day(m(5), 15),
      interval: '12',
      day: 15,
    });
    await expect(expensePayments(page, CAR)).toHaveCount(0);
    await expectReserve(page, CAR, '200,00');
    await expect(expenseDefinition(page, CAR)).toContainText('/mies.');
    await expect(expenseDefinition(page, CAR).getByTestId('expense-next-payment')).toContainText(
      dateInText(day(m(5), 15)),
    );
    await expectCost(page, '4 200,00');
  });

  await test.step('Krok 6: free funds of the current month are plan 12 300 zł and forecast 12 300 zł', async () => {
    await expectFreeFunds(page, '12 300,00', '12 300,00');
  });

  await test.step('Krok 7: M-1 has no OC reserve, Życie and Rachunki are charged in full and cost is 4 000 zł', async () => {
    await openMonth(page, m(-1));
    await expectReserve(page, CAR, '0,00');
    await expect(expensePayments(page, LIFE)).toContainText(pln('3 000,00'));
    await expect(expensePayments(page, BILLS)).toContainText(pln('1 000,00'));
    await expectCost(page, '4 000,00');
  });

  await test.step('Krok 8: M+5 lists the OC Toyota payment of 1 200 zł on the 15th, the reserve stays 200 zł and cost is 4 200 zł', async () => {
    await openMonth(page, m(5));
    const payment = expensePayments(page, CAR);
    await expect(payment).toContainText(dateInText(day(m(5), 15)));
    await expect(payment).toContainText(pln('1 200,00'));
    await expectReserve(page, CAR, '200,00');
    await expectCost(page, '4 200,00');
  });

  await test.step('Krok 9: M+6 has the OC reserve 100 zł/mies., next payment 15.(M+17) and cost 4 100 zł', async () => {
    await openMonth(page, m(6));
    await expectReserve(page, CAR, '100,00');
    await expect(expenseDefinition(page, CAR).getByTestId('expense-next-payment')).toContainText(
      dateInText(day(m(17), 15)),
    );
    await expectCost(page, '4 100,00');
  });

  await test.step('Krok 10: Serwis 1 000 zł every 3 months from the 20th of M+2 reserves 333,33 / 333,33 / 333,34 zł in M, M+1, M+2, cost M is 4 533,33 zł and plan M 11 966,67 zł', async () => {
    await openMonth(page, m(0));
    await addExpense(page, {
      name: SERVICE,
      amount: '1000',
      start: day(m(2), 20),
      interval: '3',
      day: 20,
    });
    await expectReserve(page, SERVICE, '333,33');
    await expectCost(page, '4 533,33');
    await expectFreeFunds(page, '11 966,67');
    await openMonth(page, m(1));
    await expectReserve(page, SERVICE, '333,33');
    await openMonth(page, m(2));
    await expectReserve(page, SERVICE, '333,34');
    await expect(expensePayments(page, SERVICE)).toContainText(pln('1 000,00'));
  });

  await test.step('Krok 11: Premia roczna 12 000 zł from the 1st of M+5 gives plan M+5 23 966,66 zł and plan M+4 11 966,67 zł', async () => {
    await openMonth(page, m(0));
    await addIncome(page, {
      name: 'Premia roczna',
      amount: '12000',
      account: MBANK,
      start: day(m(5), 1),
      interval: '12',
      day: 25,
    });
    await openMonth(page, m(5));
    await expectFreeFunds(page, '23 966,66');
    await openMonth(page, m(4));
    await expectFreeFunds(page, '11 966,67');
  });

  await test.step('Krok 12: Wkład własny 13 000 zł in M+1 gives cost 17 533,33 zł and plan -1 033,33 zł marked as a deficit', async () => {
    await openMonth(page, m(1));
    await addExpense(page, {
      name: DOWN_PAYMENT,
      amount: '13000',
      start: day(m(1), 5),
      end: lastDay(m(1)),
      interval: '1',
      day: 5,
    });
    await expectCost(page, '17 533,33');
    await expectFreeFunds(page, '-1 033,33');
    await expect(
      page.getByTestId('free-funds-plan').getByTestId('free-funds-deficit'),
    ).toBeVisible();
  });

  await test.step('Krok 13: deleting Wkład własny returns M+1 to cost 4 533,33 zł, plan 11 966,67 zł and no deficit', async () => {
    await deleteExpense(page, DOWN_PAYMENT);
    await expectCost(page, '4 533,33');
    await expectFreeFunds(page, '11 966,67');
    await expect(page.getByTestId('free-funds-deficit')).toHaveCount(0);
  });

  await test.step('Krok 14: settling Wypłata Dominik at 9 143,27 zł and Wypłata żona at 6 500 zł keeps plan 11 966,67 zł and gives forecast 11 909,94 zł', async () => {
    await openMonth(page, m(0));
    await settle(page, DOMINIK, '9143.27');
    await settle(page, WIFE, '6500');
    await expectFreeFunds(page, '11 966,67', '11 909,94');
  });

  await test.step('Krok 15: skipping 800+ gives forecast 11 109,94 zł and plan 11 966,67 zł', async () => {
    const row = occurrences(page, BENEFIT);
    await row.getByTestId('skip-occurrence').click();
    await expect(row.getByTestId('occurrence-status')).toHaveText('pominięte');
    await expectFreeFunds(page, '11 966,67', '11 109,94');
  });

  await test.step('Krok 16: OC Toyota at 1 320 zł from M+3 reserves 200 zł in M..M+2, 240 zł in M+3..M+5, pays 1 320 zł in M+5 and shows the scheduled change in M', async () => {
    await editExpenseFrom(page, CAR, m(3), { amount: '1320' });
    const change = expenseDefinition(page, CAR).getByTestId('expense-next-change');
    await expect(change).toContainText(monthInList(m(3)));
    await expect(change).toContainText(pln('1 320,00'));
    await expectReserve(page, CAR, '200,00');
    for (const offset of [1, 2]) {
      await openMonth(page, m(offset));
      await expectReserve(page, CAR, '200,00');
    }
    for (const offset of [3, 4, 5]) {
      await openMonth(page, m(offset));
      await expectReserve(page, CAR, '240,00');
    }
    await expect(expensePayments(page, CAR)).toContainText(pln('1 320,00'));
  });

  await test.step('Krok 17: deleting the scheduled change returns the M+3 reserve to 200 zł and removes the change', async () => {
    await openMonth(page, m(0));
    const change = expenseDefinition(page, CAR).getByTestId('expense-next-change');
    await expect(change).toBeVisible();
    await change.getByTestId('delete-expense-change').click();
    await confirmDelete(page);
    await expect(change).toHaveCount(0);
    await openMonth(page, m(3));
    await expectReserve(page, CAR, '200,00');
  });

  await test.step('Krok 18: OC Toyota at 300 zł from M+3 gives reserve -100 zł and cost 4 233,33 zł in M+3, M stays at 200 zł', async () => {
    await openMonth(page, m(0));
    await editExpenseFrom(page, CAR, m(3), { amount: '300' });
    await openMonth(page, m(3));
    await expectReserve(page, CAR, '-100,00');
    await expectCost(page, '4 233,33');
    await openMonth(page, m(0));
    await expectReserve(page, CAR, '200,00');
  });

  await test.step('Krok 19: ending OC Toyota on the last day of M+4 leaves no payment in M+5, no reserve in M..M+4 and cost M 4 333,33 zł', async () => {
    await endExpense(page, CAR, lastDay(m(4)));
    await expectCost(page, '4 333,33');
    for (const offset of [0, 1, 2, 3, 4]) {
      await openMonth(page, m(offset));
      await expectReserve(page, CAR, '0,00');
    }
    await openMonth(page, m(5));
    await expect(expensePayments(page, CAR)).toHaveCount(0);
  });

  await test.step('Krok 20: deleting Serwis gives cost M 4 000 zł and plan M 12 500 zł', async () => {
    await openMonth(page, m(0));
    await deleteExpense(page, SERVICE);
    await expectCost(page, '4 000,00');
    await expectFreeFunds(page, '12 500,00');
  });

  await test.step('Krok 21: renaming Rachunki to Media from M+2 shows the new name in M-1 and M with the amounts unchanged', async () => {
    await openMonth(page, m(2));
    await editExpenseFrom(page, BILLS, m(2), { name: 'Media' });
    for (const offset of [-1, 0]) {
      await openMonth(page, m(offset));
      await expect(expenseDefinition(page, 'Media')).toHaveCount(1);
      await expect(expenseDefinition(page, BILLS)).toHaveCount(0);
      await expect(expensePayments(page, 'Media')).toContainText(pln('1 000,00'));
      await expectCost(page, '4 000,00');
    }
  });
});
