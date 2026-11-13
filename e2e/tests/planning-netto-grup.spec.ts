import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test } from './fixtures';
import {
  addExpense,
  addIncome,
  dateInText,
  exactly,
  money,
  occurrences,
  openMonth,
  pln,
  status,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TYPE_DEPOSIT = 2;
const TYPE_WITHDRAW = 3;

const NOVEMBER = '2026-11';
const DECEMBER = '2026-12';

const ERSTE = 'Erste Platinum';
const IKE_CASH = 'IKE gotówka';
const IKE_SAVINGS = 'IKE oszczędnościowe';
const SAVINGS = 'Konto oszczędnościowe';
const RETIREMENT = 'Emerytura';
const CUSHION = 'Poduszka';
const SALARY = 'Dominik Wypłata';
const BENEFIT = '800+';
const RENT = 'Czynsz';

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

function progress(net: string, plan: string): RegExp {
  return new RegExp(`^\\s*${pln(net).source}\\s/\\s${pln(plan).source}\\s*$`);
}

function amountOf(text: string): number {
  return Number(
    text
      .replace(/[^\d,\-−]/g, '')
      .replace('−', '-')
      .replace(',', '.'),
  );
}

async function post<T>(
  request: APIRequestContext,
  token: string,
  url: string,
  data: unknown,
): Promise<T> {
  const response = await request.post(url, { headers: authorization(token), data });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as T;
}

async function bankAccount(
  request: APIRequestContext,
  token: string,
  name: string,
  groupId?: string,
): Promise<Created> {
  return post<Created>(request, token, '/api/portfolio/accounts', {
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
  account: Created,
  transaction: { type: number; quantity: number; date: string },
): Promise<void> {
  await post(request, token, `/api/portfolio/assets/${account.cashAssetId}/transactions`, {
    ...transaction,
    unitPrice: 1,
  });
}

async function transfer(
  request: APIRequestContext,
  token: string,
  from: Created,
  to: Created,
  amount: number,
  date: string,
): Promise<void> {
  await post(request, token, '/api/portfolio/transfers', {
    sourceAssetId: from.cashAssetId,
    targetAssetId: to.cashAssetId,
    amount,
    date,
  });
}

async function settleOn(page: Page, name: string, amount: string, date: string): Promise<void> {
  const row = occurrences(page, name);
  await row.getByTestId('settle-occurrence').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' }).fill(amount);
  await dialog.getByRole('textbox', { name: 'Faktyczna data' }).fill(date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
  await expect(status(row)).toHaveText('rozliczone');
}

function contributionRow(page: Page, name: string): Locator {
  return page
    .getByTestId('contribution-row')
    .filter({ has: page.getByTestId('row-name').filter({ hasText: exactly(name) }) });
}

async function expectGroupRow(
  page: Page,
  name: string,
  amount: RegExp,
  chip: string,
): Promise<Locator> {
  const row = contributionRow(page, name);
  await expect(row).toHaveCount(1);
  await expect(row.locator('.day-list__amount')).toHaveText(amount);
  await expect(row.getByTestId('contribution-status')).toContainText(chip);
  return row;
}

async function cascadeFigure(page: Page, testId: string): Promise<number> {
  return amountOf(await page.getByTestId(testId).locator('.cascade__amount').innerText());
}

test('planowanie: wpłaty do grup netto, rozliczone wiersze, kalendarz i widok roku', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  await page.goto('/login');
  await logInThroughUi(page, user);
  let erste: Created = { id: '', cashAssetId: '' };
  let ikeCash: Created = { id: '', cashAssetId: '' };
  let savings: Created = { id: '', cashAssetId: '' };
  let ikeSavings: Created = { id: '', cashAssetId: '' };
  let retirementId = '';
  let cushionId = '';
  let freeAfterStep14 = 0;

  await test.step('Krok 1: konto Erste Platinum z wpłatą 20 000 zł ma saldo 20 000 zł', async () => {
    erste = await bankAccount(request, token, ERSTE);
    await cashTransaction(request, token, erste, {
      type: TYPE_DEPOSIT,
      quantity: 20000,
      date: `${NOVEMBER}-01`,
    });
    const listed = await request.get('/api/portfolio/accounts', { headers: authorization(token) });
    const accounts = (await listed.json()) as { name: string; cashBalance: number }[];
    expect(accounts.find((account) => account.name === ERSTE)?.cashBalance).toBe(20000);
  });

  await test.step('Krok 2: grupa Emerytura z kontem IKE gotówka ma plan wpłaty 2 000 zł z Erste Platinum', async () => {
    const group = await post<{ id: string }>(request, token, '/api/portfolio/asset-groups', {
      name: RETIREMENT,
      color: 'purple',
      contributions: [{ amountPln: 2000, accountId: erste.id }],
    });
    retirementId = group.id;
    ikeCash = await bankAccount(request, token, IKE_CASH, retirementId);
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    const card = page
      .getByTestId('group-card')
      .filter({ has: page.getByTestId('group-name').getByText(RETIREMENT, { exact: true }) });
    await expect(card.getByTestId('group-contribution')).toContainText(pln('2 000,00'));
  });

  await test.step('Krok 3: grupa Poduszka bez planu z kontem Konto oszczędnościowe i wpłatą 5 000 zł ma saldo 5 000 zł', async () => {
    const group = await post<{ id: string }>(request, token, '/api/portfolio/asset-groups', {
      name: CUSHION,
      color: 'blue',
      contributions: [],
    });
    cushionId = group.id;
    savings = await bankAccount(request, token, SAVINGS, cushionId);
    await cashTransaction(request, token, savings, {
      type: TYPE_DEPOSIT,
      quantity: 5000,
      date: '2026-10-20',
    });
    const listed = await request.get('/api/portfolio/accounts', { headers: authorization(token) });
    const accounts = (await listed.json()) as { name: string; cashBalance: number }[];
    expect(accounts.find((account) => account.name === SAVINGS)?.cashBalance).toBe(5000);
  });

  await test.step('Krok 4: wpływy Dominik Wypłata i 800+ oraz wydatek Czynsz są widoczne za listopad 2026', async () => {
    await openMonth(page, NOVEMBER);
    await addIncome(page, {
      name: SALARY,
      amount: '10000',
      account: ERSTE,
      start: `${NOVEMBER}-01`,
      interval: '1',
      day: 4,
    });
    await addIncome(page, {
      name: BENEFIT,
      amount: '1600',
      account: ERSTE,
      start: `${NOVEMBER}-01`,
      interval: '1',
      day: 8,
    });
    await addExpense(page, {
      name: RENT,
      amount: '1500',
      start: `${NOVEMBER}-01`,
      interval: '1',
      day: 20,
    });
    await openMonth(page, NOVEMBER);
    await expect(occurrences(page, SALARY)).toHaveCount(1);
    await expect(occurrences(page, BENEFIT)).toHaveCount(1);
  });

  await test.step('Krok 5: Dominik Wypłata rozliczona kwotą 10 882 zł pokazuje plan i różnicę w linii meta', async () => {
    await settleOn(page, SALARY, '10882', `${NOVEMBER}-04`);
    await openMonth(page, NOVEMBER);
    const row = occurrences(page, SALARY);
    const meta = row.locator('.day-list__meta');
    await expect(meta).toContainText(dateInText(`${NOVEMBER}-04`));
    await expect(meta.getByTestId('occurrence-plan')).toContainText(pln('10 000,00'));
    await expect(meta.getByTestId('occurrence-difference')).toContainText(
      new RegExp(`Różnica:\\s*${pln('882,00').source}`),
    );
    await expect(row.locator('.day-list__amount')).toContainText(pln('10 882,00'));
    await expect(row).not.toContainText('wpłata utworzona');
  });

  await test.step('Krok 6: 800+ rozliczone kwotą 1 600 zł pokazuje tylko datę, chip i kwotę bez różnicy', async () => {
    await settleOn(page, BENEFIT, '1600', `${NOVEMBER}-08`);
    await openMonth(page, NOVEMBER);
    const row = occurrences(page, BENEFIT);
    await expect(row.locator('.day-list__meta')).toContainText(dateInText(`${NOVEMBER}-08`));
    await expect(row.getByTestId('occurrence-plan')).toHaveCount(0);
    await expect(row.getByTestId('occurrence-difference')).toHaveCount(0);
    await expect(row).not.toContainText('Różnica');
    await expect(status(row)).toHaveText('rozliczone');
    await expect(row.getByTestId('occurrence-actual')).toHaveText(exactMoney('1 600,00'));
    await expect(row).not.toContainText('wpłata utworzona');
  });

  await test.step('Krok 7: przelew 4 000 zł na IKE gotówka daje Emerytura 4 000,00 / 2 000,00 zł Zrealizowana', async () => {
    await transfer(request, token, erste, ikeCash, 4000, `${NOVEMBER}-05`);
    await openMonth(page, NOVEMBER);
    const row = await expectGroupRow(
      page,
      RETIREMENT,
      progress('4 000,00', '2 000,00'),
      'Zrealizowana',
    );
    await expect(row).toContainText('Wpłata do grupy');
    await expect(row).not.toContainText('Różnica');
    await expect(page.getByTestId('no-date-block').getByTestId('contribution-row')).toContainText(
      RETIREMENT,
    );
  });

  await test.step('Krok 8: wypłata 233,14 zł z Konta oszczędnościowego daje wiersz Poduszka −233,14 zł Nierozliczona', async () => {
    await cashTransaction(request, token, savings, {
      type: TYPE_WITHDRAW,
      quantity: 233.14,
      date: `${NOVEMBER}-01`,
    });
    await openMonth(page, NOVEMBER);
    await expectGroupRow(page, CUSHION, exactMoney('-233,14'), 'Nierozliczona');
  });

  await test.step('Krok 9: przelew 3 200 zł na Konto oszczędnościowe daje Poduszka 2 966,86 zł Zrealizowana bez chipu Nieplanowany', async () => {
    await transfer(request, token, erste, savings, 3200, `${NOVEMBER}-04`);
    await openMonth(page, NOVEMBER);
    const row = await expectGroupRow(page, CUSHION, exactMoney('2 966,86'), 'Zrealizowana');
    await expect(row).not.toContainText('Nieplanowany');
  });

  await test.step('Krok 10: przelewy 2 000 zł na Erste Platinum i 3 000 zł z powrotem dają Poduszka 3 966,86 zł Zrealizowana', async () => {
    await transfer(request, token, savings, erste, 2000, `${NOVEMBER}-04`);
    await transfer(request, token, erste, savings, 3000, `${NOVEMBER}-04`);
    await openMonth(page, NOVEMBER);
    await expectGroupRow(page, CUSHION, exactMoney('3 966,86'), 'Zrealizowana');
  });

  await test.step('Krok 11: blok bez daty nie ma wierszy Zrealizowane i każda grupa ma jeden wiersz', async () => {
    await openMonth(page, NOVEMBER);
    const block = page.getByTestId('no-date-block');
    await expect(block.getByTestId('contribution-row')).toHaveCount(2);
    await expect(contributionRow(page, RETIREMENT)).toHaveCount(1);
    await expect(contributionRow(page, CUSHION)).toHaveCount(1);
    await expect(page.getByTestId('excess-row')).toHaveCount(0);
    await expect(block).not.toContainText('Zrealizowane');
  });

  await test.step('Krok 12: drugie konto IKE oszczędnościowe i przelew 1 000 zł wewnątrz grupy zostawiają Emerytura 4 000,00 / 2 000,00 zł', async () => {
    ikeSavings = await bankAccount(request, token, IKE_SAVINGS, retirementId);
    await transfer(request, token, ikeCash, ikeSavings, 1000, `${NOVEMBER}-06`);
    await openMonth(page, NOVEMBER);
    await expectGroupRow(page, RETIREMENT, progress('4 000,00', '2 000,00'), 'Zrealizowana');
  });

  await test.step('Krok 13: kaskada wolnych środków ma Wpłata do grupy 7 966,86 zł i Faktycznie pomniejszone o tę kwotę', async () => {
    await openMonth(page, NOVEMBER);
    await expect(
      page.getByTestId('cascade-actual-contributions').locator('.cascade__amount'),
    ).toHaveText(exactMoney('7 966,86'));
    await expect(async () => {
      const incomes = await cascadeFigure(page, 'cascade-actual-incomes');
      const contributions = await cascadeFigure(page, 'cascade-actual-contributions');
      const free = await cascadeFigure(page, 'free-funds-actual');
      expect(Math.round((incomes - contributions - free) * 100)).toBe(0);
    }).toPass();
  });

  await test.step('Krok 14: przelew 3 000 zł z IKE gotówka na Erste Platinum daje Emerytura 1 000,00 / 2 000,00 zł Częściowa i Wpłata do grupy 4 966,86 zł', async () => {
    await transfer(request, token, ikeCash, erste, 3000, `${NOVEMBER}-09`);
    await openMonth(page, NOVEMBER);
    await expectGroupRow(page, RETIREMENT, progress('1 000,00', '2 000,00'), 'Częściowa');
    await expect(
      page.getByTestId('cascade-actual-contributions').locator('.cascade__amount'),
    ).toHaveText(exactMoney('4 966,86'));
    freeAfterStep14 = await cascadeFigure(page, 'free-funds-actual');
  });

  await test.step('Krok 15: kalendarz listopada 2026 ma dni spoza miesiąca jako puste pola bez obramowania', async () => {
    await page.getByTestId('layout-calendar').click();
    await expect(page.getByTestId('planning-calendar')).toBeVisible();
    const blank = page.locator('[data-testid="calendar-cell"]:not([data-date])');
    expect(await blank.count()).toBeGreaterThan(0);
    for (const cell of await blank.all()) {
      await expect(cell).toHaveText('');
      await expect(cell).toHaveCSS('border-top-width', '0px');
      await expect(cell).toHaveCSS('border-bottom-width', '0px');
      await expect(cell).toHaveCSS('border-left-width', '0px');
      await expect(cell).toHaveCSS('border-right-width', '0px');
    }
  });

  await test.step('Krok 16: w kalendarzu rozliczona pozycja jest wyszarzona, a nierozliczona nie', async () => {
    const settled = page
      .locator('[data-testid="calendar-item"][data-status="settled"]')
      .filter({ hasText: SALARY });
    await expect(settled).toHaveCount(1);
    await expect(settled).toHaveCSS('opacity', '0.6');
    const pending = page
      .locator('[data-testid="calendar-item"]:not([data-status="settled"])')
      .filter({ hasText: RENT });
    await expect(pending).toHaveCount(1);
    await expect(pending).toHaveCSS('opacity', '1');
  });

  await test.step('Krok 17: widok roku 2026 nie ma wykresu, jest tylko tabela miesięcy', async () => {
    await page.goto('/planning?tab=year&year=2026');
    await expect(page.getByTestId('year-row')).toHaveCount(12);
    await expect(page.getByTestId('year-chart-column')).toHaveCount(0);
    await expect(page.locator('.year-chart__label')).toHaveCount(0);
  });

  await test.step('Krok 18: w tabeli roku Faktycznie wolnych środków listopada równa się wartości z widoku miesiąca', async () => {
    const row = page.getByTestId('year-row').nth(10);
    await expect(row.locator('td').nth(5)).toHaveText(
      new RegExp(`^\\s*${money(formatPln(freeAfterStep14), 'zł').source}\\s*$`),
    );
  });

  await test.step('Krok 19: grudzień 2026 pokazuje Emerytura z planem 2 000 zł bez chipu, a Poduszka nie ma wiersza', async () => {
    await openMonth(page, DECEMBER);
    const row = contributionRow(page, RETIREMENT);
    await expect(row).toHaveCount(1);
    await expect(row.locator('.day-list__amount')).toContainText(pln('2 000,00'));
    await expect(row.getByTestId('contribution-status')).toHaveCount(0);
    await expect(contributionRow(page, CUSHION)).toHaveCount(0);
  });
});

function formatPln(value: number): string {
  const [whole, cents] = Math.abs(value).toFixed(2).split('.') as [string, string];
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  return `${value < 0 ? '-' : ''}${grouped},${cents}`;
}
