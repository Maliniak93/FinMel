import { type Locator, type Page } from '@playwright/test';
import {
  expect,
  logInThroughApi,
  logInThroughUi,
  openSelect,
  test,
  warsawDate,
  wealthTab,
} from './fixtures';
import {
  addExpense,
  choose,
  confirmDelete,
  dateInText,
  deleteExpense,
  eur,
  expectCost,
  expectFreeFunds,
  expenseDefinition,
  fillExpense,
  openAddExpenseDialog,
  openMonth,
  pln,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TRANSACTION_DEPOSIT = 2;
const TRANSACTION_WITHDRAW = 3;

const MBANK = 'mBank bieżące';
const REVOLUT = 'Revolut EUR';
const RESERVE = 'Rezerwa';
const RENT = 'Czynsz';
const SUBSCRIPTION = 'Abonament';
const GYM = 'Siłownia';

const YEAR = '2026';
const SEPTEMBER = `${YEAR}-09`;
const OCTOBER = `${YEAR}-10`;
const NOVEMBER = `${YEAR}-11`;
const OCTOBER_5 = `${YEAR}-10-05`;
const OCTOBER_10 = `${YEAR}-10-10`;
const SEPTEMBER_10 = `${YEAR}-09-10`;
const NOVEMBER_3 = `${YEAR}-11-03`;
const NOVEMBER_10 = `${YEAR}-11-10`;

function looseDate(iso: string): RegExp {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new RegExp(`(?=.*\\b0?${day}\\b)(?=.*\\b0?${month}\\b)(?=.*${year})`);
}

async function createBankAccount(
  request: Parameters<typeof logInThroughApi>[0],
  token: string,
  account: { name: string; currency: string; opening: number },
): Promise<{ id: string; cashAssetId: string }> {
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
  const body = (await created.json()) as { id: string; cashAssetId: string };
  const deposit = await request.post(`/api/portfolio/assets/${body.cashAssetId}/transactions`, {
    headers,
    data: {
      type: TRANSACTION_DEPOSIT,
      quantity: account.opening,
      unitPrice: 1,
      date: `${YEAR}-01-01`,
    },
  });
  expect(deposit.ok()).toBeTruthy();
  return body;
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

function oneOffExpenseRow(page: Page, name: string): Locator {
  return page
    .getByTestId('one-off-expense-row')
    .filter({ has: page.locator('.planning-definition__name', { hasText: name }) });
}

function snackBar(page: Page): Locator {
  return page.locator('mat-snack-bar-container');
}

function cash(page: Page): Locator {
  return page.getByTestId('account-cash');
}

function historyRow(page: Page, text: string | RegExp): Locator {
  return page.locator('tr[data-transaction-id]').filter({ hasText: text });
}

async function openBankAccount(page: Page, name: string): Promise<void> {
  await wealthTab(page, 'Konta bankowe');
  await page.getByRole('link', { name, exact: true }).click();
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
}

async function openSettle(page: Page, row: Locator): Promise<Locator> {
  await row.getByTestId('settle-expense').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
  return dialog;
}

async function fillSettlement(
  dialog: Locator,
  values: { amount?: string; date?: string },
): Promise<void> {
  if (values.amount) {
    await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill(values.amount);
  }
  if (values.date) {
    await dialog.getByRole('textbox', { name: 'Faktyczna data' }).fill(values.date);
  }
}

async function saveSettlement(dialog: Locator): Promise<void> {
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function undo(page: Page, row: Locator): Promise<void> {
  await row.getByTestId('undo-expense').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
  await expect(row.getByTestId('settle-expense')).toBeVisible();
}

function accountSelect(dialog: Locator): Locator {
  return dialog.getByRole('combobox', { name: 'Konto', exact: true });
}

function withdrawalSwitch(dialog: Locator): Locator {
  return dialog.getByRole('checkbox', { name: 'Zapisz wypłatę na koncie' });
}

function rememberSwitch(dialog: Locator): Locator {
  return dialog.getByRole('checkbox', { name: 'Zapamiętaj to konto dla tego wydatku' });
}

test('rozliczanie wydatków: settle recurring and one-off expenses with the withdrawal on the account', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  await createBankAccount(request, token, { name: MBANK, currency: 'PLN', opening: 10000 });
  await createBankAccount(request, token, { name: REVOLUT, currency: 'EUR', opening: 200 });
  const reserve = await createBankAccount(request, token, {
    name: RESERVE,
    currency: 'PLN',
    opening: 100,
  });

  await page.goto('/login');
  await logInThroughUi(page, user);
  await openMonth(page, OCTOBER);

  await test.step('Krok 1: Czynsz 2 400 zł monthly on the 5th from 5.10 with mBank bieżące shows the account on the definition', async () => {
    const dialog = await openAddExpenseDialog(page);
    await fillExpense(page, dialog, {
      name: RENT,
      amount: '2400',
      start: OCTOBER_5,
      interval: '1',
      day: 5,
    });
    await choose(page, accountSelect(dialog), new RegExp(`^${MBANK}`));
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(expenseDefinition(page, RENT)).toHaveCount(1);
    await expect(expenseDefinition(page, RENT).getByTestId('expense-account')).toHaveText(MBANK);
  });

  await test.step('Krok 2: październik shows the unsettled 5.10 payment with Rozlicz in the list and the calendar, cost 2 400 zł', async () => {
    await openMonth(page, OCTOBER);
    const row = payment(page, RENT, OCTOBER_5);
    await expect(paymentStatus(row)).toHaveText('oczekuje');
    await expect(row.getByTestId('settle-expense')).toHaveText('Rozlicz');
    await expectCost(page, '2 400,00');
    await page.getByTestId('layout-calendar').click();
    const item = page
      .locator(`[data-testid="calendar-cell"][data-date="${OCTOBER_5}"]`)
      .getByTestId('calendar-item')
      .filter({ hasText: RENT });
    await expect(item).toHaveCount(1);
    await expect(item).toHaveAttribute('data-status', 'pending');
    await expect(item).toHaveAttribute('data-kind', 'payment');
    await page.getByTestId('layout-list').click();
    await expect(page.getByTestId('layout-list')).toHaveAttribute('aria-pressed', 'true');
  });

  await test.step('Krok 3: the settle window offers 2 400, 5.10, mBank bieżące, the withdrawal switch and no remember switch for the account already planned', async () => {
    const dialog = await openSettle(page, payment(page, RENT, OCTOBER_5));
    await expect(dialog.getByRole('heading', { name: 'Rozlicz „Czynsz”' })).toBeVisible();
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toHaveValue('2400');
    await expect(dialog.getByRole('textbox', { name: 'Faktyczna data' })).toHaveValue(
      looseDate(OCTOBER_5),
    );
    await expect(accountSelect(dialog)).toContainText(MBANK);
    await expect(withdrawalSwitch(dialog)).toBeChecked();
    await expect(rememberSwitch(dialog)).toHaveCount(0);
    await fillSettlement(dialog, { amount: '2350' });
    await saveSettlement(dialog);
  });

  await test.step('Krok 4: after 2 350 the row is rozliczone with actual 2 350 and difference -50 zł, cost 2 350 zł, free funds 50 zł higher', async () => {
    const row = payment(page, RENT, OCTOBER_5);
    await expect(paymentStatus(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('expense-actual')).toContainText(pln('2 350,00'));
    await expect(row.getByTestId('expense-difference')).toContainText(pln('-50,00'));
    await expectCost(page, '2 350,00');
    await expectFreeFunds(page, '-2 350,00');
  });

  await test.step('Krok 5: mBank bieżące has a 2 350 zł withdrawal from 5.10 and balance 7 650 zł', async () => {
    await openBankAccount(page, MBANK);
    const withdrawal = historyRow(page, pln('2 350,00'));
    await expect(withdrawal).toHaveCount(1);
    await expect(withdrawal).toContainText(dateInText(OCTOBER_5));
    await expect(cash(page)).toContainText(pln('7 650,00'));
  });

  await test.step('Krok 6: undoing the settlement removes the badge and the withdrawal, balance 10 000 zł, cost back to 2 400 zł', async () => {
    await openMonth(page, OCTOBER);
    const row = payment(page, RENT, OCTOBER_5);
    await undo(page, row);
    await expect(paymentStatus(row)).toHaveText('oczekuje');
    await expectCost(page, '2 400,00');
    await expectFreeFunds(page, '-2 400,00');
    await openBankAccount(page, MBANK);
    await expect(cash(page)).toContainText(pln('10 000,00'));
    await expect(historyRow(page, pln('2 350,00'))).toHaveCount(0);
  });

  await test.step('Krok 7: settling 2 400 zł with the withdrawal switch off leaves the balance at 10 000 zł and creates no withdrawal', async () => {
    await openMonth(page, OCTOBER);
    const row = payment(page, RENT, OCTOBER_5);
    const dialog = await openSettle(page, row);
    await withdrawalSwitch(dialog).uncheck();
    await saveSettlement(dialog);
    await expect(paymentStatus(row)).toHaveText('rozliczone');
    await openBankAccount(page, MBANK);
    await expect(cash(page)).toContainText(pln('10 000,00'));
    await expect(historyRow(page, pln('2 400,00'))).toHaveCount(0);
  });

  await test.step('Krok 8: a withdrawal deleted by hand leaves the row settled and the undo clears only the badge', async () => {
    await openMonth(page, OCTOBER);
    const row = payment(page, RENT, OCTOBER_5);
    await undo(page, row);
    const dialog = await openSettle(page, row);
    await expect(withdrawalSwitch(dialog)).toBeChecked();
    await saveSettlement(dialog);
    await expect(paymentStatus(row)).toHaveText('rozliczone');

    await openBankAccount(page, MBANK);
    await expect(cash(page)).toContainText(pln('7 600,00'));
    const withdrawal = historyRow(page, pln('2 400,00'));
    await expect(withdrawal).toHaveCount(1);
    await withdrawal.getByRole('button', { name: 'Usuń', exact: true }).click();
    await confirmDelete(page);
    await expect(historyRow(page, pln('2 400,00'))).toHaveCount(0);
    await expect(cash(page)).toContainText(pln('10 000,00'));

    await openMonth(page, OCTOBER);
    await expect(paymentStatus(row)).toHaveText('rozliczone');
    await undo(page, row);
    await expect(paymentStatus(row)).toHaveText('oczekuje');
    await expect(snackBar(page)).toHaveCount(0);
  });

  await test.step('Krok 9: Abonament 100 zł on 3.11 without an account needs an account in the settle window', async () => {
    await openMonth(page, NOVEMBER);
    const add = await openAddExpenseDialog(page);
    await add.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
    await add.getByLabel('Nazwa').fill(SUBSCRIPTION);
    await add.getByRole('spinbutton', { name: 'Kwota', exact: true }).fill('100');
    await add.getByLabel('Data', { exact: true }).fill(NOVEMBER_3);
    await add.getByRole('button', { name: 'Zapisz' }).click();
    await expect(add).toBeHidden();
    await expect(oneOffExpenseRow(page, SUBSCRIPTION)).toHaveCount(1);

    const dialog = await openSettle(page, payment(page, SUBSCRIPTION, NOVEMBER_3));
    await expect(dialog.getByRole('button', { name: 'Zapisz' })).toBeDisabled();
  });

  await test.step('Krok 10: settling 25 EUR on Revolut EUR on 3.11 withdraws 25 EUR and counts the expense at the EUR rate in listopad', async () => {
    const row = payment(page, SUBSCRIPTION, NOVEMBER_3);
    const dialog = page.getByRole('dialog');
    await choose(page, accountSelect(dialog), new RegExp(`^${REVOLUT}`));
    await fillSettlement(dialog, { amount: '25', date: NOVEMBER_3 });
    await rememberSwitch(dialog).check();
    // The EUR rate reaches MarketData asynchronously, so a refused save is retried.
    await expect(async () => {
      await dialog.getByRole('button', { name: 'Zapisz' }).click({ timeout: 3_000 });
      await expect(dialog).toBeHidden({ timeout: 8_000 });
    }).toPass({ timeout: 90_000, intervals: [1_000, 3_000, 5_000] });
    await expect(paymentStatus(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('expense-actual')).toContainText(eur('25,00'));
    await expectCost(page, '2 506,25');
    await openBankAccount(page, REVOLUT);
    await expect(historyRow(page, eur('25,00'))).toHaveCount(1);
    await expect(cash(page)).toContainText(eur('175,00'));
  });

  await test.step('Krok 11: the settled Abonament cannot be edited or deleted; after the undo the edit works with the default account Revolut EUR', async () => {
    await openMonth(page, NOVEMBER);
    const listed = oneOffExpenseRow(page, SUBSCRIPTION);
    await expect(listed.getByTestId('edit-one-off-expense')).toHaveCount(0);
    await expect(listed.getByTestId('delete-one-off-expense')).toHaveCount(0);
    await expect(listed.getByTestId('one-off-locked-hint')).toHaveText(
      'Cofnij rozliczenie, aby edytować lub usunąć wpis.',
    );
    await undo(page, payment(page, SUBSCRIPTION, NOVEMBER_3));
    await listed.getByTestId('edit-one-off-expense').click();
    const dialog = page.getByRole('dialog');
    await expect(accountSelect(dialog)).toContainText(REVOLUT);
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 12: settling Abonament 500 on the EUR account is refused for lack of funds and nothing is saved', async () => {
    const row = payment(page, SUBSCRIPTION, NOVEMBER_3);
    const dialog = await openSettle(page, row);
    await expect(accountSelect(dialog)).toContainText(REVOLUT);
    await fillSettlement(dialog, { amount: '500', date: NOVEMBER_3 });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(
      page.getByText("The source of funds can't cover this amount").first(),
    ).toBeVisible();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(paymentStatus(row)).toHaveText('oczekuje');
    await openBankAccount(page, REVOLUT);
    await expect(cash(page)).toContainText(eur('200,00'));
  });

  await test.step('Krok 13: an archived Rezerwa is missing from the accounts of the settle window', async () => {
    const withdrawn = await request.post(
      `/api/portfolio/assets/${reserve.cashAssetId}/transactions`,
      {
        headers: { Authorization: `Bearer ${token}` },
        data: { type: TRANSACTION_WITHDRAW, quantity: 100, unitPrice: 1, date: warsawDate() },
      },
    );
    expect(withdrawn.ok()).toBeTruthy();
    await wealthTab(page, 'Konta bankowe');
    await page.getByRole('button', { name: `Akcje dla: ${RESERVE}`, exact: true }).click();
    await page.getByRole('menuitem', { name: 'Archiwizuj', exact: true }).click();
    const archive = page.getByRole('dialog');
    await archive.getByRole('button', { name: 'Archiwizuj', exact: true }).click();
    await expect(archive).toBeHidden();

    await openMonth(page, NOVEMBER);
    const dialog = await openSettle(page, payment(page, SUBSCRIPTION, NOVEMBER_3));
    await openSelect(accountSelect(dialog));
    await expect(page.getByRole('option', { name: new RegExp(`^${MBANK}`) })).toBeVisible();
    await expect(page.getByRole('option', { name: new RegExp(`^${RESERVE}`) })).toHaveCount(0);
    await page.keyboard.press('Escape');
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 14: Siłownia settled in październik on mBank bieżące with remember suggests it for 10.11 and not for 10.09', async () => {
    await openMonth(page, OCTOBER);
    await addExpense(page, {
      name: GYM,
      amount: '120',
      start: SEPTEMBER_10,
      interval: '1',
      day: 10,
    });
    const row = payment(page, GYM, OCTOBER_10);
    const dialog = await openSettle(page, row);
    await choose(page, accountSelect(dialog), new RegExp(`^${MBANK}`));
    await rememberSwitch(dialog).check();
    await saveSettlement(dialog);
    await expect(paymentStatus(row)).toHaveText('rozliczone');

    await openMonth(page, NOVEMBER);
    const november = await openSettle(page, payment(page, GYM, NOVEMBER_10));
    await expect(accountSelect(november)).toContainText(MBANK);
    await november.getByRole('button', { name: 'Anuluj' }).click();
    await expect(november).toBeHidden();

    await openMonth(page, SEPTEMBER);
    const september = await openSettle(page, payment(page, GYM, SEPTEMBER_10));
    await expect(accountSelect(september)).not.toContainText(MBANK);
    await september.getByRole('button', { name: 'Anuluj' }).click();
    await expect(september).toBeHidden();
  });

  await test.step('Krok 15: deleting Czynsz with a settled payment is refused; after the undo the delete works', async () => {
    await openMonth(page, OCTOBER);
    const row = payment(page, RENT, OCTOBER_5);
    const dialog = await openSettle(page, row);
    await saveSettlement(dialog);
    await expect(paymentStatus(row)).toHaveText('rozliczone');

    await expenseDefinition(page, RENT).getByRole('button', { name: 'Usuń', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(snackBar(page)).toContainText('has settled payments');
    await expect(expenseDefinition(page, RENT)).toHaveCount(1);

    await undo(page, row);
    await deleteExpense(page, RENT);
  });
});
