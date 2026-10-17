import { type Locator, type Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test, warsawDate } from './fixtures';
import {
  MBANK,
  addExpense,
  addIncome,
  choose,
  confirmDelete,
  dateInText,
  day,
  deleteExpense,
  editExpenseFrom,
  expectCost,
  expectFreeFunds,
  expensePayments,
  exactly,
  lastDay,
  monthInList,
  openMonth,
  pln,
  settle,
  shiftMonth,
  type IncomeInput,
} from './planning';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const WRAPPER_IKE = 1;
const WRAPPER_IKZE = 2;
const VARIANT_CURRENT = 0;

const SAVINGS_ACCOUNT = 'Konto oszczędnościowe';
const IKE_ACCOUNT = 'IKE Dominik';
const IKZE_ACCOUNT = 'IKZE Dominik';
const DOMINIK = 'Wypłata Dominik';
const WIFE = 'Wypłata żona';
const LIFE = 'Życie';
const CAR = 'OC Toyota';
const DOWN_PAYMENT = 'Wkład własny';
const CUSHION = 'Poduszka';
const HOLIDAY = 'Wakacje';
const INVESTMENTS = 'Inwestycje';
const IKE = 'IKE Dominik';
const IKZE = 'IKZE Dominik';

const CURRENT = warsawDate().slice(0, 7);
const DECEMBER_2026 = '2026-12';
const JANUARY_2027 = '2027-01';

function m(offset: number): string {
  return shiftMonth(CURRENT, offset);
}

function exactPln(amount: string): RegExp {
  return new RegExp(`^\\s*${pln(amount).source}\\s*$`);
}

