import { type Locator, type Page } from '@playwright/test';
import {
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
  type Credentials,
} from './fixtures';
import {
  COMPANY,
  MBANK,
  REVOLUT,
  addIncome,
  choose,
  confirmDelete,
  dateInText,
  definition,
  eur,
  occurrence,
  occurrences,
  openEditDialog,
  openMonth,
  pln,
  saveEdit,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;

const SEPTEMBER = '2026-09';
const DOMINIK = 'Wypłata Dominik';
const WIFE = 'Wypłata żona';
const BENEFIT = '800+';
const RENT = 'Najem EUR';

function today(): string {
  return warsawDate();
}

function looseDate(iso: string): RegExp {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new RegExp(`(?=.*\\b0?${day}\\b)(?=.*\\b0?${month}\\b)(?=.*${year})`);
}

async function createAccounts(
  request: Parameters<typeof logInThroughApi>[0],
  user: Credentials,
): Promise<void> {
  const token = await logInThroughApi(request, user);
  for (const account of [
    { name: MBANK, currency: 'PLN' },
    { name: COMPANY, currency: 'PLN' },
    { name: REVOLUT, currency: 'EUR' },
  ]) {
    const created = await request.post('/api/portfolio/accounts', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        ...account,
        type: ACCOUNT_TYPE_BANK,
        wrapper: WRAPPER_REGULAR,
        variant: VARIANT_CURRENT,
        annualInterestRatePercent: 0,
      },
    });
    expect(created.ok()).toBeTruthy();
  }
}

async function expectBalance(
  page: Page,
  balance: { plan: string; actual: string; pending: string; difference: string },
): Promise<void> {
  await expect(page.getByTestId('balance-plan').locator('dd')).toContainText(pln(balance.plan));
  await expect(page.getByTestId('balance-actual').locator('dd')).toContainText(pln(balance.actual));
  await expect(page.getByTestId('balance-pending').locator('dd')).toContainText(
    pln(balance.pending),
  );
  await expect(page.getByTestId('balance-difference').locator('dd')).toContainText(
    pln(balance.difference),
  );
}

function status(row: Locator): Locator {
  return row.getByTestId('occurrence-status');
}

function settleDialog(page: Page): Locator {
  return page.getByRole('dialog');
}

