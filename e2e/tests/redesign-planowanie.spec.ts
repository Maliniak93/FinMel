import type { Locator, Page } from '@playwright/test';
import { expect, test, warsawDate } from './fixtures';
import {
  addExpense,
  choose,
  confirmDelete,
  dateInText,
  exactly,
  expenseDefinition,
  expectReserve,
  fillIncome,
  monthLabel,
  occurrences,
  openAddDialog,
  openAddExpenseDialog,
  openMonth,
  pln,
  shiftMonth,
  status,
} from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const VARIANT_SAVINGS = 1;
const KIND_CUSHION = 1;

const MBANK = 'mBank osobiste';
const SAVINGS = 'Konto oszczędnościowe';
const DEPOSIT = 'Lokata na miesiąc';
const CUSHION = 'Poduszka finansowa';
const PENSION = 'Emerytura';
const HOLIDAY = 'Wakacje';
const SALARY = 'Wynagrodzenie';
const PREMIA = 'Premia kwartalna';
const REFUND = 'Zwrot podatku';
const RENT = 'Czynsz';
const POWER = 'Prąd';
const GIFT = 'Prezent';
const INSURANCE = 'Ubezpieczenie OC';
const OLD_PURCHASE = 'Stary zakup';

const WIDE = { width: 1440, height: 900 };
const MID = { width: 1000, height: 900 };
const PHONE = { width: 390, height: 844 };

const TODAY = warsawDate();
const MONTH = TODAY.slice(0, 7);
const YEAR = Number(TODAY.slice(0, 4));
const PREMIA_DATE = warsawDate(23);
const PREMIA_MONTH = PREMIA_DATE.slice(0, 7);
const REFUND_DATE = warsawDate(2);
const REFUND_MONTH = REFUND_DATE.slice(0, 7);
const GIFT_DATE = addDays(TODAY, 5);
const GIFT_MONTH = GIFT_DATE.slice(0, 7);
const DEPOSIT_DAY = `${MONTH}-28`;
const DEPOSIT_START = `${shiftMonth(MONTH, -1)}-28`;