function bucket(page: Page, name: string): Locator {
  return page.getByTestId('allocation-bucket-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

async function expectBucket(page: Page, name: string, amount: string): Promise<void> {
  await expect(bucket(page, name).locator('.planning-allocation__amount')).toHaveText(
    exactPln(amount),
  );
}

async function expectToInvest(page: Page, amount: string): Promise<void> {
  await expect(page.getByTestId('allocation-to-invest').locator('dd')).toHaveText(exactPln(amount));
}

async function expectAllocationForecast(page: Page, amount: string): Promise<void> {
  await expect(page.getByTestId('allocation-forecast').locator('dd')).toHaveText(exactPln(amount));
}

async function expectCushionBalance(
  page: Page,
  balance: { start: string; end: string; target: string },
): Promise<void> {
  await expect(bucket(page, CUSHION).getByTestId('allocation-cushion-balance')).toContainText(
    new RegExp(
      `Saldo\\s+${pln(balance.start).source}\\s+→\\s+${pln(balance.end).source}\\s+/\\s+cel\\s+${pln(balance.target).source}`,
    ),
  );
}

async function expectCounter(
  page: Page,
  name: string,
  counter: { year: string; contributed: string; limit: string },
): Promise<void> {
  await expect(bucket(page, name).getByTestId('allocation-limit')).toContainText(
    new RegExp(
      `Wpłacono w ${counter.year}:\\s+${pln(counter.contributed).source}\\s+/\\s+${pln(counter.limit).source}`,
    ),
  );
}

async function openPlanDialog(page: Page, effectiveFrom?: string): Promise<Locator> {
  await page.getByTestId('edit-allocation').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj podział' })).toBeVisible();
  if (effectiveFrom) {
    await dialog.getByTestId('effective-from').fill(effectiveFrom);
  }
  return dialog;
}

async function savePlan(dialog: Locator): Promise<void> {
  await dialog.getByTestId('save').click();
  await expect(dialog).toBeHidden();
}

async function cancelPlan(dialog: Locator): Promise<void> {
  await dialog.getByRole('button', { name: 'Anuluj' }).click();
  await expect(dialog).toBeHidden();
}

async function rowByName(dialog: Locator, name: string): Promise<Locator> {
  const names = await dialog
    .getByTestId('row-name')
    .evaluateAll((inputs) => inputs.map((input) => (input as HTMLInputElement).value));
  const index = names.indexOf(name);
  expect(index, `row ${name} in ${names.join(', ')}`).toBeGreaterThanOrEqual(0);
  return dialog.getByTestId('plan-row').nth(index);
}

async function rowNames(dialog: Locator): Promise<string[]> {
  return dialog
    .getByTestId('row-name')
    .evaluateAll((inputs) => inputs.map((input) => (input as HTMLInputElement).value));
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

async function setBalance(page: Page, name: string, month: string, amount: string): Promise<void> {
  await bucket(page, name).getByTestId('set-bucket-balance').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByTestId('bucket-balance-month').fill(month);
  await dialog.getByTestId('bucket-balance-amount').fill(amount);
  await dialog.getByTestId('bucket-balance-save').click();
  await expect(dialog).toBeHidden();
}

async function expectReserveRow(
  page: Page,
  reserve: { monthly: string; balance: string; account?: string },
): Promise<void> {
  const row = page.getByTestId('reserve-row');
  await expect(row).toContainText(new RegExp(`odkładasz\\s+${pln(reserve.monthly).source}`));
  await expect(row).toContainText(new RegExp(`saldo\\s+${pln(reserve.balance).source}`));
  if (reserve.account) {
    await expect(row).toContainText(`na koncie ${reserve.account}`);
  } else {
    await expect(row).not.toContainText('na koncie');
  }
}

async function expectBucketsSumToForecast(page: Page): Promise<void> {
  const texts = await page
    .getByTestId('allocation-bucket-row')
    .locator('.planning-allocation__amount')
    .allInnerTexts();
  const forecast = await page.getByTestId('allocation-forecast').locator('dd').innerText();
  expect(texts.map(toGrosze).reduce((sum, value) => sum + value, 0)).toBe(toGrosze(forecast));
}

function toGrosze(text: string): number {
  const digits = text.replace(/[^\d,-]/g, '').replace(',', '.');
  return Math.round(Number(digits) * 100);
}

async function expectStateOfNextMonth(page: Page): Promise<void> {
  await expectAllocationForecast(page, '12 300,00');
  await expectBucket(page, CUSHION, '0,00');
  await expect(bucket(page, CUSHION).getByTestId('allocation-cushion-reached')).toBeVisible();
  await expectBucket(page, HOLIDAY, '500,00');
  await expectBucket(page, IKE, '0,00');
  await expect(bucket(page, IKE).getByTestId('allocation-limit-reached')).toBeVisible();
  await expectBucket(page, IKZE, '0,00');
  await expect(bucket(page, IKZE).getByTestId('allocation-limit-reached')).toBeVisible();
  await expectBucket(page, INVESTMENTS, '11 800,00');
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
    name: '800+',
    amount: '800',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 10,
  },
];

test('allocation buckets: split the forecast into cushion, fixed, percent and IKE/IKZE buckets with limits, corrections and the reserve row', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(600_000);

  await test.step('Przygotowanie 4: accounts mBank ROR, Konto oszczędnościowe, IKE Dominik and IKZE Dominik', async () => {
    const token = await logInThroughApi(request, user);
    const accounts = [
      { name: MBANK, type: ACCOUNT_TYPE_BANK, wrapper: WRAPPER_REGULAR },
      { name: SAVINGS_ACCOUNT, type: ACCOUNT_TYPE_BANK, wrapper: WRAPPER_REGULAR },
      { name: IKE_ACCOUNT, type: ACCOUNT_TYPE_BROKERAGE, wrapper: WRAPPER_IKE },
      { name: IKZE_ACCOUNT, type: ACCOUNT_TYPE_BROKERAGE, wrapper: WRAPPER_IKZE },
    ];
    for (const account of accounts) {
      const created = await request.post('/api/portfolio/accounts', {
        headers: { Authorization: `Bearer ${token}` },
        data:
          account.type === ACCOUNT_TYPE_BANK
            ? {
                ...account,
                currency: 'PLN',
                variant: VARIANT_CURRENT,
                annualInterestRatePercent: 0,
              }
            : account,
      });
      expect(created.ok()).toBeTruthy();
    }
    await page.goto('/login');
    await logInThroughUi(page, user);
  });

  await test.step('Przygotowanie 5: incomes and expenses from 2026-01-01, OC Toyota from the 15th of M+5', async () => {
    await openMonth(page, m(0));
    for (const income of INCOMES) {
      await addIncome(page, income);
    }
    await addExpense(page, {
      name: LIFE,
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
      name: CAR,
      amount: '1200',
      start: day(m(5), 15),
      interval: '12',
      day: 15,
    });
  });

  await test.step('Przygotowanie 6: settling Wypłata Dominik at 9 143,27 zł and Wypłata żona at 6 500 zł gives forecast 12 243,27 zł and cost 4 200 zł', async () => {
    await settle(page, DOMINIK, '9143.27');
    await settle(page, WIFE, '6500');
    await expectFreeFunds(page, '12 300,00', '12 243,27');
    await expectCost(page, '4 200,00');
  });

  await test.step('Krok 1: Podział of the current month has no buckets and the reserve row shows 200 zł, balance 200 zł, no account', async () => {
    await openMonth(page, m(0));
    await expect(page.getByTestId('allocation-section')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Podział', exact: true })).toBeVisible();
    await expect(page.getByText('Brak podziału obowiązującego w tym miesiącu.')).toBeVisible();
    await expect(page.getByTestId('allocation-bucket-row')).toHaveCount(0);
    await expectReserveRow(page, { monthly: '200,00', balance: '200,00' });
  });

  await test.step('Krok 2: Inwestycje 100% as the remainder gives Inwestycje 12 243,27 zł and to invest 12 243,27 zł', async () => {
    const dialog = await openPlanDialog(page);
    await addBucket(page, dialog, {
      name: INVESTMENTS,
      share: '100',
      remainder: true,
    });
    await savePlan(dialog);
    await expectBucket(page, INVESTMENTS, '12 243,27');
    await expectToInvest(page, '12 243,27');
  });

  await test.step('Krok 3: Wakacje as a fixed 500 zł Savings bucket on Konto oszczędnościowe gives Wakacje 500 zł, Inwestycje 11 743,27 zł and to invest 11 743,27 zł', async () => {
    const dialog = await openPlanDialog(page);
    await addBucket(page, dialog, {
      name: HOLIDAY,
      kind: 'Oszczędności',
      share: '500',
      account: SAVINGS_ACCOUNT,
      fixed: true,
    });
    await savePlan(dialog);
    await expect(page.getByTestId('allocation-group-fixed')).toBeVisible();
    await expectBucket(page, HOLIDAY, '500,00');
    await expectBucket(page, INVESTMENTS, '11 743,27');
    await expectToInvest(page, '11 743,27');
  });

  await test.step('Krok 4: adding Akcje 10% beside Inwestycje 100% blocks the save with the over-100 message', async () => {
    const dialog = await openPlanDialog(page);
    await addBucket(page, dialog, { name: 'Akcje', kind: INVESTMENTS, share: '10' });
    await expect(dialog.getByTestId('percent-sum')).toContainText('110');
    await expect(dialog.getByRole('alert')).toContainText('Suma procentów przekracza 100.');
    await expect(dialog.getByTestId('save')).toBeDisabled();
    await cancelPlan(dialog);
    await expect(bucket(page, 'Akcje')).toHaveCount(0);
  });

  await test.step('Krok 5: a 6-month cushion with a 20 000 zł starting balance takes 5 200 zł first, balance 20 000 → 25 200 / target 25 200 zł, Wakacje 500 zł, Inwestycje 6 543,27 zł', async () => {
    const dialog = await openPlanDialog(page);
    await dialog.getByTestId('add-cushion').click();
    const row = dialog.getByTestId('plan-row').first();
    await row.getByTestId('row-name').fill(CUSHION);
    await row.getByTestId('row-target-months').fill('6');
    await row.getByTestId('row-starting-balance').fill('20000');
    await savePlan(dialog);
    await expect(page.getByTestId('allocation-group-cushion')).toBeVisible();
    await expectBucket(page, CUSHION, '5 200,00');
    await expectCushionBalance(page, { start: '20 000,00', end: '25 200,00', target: '25 200,00' });
    await expectBucket(page, HOLIDAY, '500,00');
    await expectBucket(page, INVESTMENTS, '6 543,27');
  });

  const planDialog = { current: undefined as Locator | undefined };

  await test.step('Krok 6: IKE Dominik 50% (26 000 zł paid), IKZE Dominik 20% (9 000 zł paid), Inwestycje 30% and the order IKE, IKZE, Inwestycje; the IKE account list offers only IKE Dominik', async () => {
    const dialog = await openPlanDialog(page);
    planDialog.current = dialog;
    await addBucket(page, dialog, {
      name: IKE,
      kind: 'IKE',
      share: '50',
      contributed: '26000',
    });
    const ikeAccount = (await rowByName(dialog, IKE)).getByRole('combobox', {
      name: 'Konto',
      exact: true,
    });
    await ikeAccount.click();
    await expect(page.getByRole('option')).toHaveText(['Bez konta', IKE_ACCOUNT]);
    await page.getByRole('option', { name: IKE_ACCOUNT, exact: true }).click();

    await addBucket(page, dialog, {
      name: IKZE,
      kind: 'IKZE',
      share: '20',
      contributed: '9000',
      account: IKZE_ACCOUNT,
    });
    await (await rowByName(dialog, INVESTMENTS)).getByTestId('row-share').fill('30');
    await (await rowByName(dialog, IKE)).getByTestId('move-up').click();
    await (await rowByName(dialog, IKZE)).getByTestId('move-up').click();
    expect(await rowNames(dialog)).toEqual([CUSHION, HOLIDAY, IKE, IKZE, INVESTMENTS]);
    await expect(dialog.getByTestId('percent-sum')).toContainText('100');
  });

  await test.step('Krok 7: saving gives IKE 2 260 zł (28 260 / 28 260 zł, limit reached), IKZE 2 304 zł (11 304 / 11 304 zł), Inwestycje 1 979,27 zł, buckets summing to 12 243,27 zł and to invest 6 543,27 zł', async () => {
    await savePlan(planDialog.current!);
    await expectBucket(page, IKE, '2 260,00');
    await expectCounter(page, IKE, { year: '2026', contributed: '28 260,00', limit: '28 260,00' });
    await expect(bucket(page, IKE).getByTestId('allocation-limit-reached')).toBeVisible();
    await expectBucket(page, IKZE, '2 304,00');
    await expectCounter(page, IKZE, { year: '2026', contributed: '11 304,00', limit: '11 304,00' });
    await expect(bucket(page, IKZE).getByTestId('allocation-limit-reached')).toBeVisible();
    await expectBucket(page, INVESTMENTS, '1 979,27');
    await expectBucketsSumToForecast(page);
    await expectToInvest(page, '6 543,27');
  });

  await test.step('Krok 8: M+1 has forecast 12 300 zł, cushion 0 zł with the target reached, Wakacje 500 zł, IKE and IKZE 0 zł at the limit, Inwestycje 11 800 zł', async () => {
    await openMonth(page, m(1));
    await expectStateOfNextMonth(page);
  });

  await test.step('Krok 9: January 2027 restarts the IKE and IKZE counters with the estimated-limit warning: IKE 5 900 zł, IKZE 2 360 zł, Inwestycje 3 540 zł, Wakacje 500 zł, cushion 0 zł', async () => {
    await openMonth(page, JANUARY_2027);
    await expect(page.getByTestId('allocation-limit-estimated')).toContainText('2027');
    await expectBucket(page, IKE, '5 900,00');
    await expectCounter(page, IKE, { year: '2027', contributed: '5 900,00', limit: '28 260,00' });
    await expectBucket(page, IKZE, '2 360,00');
    await expectCounter(page, IKZE, { year: '2027', contributed: '2 360,00', limit: '11 304,00' });
    await expectBucket(page, INVESTMENTS, '3 540,00');
    await expectBucket(page, HOLIDAY, '500,00');
    await expectBucket(page, CUSHION, '0,00');
  });

  await test.step('Krok 10: Wakacje 800 zł from January 2027 keeps December 2026 at 500 zł, gives January 2027 800 zł and shows the scheduled change in M', async () => {
    await openMonth(page, m(0));
    const dialog = await openPlanDialog(page, JANUARY_2027);
    await (await rowByName(dialog, HOLIDAY)).getByTestId('row-share').fill('800');
    await savePlan(dialog);
    await expect(page.getByTestId('allocation-next-change')).toContainText('01.2027');
    await openMonth(page, DECEMBER_2026);
    await expectBucket(page, HOLIDAY, '500,00');
    await openMonth(page, JANUARY_2027);
    await expectBucket(page, HOLIDAY, '800,00');
  });

  await test.step('Krok 11: deleting the scheduled change returns January 2027 to Wakacje 500 zł and removes the change', async () => {
    await openMonth(page, m(0));
    await page.getByTestId('delete-allocation-change').click();
    await confirmDelete(page);
    await expect(page.getByTestId('allocation-next-change')).toHaveCount(0);
    await openMonth(page, JANUARY_2027);
    await expectBucket(page, HOLIDAY, '500,00');
    await expect(page.getByTestId('allocation-next-change')).toHaveCount(0);
  });

  await test.step('Krok 12: Życie at 3 300 zł from January 2027 gives cost 4 500 zł, cushion target 27 000 zł and the cushion taking 1 800 zł', async () => {
    await openMonth(page, m(0));
    await editExpenseFrom(page, LIFE, JANUARY_2027, { amount: '3300' });
    await openMonth(page, JANUARY_2027);
    await expectCost(page, '4 500,00');
    await expectCushionBalance(page, { start: '25 200,00', end: '27 000,00', target: '27 000,00' });
    await expectBucket(page, CUSHION, '1 800,00');
  });

  await test.step('Krok 13: Wkład własny 17 000 zł in M+1 zeroes every bucket with a deficit of 4 700 zł, M+2 starts the cushion at 25 200 zł, then the expense is deleted', async () => {
    await openMonth(page, m(1));
    await addExpense(page, {
      name: DOWN_PAYMENT,
      amount: '17000',
      start: day(m(1), 5),
      end: lastDay(m(1)),
      interval: '1',
      day: 5,
    });
    await expect(
      page.getByTestId('allocation-bucket-row').locator('.planning-allocation__amount'),
    ).toHaveText(Array<RegExp>(5).fill(exactPln('0,00')));
    await expect(page.getByTestId('allocation-deficit')).toContainText(pln('4 700,00'));
    await openMonth(page, m(2));
    await expectCushionBalance(page, { start: '25 200,00', end: '25 200,00', target: '25 200,00' });
    await openMonth(page, m(1));
    await deleteExpense(page, DOWN_PAYMENT);
    await expect(page.getByTestId('allocation-deficit')).toHaveCount(0);
  });

  await test.step('Krok 14: setting the cushion balance from M+2 to 15 000 zł gives the cushion 10 200 zł, Wakacje 500 zł, IKE and IKZE 0 zł, Inwestycje 1 600 zł and leaves M+1 unchanged', async () => {
    await openMonth(page, m(2));
    await setBalance(page, CUSHION, m(2), '15000');
    await expectBucket(page, CUSHION, '10 200,00');
    await expectCushionBalance(page, { start: '15 000,00', end: '25 200,00', target: '25 200,00' });
    await expectBucket(page, HOLIDAY, '500,00');
    await expectBucket(page, IKE, '0,00');
    await expectBucket(page, IKZE, '0,00');
    await expectBucket(page, INVESTMENTS, '1 600,00');
    await openMonth(page, m(1));
    await expectStateOfNextMonth(page);
  });

  await test.step('Krok 15: setting the IKE counter from M+1 to 20 000 zł gives IKE 5 900 zł (25 900 / 28 260 zł), IKZE 0 zł and Inwestycje 5 900 zł', async () => {
    await setBalance(page, IKE, m(1), '20000');
    await expectBucket(page, IKE, '5 900,00');
    await expectCounter(page, IKE, { year: '2026', contributed: '25 900,00', limit: '28 260,00' });
    await expectBucket(page, IKZE, '0,00');
    await expectBucket(page, INVESTMENTS, '5 900,00');
  });

  await test.step('Krok 16: deleting the IKE correction from M+1 returns M+1 to the state of Krok 8', async () => {
    const correction = bucket(page, IKE)
      .getByTestId('bucket-adjustment')
      .filter({ hasText: new RegExp(`od ${monthInList(m(1))}:`) });
    await correction.getByTestId('delete-bucket-adjustment').click();
    await confirmDelete(page);
    await expect(correction).toHaveCount(0);
    await expectStateOfNextMonth(page);
  });

  await test.step('Krok 17: the reserve account Konto oszczędnościowe from M shows in the reserve row of M', async () => {
    await openMonth(page, m(0));
    const dialog = await openPlanDialog(page);
    await choose(page, dialog.getByTestId('reserve-account'), SAVINGS_ACCOUNT);
    await savePlan(dialog);
    await expectReserveRow(page, {
      monthly: '200,00',
      balance: '200,00',
      account: SAVINGS_ACCOUNT,
    });
  });

  await test.step('Krok 18: M+2 shows the reserve 200 zł per month with balance 600 zł', async () => {
    await openMonth(page, m(2));
    await expectReserveRow(page, {
      monthly: '200,00',
      balance: '600,00',
      account: SAVINGS_ACCOUNT,
    });
  });

  await test.step('Krok 19: M+5 lists the OC Toyota payment of 1 200 zł and the reserve 200 zł per month with balance 0 zł', async () => {
    await openMonth(page, m(5));
    const payment = expensePayments(page, CAR);
    await expect(payment).toContainText(dateInText(day(m(5), 15)));
    await expect(payment).toContainText(pln('1 200,00'));
    await expectReserveRow(page, {
      monthly: '200,00',
      balance: '0,00',
      account: SAVINGS_ACCOUNT,
    });
  });

  await test.step('Krok 20: removing every bucket from M+6 leaves Podział empty from M+6 and M+5 unchanged', async () => {
    await openMonth(page, m(6));
    const dialog = await openPlanDialog(page);
    const count = await dialog.getByTestId('plan-row').count();
    for (let removed = 0; removed < count; removed++) {
      await dialog.getByTestId('remove-row').first().click();
    }
    await expect(dialog.getByTestId('plan-row')).toHaveCount(0);
    await savePlan(dialog);
    await expect(page.getByTestId('allocation-bucket-row')).toHaveCount(0);
    await expect(page.getByText('Brak podziału obowiązującego w tym miesiącu.')).toBeVisible();
    await openMonth(page, m(5));
    await expect(bucket(page, INVESTMENTS)).toHaveCount(1);
    await expect(page.getByTestId('allocation-bucket-row')).toHaveCount(5);
  });
});