async function openSettle(page: Page, row: Locator, button: string): Promise<Locator> {
  await row.getByTestId(button).click();
  const dialog = settleDialog(page);
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

function historyRow(page: Page, text: string | RegExp): Locator {
  return page.locator('tr[data-transaction-id]').filter({ hasText: text });
}

function snackBar(page: Page): Locator {
  return page.locator('mat-snack-bar-container');
}

test('income settlement: settle, skip, edit and undo occurrences with the cash deposit and the month balance', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  await createAccounts(request, user);
  await page.goto('/login');
  await logInThroughUi(page, user);

  await openMonth(page, SEPTEMBER);
  for (const income of [
    {
      name: DOMINIK,
      amount: '9200',
      account: MBANK,
      start: '2026-01-01',
      interval: '1',
      day: 'last' as const,
    },
    {
      name: WIFE,
      amount: '6500',
      account: MBANK,
      start: '2026-01-01',
      interval: '1',
      day: 10,
      shift: 'Poprzedni dzień roboczy' as const,
    },
    {
      name: BENEFIT,
      amount: '800',
      account: MBANK,
      start: '2024-06-10',
      interval: '1',
      day: 10,
    },
    {
      name: RENT,
      amount: '1000',
      account: REVOLUT,
      start: '2026-01-01',
      interval: '1',
      day: 15,
    },
  ]) {
    await addIncome(page, income);
  }

  await test.step('Krok 1: four pending occurrences in wrzesień 2026, none overdue, no "Do rozliczenia" section and no counter at Planowanie', async () => {
    await openMonth(page, SEPTEMBER);
    for (const [name, date] of [
      [BENEFIT, '2026-09-10'],
      [WIFE, '2026-09-10'],
      [RENT, '2026-09-15'],
      [DOMINIK, '2026-09-30'],
    ] as const) {
      const row = occurrence(page, name, date);
      await expect(status(row)).toHaveText('oczekuje');
      await expect(row).not.toHaveClass(/overdue/);
    }
    await expect(page.getByTestId('occurrence-row')).toHaveCount(4);
    await expect(page.getByTestId('overdue-section')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Do rozliczenia' })).toHaveCount(0);
    await expect(page.getByTestId('overdue-badge')).toHaveCount(0);
  });

  await test.step('Krok 2: settling Wypłata Dominik with 9 143,27 zł on 2026-09-30 gives rozliczone, plan 9 200, actual 9 143,27, difference -56,73 zł', async () => {
    const row = occurrence(page, DOMINIK, '2026-09-30');
    const dialog = await openSettle(page, row, 'settle-occurrence');
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toHaveValue('9200');
    await expect(dialog.getByRole('textbox', { name: 'Faktyczna data' })).toHaveValue(
      looseDate(today()),
    );
    await expect(dialog.getByRole('combobox', { name: 'Konto' })).toContainText(MBANK);
    await expect(dialog.getByRole('checkbox', { name: 'Utwórz wpłatę na konto' })).toBeChecked();
    await fillSettlement(dialog, { amount: '9143.27', date: '2026-09-30' });
    await saveSettlement(dialog);
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row).toContainText(pln('9 200,00'));
    await expect(row.getByTestId('occurrence-actual')).toContainText(pln('9 143,27'));
    await expect(row.getByTestId('occurrence-difference')).toContainText(pln('-56,73'));
  });

  await test.step('Krok 3: mBank ROR history has a 9 143,27 zł deposit from 30.09.2026 named Wypłata Dominik without edit or delete, balance 9 143,27 zł', async () => {
    await openBankAccount(page, MBANK);
    const deposit = historyRow(page, 'Wpływ · Wypłata Dominik');
    await expect(deposit).toHaveCount(1);
    await expect(deposit).toContainText(dateInText('2026-09-30'));
    await expect(deposit).toContainText(pln('9 143,27'));
    await expect(deposit.getByRole('button')).toHaveCount(0);
    await expect(cash(page)).toContainText(pln('9 143,27'));
  });

  await test.step('Krok 4: settling Wypłata żona on 2026-09-10 into Konto firmowe (the list has no Revolut EUR) gives rozliczone and 6 500 zł on Konto firmowe', async () => {
    await openMonth(page, SEPTEMBER);
    const row = occurrence(page, WIFE, '2026-09-10');
    const dialog = await openSettle(page, row, 'settle-occurrence');
    await fillSettlement(dialog, { amount: '6500', date: '2026-09-10' });
    await dialog.getByRole('combobox', { name: 'Konto' }).click();
    await expect(page.getByRole('option', { name: /^Konto firmowe/ })).toBeVisible();
    await expect(page.getByRole('option', { name: /Revolut EUR/ })).toHaveCount(0);
    await page.getByRole('option', { name: /^Konto firmowe/ }).click();
    await saveSettlement(dialog);
    await expect(status(row)).toHaveText('rozliczone');
    await openBankAccount(page, COMPANY);
    await expect(cash(page)).toContainText(pln('6 500,00'));
  });

  await test.step('Krok 5: settling Najem EUR with 980 on 2026-09-15 gives a difference of -20 EUR without an estimate and 980 EUR on Revolut EUR', async () => {
    await openMonth(page, SEPTEMBER);
    const row = occurrence(page, RENT, '2026-09-15');
    const dialog = await openSettle(page, row, 'settle-occurrence');
    await expect(dialog.getByRole('combobox', { name: 'Konto' })).toContainText(REVOLUT);
    await fillSettlement(dialog, { amount: '980', date: '2026-09-15' });
    // The EUR rate reaches MarketData asynchronously, so a refused save is retried.
    await expect(async () => {
      await dialog.getByRole('button', { name: 'Zapisz' }).click({ timeout: 3_000 });
      await expect(dialog).toBeHidden({ timeout: 8_000 });
    }).toPass({ timeout: 90_000, intervals: [1_000, 3_000, 5_000] });
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('occurrence-actual')).toContainText(eur('980,00'));
    await expect(row.getByTestId('occurrence-difference')).toContainText(eur('-20,00'));
    await expect(row).not.toContainText('≈');
    await openBankAccount(page, REVOLUT);
    await expect(cash(page)).toContainText(eur('980,00'));
  });

  await test.step('Krok 6: the balance of wrzesień is plan 20 750 zł, actual 19 808,27 zł, pending 800 zł, difference -141,73 zł', async () => {
    await openMonth(page, SEPTEMBER);
    await expectBalance(page, {
      plan: '20 750,00',
      actual: '19 808,27',
      pending: '800,00',
      difference: '-141,73',
    });
  });

  await test.step('Krok 7: editing the settlement of Wypłata żona to mBank ROR gives Konto firmowe 0 zł and mBank ROR 15 643,27 zł', async () => {
    const row = occurrence(page, WIFE, '2026-09-10');
    const dialog = await openSettle(page, row, 'edit-settlement');
    await expect(
      dialog.getByRole('heading', { name: 'Edytuj rozliczenie „Wypłata żona”' }),
    ).toBeVisible();
    await choose(page, dialog.getByRole('combobox', { name: 'Konto' }), /^mBank ROR/);
    await saveSettlement(dialog);
    await openBankAccount(page, COMPANY);
    await expect(cash(page)).toContainText(pln('0,00'));
    await openBankAccount(page, MBANK);
    await expect(cash(page)).toContainText(pln('15 643,27'));
  });

  await test.step('Krok 8: a manual withdrawal of 15 000 zł from mBank ROR leaves 643,27 zł', async () => {
    await page.getByRole('button', { name: 'Wypłata', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Kwota').fill('15000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(cash(page)).toContainText(pln('643,27'));
  });

  await test.step('Krok 9: undoing the settlement of Wypłata Dominik is refused because the account cash would drop below zero', async () => {
    await openMonth(page, SEPTEMBER);
    const row = occurrence(page, DOMINIK, '2026-09-30');
    await row.getByTestId('undo-occurrence').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(snackBar(page)).toContainText("The source of funds can't cover this amount");
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('deposit-created')).toBeVisible();
  });

  await test.step('Krok 10: deleting the manual withdrawal and settling Wypłata Dominik without a deposit leaves mBank ROR at 6 500 zł and the balance unchanged', async () => {
    await openBankAccount(page, MBANK);
    const withdrawal = historyRow(page, pln('15 000,00'));
    await expect(withdrawal).toHaveCount(1);
    await withdrawal.getByRole('button', { name: 'Usuń', exact: true }).click();
    await confirmDelete(page);
    await expect(cash(page)).toContainText(pln('15 643,27'));

    await openMonth(page, SEPTEMBER);
    const row = occurrence(page, DOMINIK, '2026-09-30');
    const dialog = await openSettle(page, row, 'edit-settlement');
    await dialog.getByRole('checkbox', { name: 'Utwórz wpłatę na konto' }).uncheck();
    await saveSettlement(dialog);
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('deposit-created')).toHaveCount(0);
    await expect(row.getByTestId('occurrence-actual')).toContainText(pln('9 143,27'));
    await expectBalance(page, {
      plan: '20 750,00',
      actual: '19 808,27',
      pending: '800,00',
      difference: '-141,73',
    });
    await openBankAccount(page, MBANK);
    await expect(historyRow(page, 'Wpływ · Wypłata Dominik')).toHaveCount(0);
    await expect(cash(page)).toContainText(pln('6 500,00'));
  });

  await test.step('Krok 11: skipping 800+ in wrzesień gives pominięte, pending 0 zł and difference -941,73 zł', async () => {
    await openMonth(page, SEPTEMBER);
    const row = occurrence(page, BENEFIT, '2026-09-10');
    await row.getByTestId('skip-occurrence').click();
    await expect(status(row)).toHaveText('pominięte');
    await expectBalance(page, {
      plan: '20 750,00',
      actual: '19 808,27',
      pending: '0,00',
      difference: '-941,73',
    });
  });

  await test.step('Krok 12: undoing the skip of 800+ brings back oczekuje and the difference -141,73 zł', async () => {
    const row = occurrence(page, BENEFIT, '2026-09-10');
    await row.getByTestId('undo-occurrence').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(status(row)).toHaveText('oczekuje');
    await expectBalance(page, {
      plan: '20 750,00',
      actual: '19 808,27',
      pending: '800,00',
      difference: '-141,73',
    });
  });

  await test.step('Krok 13: a settlement of Wypłata żona dated tomorrow is rejected', async () => {
    const row = occurrence(page, WIFE, '2026-09-10');
    const dialog = await openSettle(page, row, 'edit-settlement');
    await fillSettlement(dialog, { date: warsawDate(-1) });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByText('Data nie może być późniejsza niż dzisiaj.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(status(row)).toHaveText('rozliczone');
  });

  await test.step('Krok 14: settling Wypłata Dominik of listopad 2026 with today and 9 200 zł keeps the row in listopad with today as the actual date and adds a 9 200 zł deposit', async () => {
    await openMonth(page, '2026-11');
    const row = occurrence(page, DOMINIK, '2026-11-30');
    const dialog = await openSettle(page, row, 'settle-occurrence');
    await saveSettlement(dialog);
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('occurrence-actual')).toContainText(pln('9 200,00'));
    await expect(row.getByTestId('occurrence-actual')).toContainText(dateInText(today()));
    await openBankAccount(page, MBANK);
    await expect(historyRow(page, 'Wpływ · Wypłata Dominik')).toContainText(pln('9 200,00'));
    await expect(cash(page)).toContainText(pln('15 700,00'));
  });

  await test.step('Krok 15: Wypłata Dominik at 9 800 zł from wrzesień 2026 keeps wrzesień and listopad at 9 200 zł and shows 9 800 zł in październik', async () => {
    await openMonth(page, '2026-10');
    const dialog = await openEditDialog(page, DOMINIK);
    await dialog.getByLabel('Obowiązuje od').fill(SEPTEMBER);
    await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('9800');
    await saveEdit(dialog);
    await expect(occurrence(page, DOMINIK, '2026-10-30')).toContainText(pln('9 800,00'));
    await expect(status(occurrence(page, DOMINIK, '2026-10-30'))).toHaveText('oczekuje');
    await openMonth(page, SEPTEMBER);
    const september = occurrence(page, DOMINIK, '2026-09-30');
    await expect(status(september)).toHaveText('rozliczone');
    await expect(september).toContainText(pln('9 200,00'));
    await openMonth(page, '2026-11');
    const november = occurrence(page, DOMINIK, '2026-11-30');
    await expect(status(november)).toHaveText('rozliczone');
    await expect(november).toContainText(pln('9 200,00'));
  });

  await test.step('Krok 16: ending Wypłata Dominik on 2026-08-31 keeps wrzesień and listopad marked poza regułą and drops październik', async () => {
    await definition(page, DOMINIK).getByRole('button', { name: 'Zakończ', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Data końca').fill('2026-08-31');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(occurrence(page, DOMINIK, '2026-11-30').getByTestId('out-of-rule')).toHaveText(
      'poza regułą',
    );
    await openMonth(page, SEPTEMBER);
    await expect(occurrence(page, DOMINIK, '2026-09-30').getByTestId('out-of-rule')).toHaveText(
      'poza regułą',
    );
    await openMonth(page, '2026-10');
    await expect(occurrences(page, DOMINIK)).toHaveCount(0);
  });

  await test.step('Krok 17: deleting the definition Wypłata Dominik is refused', async () => {
    await definition(page, DOMINIK).getByRole('button', { name: 'Usuń', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(snackBar(page)).toContainText('has settled occurrences');
    await expect(definition(page, DOMINIK)).toHaveCount(1);
  });

  await test.step('Krok 18: Premia of 2 000 zł skipped in wrzesień can be deleted and disappears from wrzesień', async () => {
    await openMonth(page, SEPTEMBER);
    await addIncome(page, {
      name: 'Premia',
      amount: '2000',
      account: MBANK,
      start: '2026-09-01',
      interval: '12',
      day: 20,
    });
    const row = occurrence(page, 'Premia', '2026-09-20');
    await row.getByTestId('skip-occurrence').click();
    await expect(status(row)).toHaveText('pominięte');
    await definition(page, 'Premia').getByRole('button', { name: 'Usuń', exact: true }).click();
    await confirmDelete(page);
    await expect(definition(page, 'Premia')).toHaveCount(0);
    await expect(occurrences(page, 'Premia')).toHaveCount(0);
  });

  await test.step('Krok 19: deleting the account mBank ROR is refused', async () => {
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Konta bankowe', exact: true })
      .click();
    await page.getByRole('button', { name: `Akcje dla: ${MBANK}`, exact: true }).click();
    await page.getByRole('menuitem', { name: 'Usuń', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(snackBar(page)).toContainText('target of a recurring income');
    await expect(page.getByRole('link', { name: MBANK, exact: true })).toBeVisible();
  });

  await test.step('Krok 20: undoing the settlement of Najem EUR removes 980 EUR from Revolut EUR and brings back oczekuje with an estimate', async () => {
    await openMonth(page, SEPTEMBER);
    const row = occurrence(page, RENT, '2026-09-15');
    await row.getByTestId('undo-occurrence').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(status(row)).toHaveText('oczekuje');
    // The latest rate comes from MarketData, so each attempt opens the month afresh.
    await expect(async () => {
      await openMonth(page, SEPTEMBER);
      await expect(row).toContainText(/≈\s4\s?250,00\szł/, { timeout: 3_000 });
    }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 3_000] });
    await openBankAccount(page, REVOLUT);
    await expect(cash(page)).toContainText(eur('0,00'));
  });
});