function addDays(iso: string, days: number): string {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function dayGroup(page: Page, iso: string): Locator {
  return page.locator(`[data-testid="day-group"][data-date="${iso}"]`);
}

function paymentOn(page: Page, iso: string, name: string): Locator {
  return dayGroup(page, iso)
    .getByTestId('expense-payment-row')
    .filter({ has: page.getByTestId('row-name').filter({ hasText: exactly(name) }) });
}

function overdueRow(page: Page, name: string): Locator {
  return page.getByTestId('overdue-row').filter({ hasText: name });
}

function shareInput(page: Page, group: string): Locator {
  return page.getByRole('spinbutton', { name: `${group} (%)` });
}

function yearRow(page: Page, month: string): Locator {
  return page.getByTestId('year-row').nth(Number(month.slice(5)) - 1);
}

function todoItem(page: Page, name: string): Locator {
  return page.getByTestId('todo-card-item').filter({ hasText: name });
}

async function openOneOffIncome(
  page: Page,
  income: { name: string; amount: string; date: string },
): Promise<void> {
  await openMonth(page, income.date.slice(0, 7));
  const dialog = await openAddDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${MBANK}`),
  );
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function addOneOffExpense(
  page: Page,
  expense: { name: string; amount: string; date: string },
): Promise<void> {
  await openMonth(page, expense.date.slice(0, 7));
  const dialog = await openAddExpenseDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(expense.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota', exact: true }).fill(expense.amount);
  await dialog.getByLabel('Data', { exact: true }).fill(expense.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function createGroup(
  page: Page,
  token: string,
  data: Record<string, unknown>,
): Promise<string> {
  const created = await page.request.post('/api/portfolio/asset-groups', {
    headers: authorization(token),
    data,
  });
  expect(created.ok()).toBeTruthy();
  return ((await created.json()) as { id: string }).id;
}

async function overflow(page: Page): Promise<number> {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
}

test('redesign planowanie: header, day list, cascade, split, calendar, year, dialogs, phone, dark theme and section errors', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(900_000);
  await page.setViewportSize(WIDE);

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;
  const headers = authorization(token);

  const mbank = await request.post('/api/portfolio/accounts', {
    headers,
    data: {
      name: MBANK,
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(mbank.ok()).toBeTruthy();
  const savings = await request.post('/api/portfolio/accounts', {
    headers,
    data: {
      name: SAVINGS,
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: VARIANT_SAVINGS,
      annualInterestRatePercent: 0,
    },
  });
  expect(savings.ok()).toBeTruthy();
  const mbankBody = (await mbank.json()) as { id: string; cashAssetId: string };
  const savingsBody = (await savings.json()) as { id: string; cashAssetId: string };
  for (const [cashAssetId, quantity] of [
    [mbankBody.cashAssetId, 10000],
    [savingsBody.cashAssetId, 50000],
  ] as const) {
    const deposited = await request.post(`/api/portfolio/assets/${cashAssetId}/transactions`, {
      headers,
      data: { type: 2, quantity, unitPrice: 1, date: warsawDate(100) },
    });
    expect(deposited.ok()).toBeTruthy();
  }
  const deposit = await request.post('/api/portfolio/deposits', {
    headers,
    data: {
      name: DEPOSIT,
      sourceAccountId: savingsBody.id,
      principal: 20000,
      startDate: DEPOSIT_START,
      termLength: 1,
      termUnit: 1,
      annualInterestRatePercent: 0,
      capitalization: 0,
    },
  });
  expect(deposit.ok()).toBeTruthy();
  await createGroup(page, token, {
    name: CUSHION,
    color: 'orange',
    kind: KIND_CUSHION,
    targetPln: 60000,
    contributions: [{ amountPln: 1000 }],
  });
  const pensionId = await createGroup(page, token, {
    name: PENSION,
    color: 'purple',
    contributions: [{ amountPln: 1500 }],
  });

  await test.step('Krok 1: Planowanie has the header, tabs, month navigation, the Lista / Kalendarz switch, only the deposit deadline in the list and a zero balance', async () => {
    await page
      .getByTestId('nav-list')
      .getByRole('link', { name: 'Planowanie', exact: true })
      .click();
    await expect(page).toHaveURL(/\/planning/);
    await expect(page.getByRole('heading', { name: 'Planowanie', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Dodaj wydatek' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Dodaj wpływ' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Miesiąc', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Rok', exact: true })).toBeVisible();
    await expect(page.getByTestId('month-label')).toHaveText(monthLabel(MONTH));
    await expect(page.getByTestId('layout-list')).toContainText('Lista');
    await expect(page.getByTestId('layout-calendar')).toContainText('Kalendarz');
    await expect(page.getByTestId('deadline-row')).toHaveCount(1);
    await expect(page.getByTestId('day-group')).toHaveCount(1);
    await expect(page.getByTestId('balance-plan').locator('dd')).toContainText(pln('0,00'));
    await expect(page.getByTestId('balance-actual').locator('dd')).toContainText(pln('0,00'));
    await expect(page.getByTestId('balance-pending').locator('dd')).toContainText(pln('0,00'));
    await expect(page.getByTestId('balance-difference').locator('dd')).toContainText(pln('0,00'));
  });

  await test.step('Krok 2: Dodaj wpływ has the Cykliczny / Jednorazowy switch, an empty name shows the error under the field, Wynagrodzenie 11 400 zł lands on its day with a status', async () => {
    const dialog = await openAddDialog(page);
    await expect(dialog.getByRole('radio', { name: 'Cykliczny', exact: true })).toBeChecked();
    await expect(dialog.getByRole('radio', { name: 'Jednorazowy', exact: true })).toBeVisible();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByText('Nazwa jest wymagana.')).toBeVisible();
    await expect(dialog.locator('.mat-form-field-invalid').first()).toBeVisible();
    await fillIncome(page, dialog, {
      name: SALARY,
      amount: '11400',
      account: MBANK,
      start: `${MONTH}-01`,
      interval: '1',
      day: 9,
      shift: 'Poprzedni dzień roboczy',
    });
    await expect(dialog.getByRole('spinbutton', { name: 'Kwota' })).toHaveCSS(
      'text-align',
      'right',
    );
    await expect(dialog.getByText('zł', { exact: true }).first()).toBeVisible();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    const row = occurrences(page, SALARY);
    await expect(row).toHaveCount(1);
    await expect(row).toContainText(pln('11 400,00'));
    await expect(row.locator('xpath=ancestor::*[@data-testid="day-group"]')).toHaveAttribute(
      'data-date',
      new RegExp(`^${MONTH}-\\d{2}$`),
    );
    await expect(row.getByTestId('occurrence-status').locator('mat-icon')).toHaveCount(1);
    await expect(status(row)).toHaveText(/^(oczekuje|zaległe)$/);
  });

  await test.step('Krok 3: the one-off Premia kwartalna from 23 days ago waits in "Do rozliczenia" with Rozlicz and is not in the day list', async () => {
    await openOneOffIncome(page, { name: PREMIA, amount: '1800', date: PREMIA_DATE });
    await openMonth(page, MONTH);
    const section = page.getByTestId('overdue-section');
    await expect(section.getByRole('heading', { name: 'Do rozliczenia' })).toBeVisible();
    const row = overdueRow(page, PREMIA);
    await expect(row).toHaveCount(1);
    await expect(row).toContainText(dateInText(PREMIA_DATE));
    await expect(row).toContainText(MBANK);
    await expect(row).toContainText(pln('1 800,00'));
    await expect(row.getByRole('button', { name: 'Rozlicz' })).toBeVisible();
    await expect(row.getByRole('button', { name: 'Pomiń' })).toHaveCount(0);
    if (PREMIA_MONTH !== MONTH) {
      await expect(occurrences(page, PREMIA)).toHaveCount(0);
    }
  });

  await test.step('Krok 4: Czynsz stands on its payment day, Prąd on its payment day two months after the start, Prezent is marked jednorazowy and Prąd shows 140,00 zł/mies. and the next payment', async () => {
    await openMonth(page, MONTH);
    await addExpense(page, {
      name: RENT,
      amount: '1450',
      start: `${MONTH}-01`,
      interval: '1',
      day: 5,
    });
    await addExpense(page, {
      name: POWER,
      amount: '280',
      start: `${shiftMonth(MONTH, -1)}-01`,
      interval: '2',
      day: 12,
    });
    await addOneOffExpense(page, { name: GIFT, amount: '300', date: GIFT_DATE });
    await openMonth(page, MONTH);
    await expect(paymentOn(page, `${MONTH}-05`, RENT)).toHaveCount(1);
    await expect(paymentOn(page, `${MONTH}-12`, POWER)).toHaveCount(0);
    await openMonth(page, shiftMonth(MONTH, 1));
    await expect(paymentOn(page, `${shiftMonth(MONTH, 1)}-12`, POWER)).toHaveCount(1);
    await openMonth(page, GIFT_MONTH);
    const gift = paymentOn(page, GIFT_DATE, GIFT);
    await expect(gift).toHaveCount(1);
    await expect(gift.getByTestId('one-off-mark')).toHaveText('jednorazowy');
    await openMonth(page, MONTH);
    await expectReserve(page, POWER, '140,00');
    await expect(expenseDefinition(page, POWER).getByTestId('expense-next-payment')).toContainText(
      'najbliższa płatność',
    );
  });

  await test.step('Krok 5: the deposit deadline stands on its day as "Koniec lokaty" and the "bez daty" block lists the group contributions in the group colours', async () => {
    const deadline = dayGroup(page, DEPOSIT_DAY).getByTestId('deadline-row');
    await expect(deadline).toHaveCount(1);
    await expect(deadline).toContainText('Koniec lokaty');
    await expect(deadline).toContainText(DEPOSIT);
    await expect(deadline).toContainText(SAVINGS);
    await expect(deadline).toContainText(pln('20 000,00'));
    const block = page.getByTestId('no-date-block');
    await expect(block).toContainText('bez daty');
    const cushion = block.getByTestId('contribution-row').filter({ hasText: CUSHION });
    const pension = block.getByTestId('contribution-row').filter({ hasText: PENSION });
    await expect(cushion).toContainText(
      new RegExp(`${pln('0,00').source}\\s/\\s${pln('1 000,00').source}`),
    );
    await expect(pension).toContainText(
      new RegExp(`${pln('0,00').source}\\s/\\s${pln('1 500,00').source}`),
    );
    await expect(cushion.locator('.day-list__dot')).toHaveAttribute('style', /--sk-g-/);
    await expect(pension.locator('.day-list__dot')).toHaveAttribute('style', /--sk-g-/);
  });

  await test.step('Krok 6: settling Premia for 1 700 zł removes it from "Do rozliczenia", the month shows "Rozliczono" with the difference -100,00 zł and Cofnij asks first with focus on Anuluj', async () => {
    await overdueRow(page, PREMIA).getByTestId('settle-overdue').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
    await expect(dialog.getByRole('textbox', { name: 'Faktyczna data' })).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Konto' })).toBeVisible();
    await expect(dialog.getByRole('checkbox', { name: 'Utwórz wpłatę na konto' })).toBeVisible();
    await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill('1700');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(overdueRow(page, PREMIA)).toHaveCount(0);
    await openMonth(page, PREMIA_MONTH);
    const row = occurrences(page, PREMIA);
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('occurrence-settled-on')).toContainText('Rozliczono');
    await expect(row.getByTestId('occurrence-actual')).toContainText(pln('1 700,00'));
    await expect(row.getByTestId('occurrence-difference')).toContainText(pln('-100,00'));
    await row.getByTestId('undo-occurrence').click();
    const undo = page.getByRole('dialog');
    await expect(undo.getByRole('button', { name: 'Anuluj' })).toBeFocused();
    await undo.getByRole('button', { name: 'Anuluj' }).click();
    await expect(undo).toBeHidden();
    await expect(status(row)).toHaveText('rozliczone');
  });

  await test.step('Krok 7: a skipped income is struck out with "pominięte" and an income past its date shows "zaległe"', async () => {
    await openMonth(page, MONTH);
    const salary = occurrences(page, SALARY);
    await salary.getByTestId('skip-occurrence').click();
    await expect(status(salary)).toHaveText('pominięte');
    await expect(
      salary.locator('xpath=self::*[contains(@class,"day-list__item--skipped")]'),
    ).toHaveCount(1);
    await expect(salary.getByTestId('occurrence-status').locator('mat-icon')).toHaveCount(1);
    await openOneOffIncome(page, { name: REFUND, amount: '250', date: REFUND_DATE });
    await openMonth(page, REFUND_MONTH);
    const late = occurrences(page, REFUND);
    await expect(status(late)).toHaveText('zaległe');
    await expect(late.getByTestId('occurrence-status').locator('mat-icon')).toHaveCount(1);
  });

  await test.step('Krok 8: the "Bilans miesiąca" column has Plan, Faktycznie, Oczekuje, Różnica and stays pinned beside the list (an income without a rate: tylko manualnie)', async () => {
    await openMonth(page, MONTH);
    const balance = page.getByTestId('month-balance');
    await expect(balance.getByRole('heading', { name: 'Bilans miesiąca' })).toBeVisible();
    for (const label of ['Plan', 'Faktycznie', 'Oczekuje', 'Różnica']) {
      await expect(balance.getByText(label, { exact: true })).toBeVisible();
    }
    await expect(page.locator('.planning-layout__aside')).toHaveCSS('position', 'sticky');
  });

  await test.step('Krok 9: the "Wolne środki" cascade has Plan and Faktyczne columns with Wpływy, Wydatki, Wpłaty do grup with the group bar and Wolne środki', async () => {
    const section = page.getByTestId('free-funds');
    await expect(section.getByRole('heading', { name: 'Wolne środki' })).toBeVisible();
    await expect(section.getByRole('heading', { name: 'Plan', exact: true })).toBeVisible();
    await expect(section.getByRole('heading', { name: 'Faktyczne', exact: true })).toBeVisible();
    for (const column of ['plan', 'actual']) {
      await expect(page.getByTestId(`cascade-${column}-incomes`)).toContainText('Wpływy');
      await expect(page.getByTestId(`cascade-${column}-expenses`)).toContainText('− Wydatki');
      await expect(page.getByTestId(`cascade-${column}-contributions`)).toContainText(
        '− Wpłaty do grup',
      );
    }
    await expect(page.getByTestId('free-funds-plan')).toContainText('= Wolne środki');
    await expect(page.getByTestId('free-funds-actual')).toContainText('= Wolne środki');
    await expect(
      page.getByTestId('cascade-bar-plan-contributions').locator('app-split-bar [role="img"]'),
    ).toBeVisible();
    await expect(page.getByTestId('expense-cost').locator('dt')).toHaveText(
      'Koszt miesiąca (z rezerwami)',
    );
  });

  await test.step('Krok 10: "Podział wolnych środków" has the info, the cushion level, shares summing to 100%, a warning with disabled save for another sum and a saved correction', async () => {
    const holiday = await request.post('/api/portfolio/asset-groups', {
      headers,
      data: { name: HOLIDAY, color: 'blue', contributions: [] },
    });
    expect(holiday.ok()).toBeTruthy();
    await openMonth(page, MONTH);
    const split = page.getByTestId('split-section');
    await expect(split.getByRole('heading', { name: 'Podział wolnych środków' })).toBeVisible();
    await expect(split).toContainText('Tylko propozycja');
    await expect(page.getByTestId('split-cushion')).toContainText(/Stan:\s.*\/\s60\s000,00\szł/);
    await expect(page.getByTestId('split-cushion')).toContainText('Brakuje:');
    await expect(page.getByTestId('share-sum')).toContainText(/Suma:\s*100\s?%/);
    const save = page.getByTestId('shares-save');
    await expect(save).toBeDisabled();
    await shareInput(page, PENSION).fill('60');
    await expect(page.getByTestId('share-sum')).toContainText(/Suma:\s*60\s?%/);
    await expect(page.getByTestId('share-sum-icon')).toHaveText('warning');
    await expect(save).toBeDisabled();
    await shareInput(page, HOLIDAY).fill('40');
    await expect(page.getByTestId('share-sum')).toContainText(/Suma:\s*100\s?%/);
    await expect(page.getByTestId('share-sum-icon')).toHaveText('check_circle');
    await expect(save).toBeEnabled();
    await save.click();
    await expect(save).toBeDisabled();
    await openMonth(page, MONTH);
    await expect(shareInput(page, PENSION)).toHaveValue('60');
    await expect(shareInput(page, HOLIDAY)).toHaveValue('40');
  });

  await test.step('Krok 11: an Emerytura contribution above the free funds shows "deficyt" with the amount in the cascade and no split', async () => {
    const raised = await request.put(`/api/portfolio/asset-groups/${pensionId}`, {
      headers,
      data: { name: PENSION, color: 'purple', kind: 0, contributions: [{ amountPln: 100000 }] },
    });
    expect(raised.ok()).toBeTruthy();
    await openMonth(page, MONTH);
    const deficit = page.getByTestId('free-funds-plan').getByTestId('free-funds-deficit');
    await expect(deficit).toContainText('deficyt');
    await expect(deficit).toContainText(/[-−]/);
    await expect(deficit.locator('mat-icon')).toBeVisible();
    await expect(page.getByTestId('split-deficit')).toContainText('Deficyt, nie ma co dzielić');
    await expect(page.getByTestId('split-cushion')).not.toContainText('Przydział');
  });

  await test.step('Krok 12: Kalendarz is a Monday-first grid with statuses, at most 3 items per cell, today marked, layout=calendar in the address and a day click listing its items (the "+N" overflow: tylko manualnie)', async () => {
    await page.getByTestId('layout-calendar').click();
    await expect(page).toHaveURL(/layout=calendar/);
    const grid = page.getByTestId('planning-calendar');
    await expect(grid).toBeVisible();
    await expect(grid.locator('.planning-calendar__weekdays span').first()).toHaveText('pon');
    const cells = page.getByTestId('calendar-cell');
    expect((await cells.count()) % 7).toBe(0);
    for (const cell of await cells.all()) {
      expect(await cell.getByTestId('calendar-item').count()).toBeLessThanOrEqual(3);
    }
    await expect(page.locator(`[data-testid="calendar-cell"][data-today="true"]`)).toHaveAttribute(
      'data-date',
      TODAY,
    );
    await expect(
      page
        .locator(`[data-testid="calendar-cell"][data-date="${MONTH}-05"]`)
        .getByTestId('calendar-item')
        .filter({ hasText: RENT }),
    ).toHaveCount(1);
    await page.locator(`[data-testid="calendar-cell"][data-date="${DEPOSIT_DAY}"]`).click();
    await expect(page.getByTestId('calendar-details')).toContainText('Koniec lokaty');
    await expect(page.getByTestId('calendar-details')).toContainText(DEPOSIT);
    await page.getByTestId('layout-list').click();
    await expect(page).not.toHaveURL(/layout=calendar/);
  });

  await test.step('Krok 13: Rok shows no chart, the table with the current month marked with Razem, actual values for past months and a row opening that month', async () => {
    await page.getByRole('tab', { name: 'Rok', exact: true }).click();
    await expect(page).toHaveURL(/tab=year/);
    await expect(page.getByTestId('year-chart-column')).toHaveCount(0);
    await expect(page.locator('[data-testid="year-row"][aria-current="date"]')).toHaveCount(1);
    for (const column of [
      'Miesiąc',
      'Wpływy plan',
      'Wpływy faktycznie',
      'Różnica',
      'Wydatki',
      'Wolne środki plan',
      'Wolne środki faktyczne',
    ]) {
      await expect(page.getByRole('columnheader', { name: column, exact: true })).toBeVisible();
    }
    await expect(page.getByTestId('year-row')).toHaveCount(12);
    await expect(page.getByTestId('year-total')).toContainText('Razem');
    await expect(yearRow(page, MONTH)).toContainText('deficyt');
    if (PREMIA_MONTH < MONTH && PREMIA_MONTH.slice(0, 4) === MONTH.slice(0, 4)) {
      await expect(page.getByTestId('year-result-actual').first()).toBeVisible();
      await yearRow(page, PREMIA_MONTH).click();
      await expect(page.getByTestId('month-label')).toHaveText(monthLabel(PREMIA_MONTH));
    }
  });

  await test.step('Krok 14: a year without definitions shows one empty state and the year arrows are disabled at the first and last year', async () => {
    await addOneOffExpense(page, {
      name: OLD_PURCHASE,
      amount: '100',
      date: `${YEAR - 2}-06-15`,
    });
    await page.goto(`/planning?tab=year&year=${YEAR}`);
    await expect(page.getByTestId('year-label')).toHaveText(String(YEAR));
    await page.getByTestId('year-previous').click();
    await expect(page.getByTestId('year-label')).toHaveText(String(YEAR - 1));
    await expect(page.getByText('Brak definicji', { exact: true })).toHaveCount(1);
    await expect(page.getByTestId('year-row')).toHaveCount(0);
    await page.getByTestId('year-previous').click();
    await expect(page.getByTestId('year-label')).toHaveText(String(YEAR - 2));
    await expect(page.getByTestId('year-row')).toHaveCount(12);
    await expect(page.getByTestId('year-previous')).toBeDisabled();
    for (const year of [YEAR - 1, YEAR, YEAR + 1]) {
      await page.getByTestId('year-next').click();
      await expect(page.getByTestId('year-label')).toHaveText(String(year));
    }
    await expect(page.getByTestId('year-next')).toBeDisabled();
  });

  await test.step('Krok 15: deleting Prąd asks with an error-coloured action and focus on Anuluj, Anuluj keeps it and Usuń removes it', async () => {
    await openMonth(page, MONTH);
    await expenseDefinition(page, POWER).getByRole('button', { name: 'Usuń', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('button', { name: 'Anuluj' })).toBeFocused();
    await expect(dialog.getByRole('button', { name: 'Usuń', exact: true })).toHaveClass(
      /confirm-dialog__confirm--destructive/,
    );
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(expenseDefinition(page, POWER)).toHaveCount(1);
    await expenseDefinition(page, POWER).getByRole('button', { name: 'Usuń', exact: true }).click();
    await confirmDelete(page);
    await expect(expenseDefinition(page, POWER)).toHaveCount(0);
  });

  await test.step('Krok 16: the payment reminder field stays under the list, keeps its value after a reload and the dashboard follows the threshold for a yearly expense due in 5 days', async () => {
    await openMonth(page, MONTH);
    await addExpense(page, {
      name: INSURANCE,
      amount: '1200',
      start: GIFT_DATE,
      interval: '12',
      day: Number(GIFT_DATE.slice(8)),
    });
    const input = page.getByTestId('payment-lead-days-input');
    await input.fill('2');
    await input.press('Tab');
    await expectProjected(
      () => page.reload(),
      async () => {
        await expect(input).toHaveValue('2', { timeout: 2_000 });
      },
    );
    await page.goto('/dashboard');
    await expect(todoItem(page, REFUND)).toHaveCount(1);
    await expect(todoItem(page, INSURANCE)).toHaveCount(0);
    await openMonth(page, MONTH);
    await input.fill('7');
    await input.press('Tab');
    await expectProjected(
      () => page.reload(),
      async () => {
        await expect(input).toHaveValue('7', { timeout: 2_000 });
      },
    );
    await expectProjected(
      async () => {
        await page.goto('/dashboard');
        await expect(todoItem(page, REFUND)).toHaveCount(1);
      },
      async () => {
        await expect(todoItem(page, INSURANCE)).toHaveCount(1, { timeout: 2_000 });
      },
    );
  });

  await test.step('Krok 17: at 390 px there is no Lista / Kalendarz switch, a summary strip leads, one column follows in order, targets are at least 44 px and nothing scrolls sideways', async () => {
    await page.setViewportSize(PHONE);
    await openMonth(page, MONTH);
    await expect(page.getByTestId('layout-list')).toHaveCount(0);
    await expect(page.getByTestId('layout-calendar')).toHaveCount(0);
    const strip = page.locator('app-summary-strip');
    await expect(strip).toContainText('Wpływy');
    await expect(strip).toContainText('Oczekuje');
    await expect(strip).toContainText('Wolne środki');
    const order = [
      strip,
      page.getByTestId('overdue-section'),
      page.getByTestId('day-group').first(),
      page.getByTestId('free-funds'),
      page.getByTestId('split-section'),
      page.getByRole('heading', { name: 'Wpływy cykliczne', exact: true }),
    ];
    await expect(async () => {
      const tops: number[] = [];
      for (const section of order) {
        const box = await section.boundingBox();
        expect(box).not.toBeNull();
        tops.push(box?.y ?? 0);
      }
      expect(tops).toEqual([...tops].sort((a, b) => a - b));
    }).toPass({ timeout: 10_000 });
    for (const target of [
      page.getByTestId('add-income'),
      page.getByTestId('add-expense'),
      page.getByTestId('settle-overdue').first(),
    ]) {
      const box = await target.boundingBox();
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
      expect(box?.width ?? 0).toBeGreaterThanOrEqual(44);
    }
    expect(await overflow(page)).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 18: the year on a phone shows Miesiąc, Wydatki and Wolne środki prognoza and the other columns after expanding a row', async () => {
    await page.goto(`/planning?tab=year&year=${YEAR}`);
    await expect(page.getByTestId('year-row')).toHaveCount(12);
    await expect(page.getByTestId('year-chart-column')).toHaveCount(0);
    await expect(page.getByRole('columnheader', { name: 'Miesiąc', exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Wydatki', exact: true })).toBeVisible();
    await expect(
      page.getByRole('columnheader', { name: 'Wolne środki faktyczne', exact: true }),
    ).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Wpływy plan', exact: true })).toHaveCount(
      0,
    );
    await page.getByTestId('year-expand').first().click();
    await expect(page.getByTestId('year-detail').first()).toContainText('Wpływy plan');
    await expect(page.getByTestId('year-detail').first()).toContainText('Wolne środki plan');
  });

  await test.step('Krok 19: at 1000 px the right column falls under the list unpinned and a dialog on a phone fills the width', async () => {
    await page.setViewportSize(MID);
    await openMonth(page, MONTH);
    const aside = page.locator('.planning-layout__aside');
    await expect(aside).toHaveCSS('position', 'static');
    const list = await page.locator('.planning-layout__main').boundingBox();
    const asideBox = await aside.boundingBox();
    expect(asideBox?.y ?? 0).toBeGreaterThanOrEqual((list?.y ?? 0) + (list?.height ?? 0) - 1);
    await page.setViewportSize(PHONE);
    await openMonth(page, MONTH);
    await page.getByTestId('add-income').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Dodaj wpływ cykliczny' })).toBeVisible();
    await expect(async () => {
      const box = await dialog.boundingBox();
      expect(box?.width ?? 0).toBeGreaterThanOrEqual(PHONE.width - 24);
    }).toPass({ timeout: 10_000 });
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 20: in the dark theme statuses, group colours and the deficit stay readable with an icon or a word', async () => {
    await page.setViewportSize(WIDE);
    await openMonth(page, MONTH);
    const theme = page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
    await theme.click();
    await theme.click();
    await expect(theme).toHaveAccessibleName(/^Motyw: ciemny/);
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    const salary = occurrences(page, SALARY);
    await expect(status(salary)).toHaveText('pominięte');
    await expect(salary.getByTestId('occurrence-status').locator('mat-icon')).toBeVisible();
    await expect(
      page.getByTestId('free-funds-plan').getByTestId('free-funds-deficit'),
    ).toContainText('deficyt');
    await expect(page.getByTestId('no-date-block').locator('.day-list__dot').first()).toBeVisible();
  });

  await test.step('Krok 21: a failing month request shows a section error with Ponów while the header and tabs stay, and Ponów brings the month back', async () => {
    await page.route('**/api/portfolio/planning/months/**', (route) => route.abort());
    await page.reload();
    await expect(page.getByRole('button', { name: 'Ponów' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Planowanie', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Miesiąc', exact: true })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Rok', exact: true })).toBeVisible();
    await page.unroute('**/api/portfolio/planning/months/**');
    await page.getByRole('button', { name: 'Ponów' }).click();
    await expect(page.getByTestId('month-balance')).toBeVisible();
    await expect(occurrences(page, SALARY)).toHaveCount(1);
  });
});
