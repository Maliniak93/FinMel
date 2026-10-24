import { type APIRequestContext, type Locator, type Page } from '@playwright/test';
import {
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
} from './fixtures';
import {
  MBANK,
  addExpense,
  addIncome,
  dateInText,
  lastDay,
  monthLabel,
  openMonth,
  pln,
  settle,
  shiftMonth,
  type IncomeInput,
} from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TERM_UNIT_MONTHS = 1;
const CAPITALIZATION_AT_MATURITY = 0;

const DEPOSIT = 'Lokata 3M';
const CHECKUP = 'Przegląd auta';

const D = warsawDate();
const M = D.slice(0, 7);
const Y = Number(M.slice(0, 4));
const MONTH_NUMBER = Number(M.slice(5, 7));
const NEXT = shiftMonth(M, 1);
const TOYOTA_MONTH = shiftMonth(M, 5);

const INCOMES: IncomeInput[] = [
  {
    name: 'Wypłata Dominik',
    amount: '9200',
    account: MBANK,
    start: '2026-01-01',
    interval: '1',
    day: 'last',
  },
  {
    name: 'Wypłata żona',
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

function addDays(iso: string, days: number): string {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

function easterSunday(year: number): string {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function isHoliday(iso: string): boolean {
  const year = Number(iso.slice(0, 4));
  const monthDay = iso.slice(5);
  if (
    [
      '01-01',
      '01-06',
      '05-01',
      '05-03',
      '08-15',
      '11-01',
      '11-11',
      '12-24',
      '12-25',
      '12-26',
    ].includes(monthDay)
  ) {
    return true;
  }
  const easter = easterSunday(year);
  return [0, 1, 49, 60].some((offset) => addDays(easter, offset) === iso);
}

function isBusinessDay(iso: string): boolean {
  const weekday = new Date(`${iso}T00:00:00Z`).getUTCDay();
  return weekday !== 0 && weekday !== 6 && !isHoliday(iso);
}

function previousBusinessDay(iso: string): string {
  let date = iso;
  while (!isBusinessDay(date)) {
    date = addDays(date, -1);
  }
  return date;
}

function daysOf(month: string): string[] {
  const last = Number(lastDay(month).slice(8));
  return Array.from(
    { length: last },
    (_, index) => `${month}-${String(index + 1).padStart(2, '0')}`,
  );
}

const FIRST = `${M}-01`;
const TEN = `${M}-10`;
const WIFE_DATE = previousBusinessDay(TEN);
const PAYDAY = previousBusinessDay(lastDay(M));
const DEPOSIT_DAY = Number(D.slice(8)) < 22 ? 22 : 28;
const DEPOSIT_END = `${M}-${DEPOSIT_DAY}`;
const CHECKUP_DATE = addDays(D, 5);

interface CalendarEvent {
  date: string;
  name: string;
}

function monthEvents(): CalendarEvent[] {
  const events: CalendarEvent[] = [
    { date: FIRST, name: 'Życie' },
    { date: WIFE_DATE, name: 'Wypłata żona' },
    { date: TEN, name: '800+' },
    { date: TEN, name: 'Rachunki' },
    { date: DEPOSIT_END, name: DEPOSIT },
    { date: PAYDAY, name: 'Wypłata Dominik' },
  ];
  if (CHECKUP_DATE.startsWith(`${M}-`)) {
    events.push({ date: CHECKUP_DATE, name: CHECKUP });
  }
  return events;
}

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function badge(page: Page): Locator {
  return page.getByTestId('todo-badge');
}

function monthCard(page: Page): Locator {
  return page.locator('app-month-card');
}

function todoItems(page: Page): Locator {
  return page.getByTestId('todo-card-item');
}

function exactPln(amount: string): RegExp {
  return new RegExp(`^\\s*${pln(amount).source}\\s*$`);
}

function plnText(cents: number): string {
  const sign = cents < 0 ? '-' : '';
  const absolute = Math.abs(cents);
  const whole = Math.floor(absolute / 100)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  return `${sign}${whole},${String(absolute % 100).padStart(2, '0')}`;
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function sameText(text: string): RegExp {
  return new RegExp(escapeRegExp(text.trim()).replace(/\s/g, '\\s'));
}

async function createBankAccount(
  request: APIRequestContext,
  token: string,
  name: string,
): Promise<{ id: string; cashAssetId: string }> {
  const response = await request.post('/api/portfolio/accounts', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()) as { id: string; cashAssetId: string };
}

async function createDepositThroughApi(
  request: APIRequestContext,
  token: string,
  sourceAccountId: string,
): Promise<void> {
  const created = await request.post('/api/portfolio/deposits', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name: DEPOSIT,
      sourceAccountId,
      principal: 10000,
      startDate: `${shiftMonth(M, -3)}-${DEPOSIT_DAY}`,
      termLength: 3,
      termUnit: TERM_UNIT_MONTHS,
      annualInterestRatePercent: 6,
      capitalization: CAPITALIZATION_AT_MATURITY,
    },
  });
  expect(created.ok()).toBeTruthy();
}

async function openDashboard(page: Page): Promise<void> {
  const todo = page.waitForResponse(
    (response) => response.url().includes('/api/portfolio/planning/todo') && response.ok(),
  );
  await page.goto('/dashboard');
  await todo;
  await expect(monthCard(page).getByRole('heading', { name: /· plan$/ })).toBeVisible();
}

async function expectCard(
  page: Page,
  figures: { plan: string; forecast: string; settled: string },
): Promise<void> {
  const card = monthCard(page);
  await expect(card.getByTestId('month-card-free')).toContainText(pln(figures.plan));
  await expect(card.getByTestId('month-card-forecast')).toContainText(pln(figures.forecast));
  await expect(card.getByTestId('month-card-settled')).toHaveText(
    `Wpływy załatwione: ${figures.settled}`,
  );
}

async function expectAllDone(page: Page): Promise<void> {
  await expect(page.getByTestId('todo-card-empty')).toHaveText('Wszystko załatwione');
  await expect(todoItems(page)).toHaveCount(0);
}

function yearRows(page: Page): Locator {
  return page.getByTestId('year-row');
}

async function openYearTab(page: Page): Promise<void> {
  await page.getByRole('tab', { name: 'Rok', exact: true }).click();
  await expect(page).toHaveURL(/tab=year/);
  await expect(page.getByTestId('year-label')).toHaveText(String(Y));
  await expect(yearRows(page)).toHaveCount(12);
}

interface YearValues {
  plan: string;
  actual: string | null;
  difference?: string;
  cost: string;
  freeFundsPlan: string;
  freeFundsForecast: string;
}

async function expectCells(cells: Locator, values: string | null, index: number): Promise<void> {
  await (values === null
    ? expect(cells.nth(index)).toHaveText(/^\s*$/)
    : expect(cells.nth(index)).toHaveText(exactPln(values)));
}

async function expectYearValues(cells: Locator, values: YearValues): Promise<void> {
  await expectCells(cells, values.plan, 0);
  await expectCells(cells, values.actual, 1);
  if (values.difference !== undefined) {
    await expectCells(cells, values.difference, 2);
  }
  await expectCells(cells, values.cost, 3);
  await expectCells(cells, values.freeFundsPlan, 4);
  await expectCells(cells, values.freeFundsForecast, 5);
}

function calendarCell(page: Page, date: string): Locator {
  return page.locator(`[data-testid="calendar-cell"][data-date="${date}"]`);
}

async function openCalendar(page: Page): Promise<void> {
  await page.getByTestId('layout-calendar').click();
  await expect(page).toHaveURL(/layout=calendar/);
  await expect(page.getByTestId('planning-calendar')).toBeVisible();
}

async function expectCalendarItem(
  page: Page,
  date: string,
  name: string,
  amount: string | RegExp,
  status?: string,
): Promise<void> {
  const item = calendarCell(page, date).getByTestId('calendar-item').filter({ hasText: name });
  await expect(item).toHaveCount(1);
  await expect(item).toContainText(typeof amount === 'string' ? pln(amount) : amount);
  if (status) {
    await expect(item).toContainText(status);
  }
}

async function dimmedDates(page: Page): Promise<string[]> {
  return page
    .getByTestId('calendar-cell')
    .evaluateAll((cells) =>
      cells
        .filter(
          (cell) => cell.getAttribute('data-date') && cell.getAttribute('data-dimmed') === 'true',
        )
        .map((cell) => cell.getAttribute('data-date') as string),
    );
}

async function expectDimmed(page: Page, month: string): Promise<void> {
  const expected = daysOf(month).filter((date) => !isBusinessDay(date));
  await expect.poll(() => dimmedDates(page)).toEqual(expected);
}

async function setLeadDays(page: Page, value: string): Promise<void> {
  const input = page.getByTestId('payment-lead-days-input');
  await input.fill(value);
  await input.press('Enter');
}

test('month dashboard: reminders, the counter, the year view and the day calendar', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(600_000);

  let depositAmount = '';

  await test.step('Przygotowanie 2: a new account', async () => {
    await logInThroughApi(request, user);
    await page.goto('/login');
    await logInThroughUi(page, user);
  });

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;

  await test.step('Przygotowanie 4: account mBank ROR with 20 000 zł', async () => {
    const bank = await createBankAccount(request, token, MBANK);
    await depositCashThroughApi(request, token, bank.cashAssetId, {
      quantity: 20000,
      date: `${shiftMonth(M, -4)}-01`,
    });
    await createDepositThroughApi(request, token, bank.id);
  });

  await test.step('Krok 1: the Ten miesiąc card shows only the link Zaplanuj miesiąc beside the Nadchodzące card with Lokata 3M', async () => {
    await openDashboard(page);
    const row = page.locator('.upcoming-card__row').filter({ hasText: DEPOSIT });
    await expect(row).toHaveCount(1);
    depositAmount = (await row.locator('.upcoming-card__amount').innerText()).trim();
    expect(depositAmount).not.toBe('');
    const card = monthCard(page);
    await expect(card.getByTestId('month-card-setup')).toHaveText('Zaplanuj miesiąc');
    await expect(card.getByTestId('month-card-free')).toHaveCount(0);
    await expect(card.getByTestId('month-card-settled')).toHaveCount(0);
  });

  await test.step('Krok 2: Zaplanuj miesiąc opens /planning on the current month in the list view', async () => {
    await monthCard(page).getByTestId('month-card-setup').click();
    await expect(page).toHaveURL(/\/planning$/);
    await expect(page.getByTestId('month-label')).toHaveText(monthLabel(M));
    await expect(page.getByTestId('layout-list')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('layout-calendar')).toHaveAttribute('aria-pressed', 'false');
  });

  await test.step('Krok 3: incomes and expenses from 2026-01-01 on mBank ROR, no counter by Planowanie', async () => {
    await openMonth(page, M);
    for (const income of INCOMES) {
      await addIncome(page, income);
    }
    await addExpense(page, {
      name: 'Życie',
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
      name: 'OC Toyota',
      amount: '1200',
      start: `${TOYOTA_MONTH}-15`,
      interval: '12',
      day: 15,
    });
    await expect(badge(page)).toHaveCount(0);
  });

  await test.step('Krok 4: the card shows free funds 12 300,00 zł / 12 300,00 zł, incomes 0 of 3 and Wszystko załatwione', async () => {
    await openDashboard(page);
    await expectCard(page, {
      plan: '12 300,00',
      forecast: '12 300,00',
      settled: '0 z 3',
    });
    await expectAllDone(page);
    await expect(badge(page)).toHaveCount(0);
  });

  await test.step('Krok 5: settling Wypłata Dominik at 9 143,27 zł and Wypłata żona at 6 500 zł gives 12 300,00 zł / 12 243,27 zł and 2 of 3', async () => {
    await openMonth(page, M);
    await settle(page, 'Wypłata Dominik', '9143.27');
    await settle(page, 'Wypłata żona', '6500');
    await openDashboard(page);
    await expectCard(page, {
      plan: '12 300,00',
      forecast: '12 243,27',
      settled: '2 z 3',
    });
  });

  await test.step('Krok 6: settling 800+ at 1 000 zł gives 12 300,00 zł / 12 443,27 zł, incomes 3 of 3, Wszystko załatwione and no counter', async () => {
    await openMonth(page, M);
    await settle(page, '800+', '1000');
    await openDashboard(page);
    await expectCard(page, {
      plan: '12 300,00',
      forecast: '12 443,27',
      settled: '3 z 3',
    });
    await expectAllDone(page);
    await expect(badge(page)).toHaveCount(0);
  });

  const before = MONTH_NUMBER - 1;
  const PLAN = 1_650_000;
  const COST_BEFORE = 400_000;
  const COST_FROM = 420_000;
  const DIFFERENCE = 14_327;
  const ACTUAL = PLAN + DIFFERENCE;
  const totalCost = before * COST_BEFORE + (13 - MONTH_NUMBER) * COST_FROM;
  const totalFreeFunds = 12 * PLAN - totalCost;

  await test.step('Krok 7: the Rok tab shows the year with the current month highlighted and its figures', async () => {
    await openMonth(page, M);
    await openYearTab(page);
    const rows = yearRows(page);
    for (let index = 0; index < 12; index++) {
      await expect(rows.nth(index).locator('th')).toContainText(
        monthLabel(`${Y}-${String(index + 1).padStart(2, '0')}`),
      );
    }
    await expect(page.locator('[data-testid="year-row"][aria-current="date"]')).toHaveCount(1);
    const current = rows.nth(MONTH_NUMBER - 1);
    await expect(current).toHaveAttribute('aria-current', 'date');
    await expectYearValues(current.locator('td'), {
      plan: plnText(PLAN),
      actual: plnText(ACTUAL),
      difference: plnText(DIFFERENCE),
      cost: plnText(COST_FROM),
      freeFundsPlan: plnText(PLAN - COST_FROM),
      freeFundsForecast: plnText(ACTUAL - COST_FROM),
    });
  });

  await test.step('Krok 8: months before and after the current one show plan, actual, cost, free funds as planned', async () => {
    const rows = yearRows(page);
    for (let index = 0; index < before; index++) {
      await expectYearValues(rows.nth(index).locator('td'), {
        plan: plnText(PLAN),
        actual: plnText(0),
        cost: plnText(COST_BEFORE),
        freeFundsPlan: plnText(PLAN - COST_BEFORE),
        freeFundsForecast: plnText(PLAN - COST_BEFORE),
      });
    }
    for (let index = MONTH_NUMBER; index < 12; index++) {
      await expectYearValues(rows.nth(index).locator('td'), {
        plan: plnText(PLAN),
        actual: null,
        cost: plnText(COST_FROM),
        freeFundsPlan: plnText(PLAN - COST_FROM),
        freeFundsForecast: plnText(PLAN - COST_FROM),
      });
    }
  });

  await test.step('Krok 9: the total row sums the year', async () => {
    const cells = page.getByTestId('year-total').locator('td');
    await expectYearValues(cells, {
      plan: plnText(12 * PLAN),
      actual: plnText(ACTUAL),
      difference: plnText(DIFFERENCE),
      cost: plnText(totalCost),
      freeFundsPlan: plnText(totalFreeFunds),
      freeFundsForecast: plnText(totalFreeFunds + DIFFERENCE),
    });
    await expect(cells).toHaveCount(6);
  });

  await test.step('Krok 10: the previous arrow is off in the first year, the next year has no actual and its next arrow is off', async () => {
    if (Y === 2026) {
      await expect(page.getByTestId('year-previous')).toBeDisabled();
    }
    await expect(page.getByTestId('year-next')).toBeEnabled();
    await page.getByTestId('year-next').click();
    await expect(page.getByTestId('year-label')).toHaveText(String(Y + 1));
    await expect(yearRows(page)).toHaveCount(12);
    for (let index = 0; index < 12; index++) {
      await expect(yearRows(page).nth(index).locator('td').nth(1)).toHaveText(/^\s*$/);
    }
    await expect(page.getByTestId('year-next')).toBeDisabled();
  });

  await test.step('Krok 11: back in the current year a click on the current month row opens its month view', async () => {
    await page.getByTestId('year-previous').click();
    await expect(page.getByTestId('year-label')).toHaveText(String(Y));
    await yearRows(page)
      .nth(MONTH_NUMBER - 1)
      .click();
    await expect(page).toHaveURL(new RegExp(`/planning\\?month=${M}$`));
    await expect(page.getByTestId('month-label')).toHaveText(monthLabel(M));
    await expect(page.getByTestId('balance-plan')).toBeVisible();
  });

  await test.step('Krok 12: the Kalendarz layout shows the Monday-first grid with dimmed non-business days and the month items', async () => {
    await openCalendar(page);
    await expect(page.locator('.planning-calendar__weekdays span')).toHaveText([
      'pon',
      'wt',
      'śr',
      'czw',
      'pt',
      'sob',
      'niedz',
    ]);
    await expectDimmed(page, M);
    await expectCalendarItem(page, FIRST, 'Życie', '3 000,00');
    await expectCalendarItem(page, WIFE_DATE, 'Wypłata żona', '6 500,00', 'rozliczone');
    await expectCalendarItem(page, TEN, '800+', '1 000,00', 'rozliczone');
    await expectCalendarItem(page, TEN, 'Rachunki', '1 000,00');
    await expectCalendarItem(page, DEPOSIT_END, DEPOSIT, sameText(depositAmount));
    await expectCalendarItem(page, PAYDAY, 'Wypłata Dominik', '9 143,27', 'rozliczone');
  });

  await test.step('Krok 13: a click on the day of 800+ opens the day panel with the settled 800+ and Rachunki', async () => {
    await calendarCell(page, TEN).click();
    const details = page.getByTestId('calendar-details');
    await expect(details).toContainText(dateInText(TEN));
    const settled = details.getByTestId('calendar-detail-item').filter({ hasText: '800+' });
    await expect(settled).toHaveCount(1);
    await expect(settled).toHaveAttribute('data-status', 'settled');
    await expect(settled).toContainText(`Rozliczono`);
    await expect(settled).toContainText(dateInText(D));
    await expect(
      details.getByTestId('calendar-detail-item').filter({ hasText: 'Rachunki' }),
    ).toHaveCount(1);
  });

  await test.step('Krok 14: the next month dims its holidays and a pending Wypłata żona opens the settlement dialog, closed without saving', async () => {
    await page.getByTestId('next-month').click();
    await expect(page.getByTestId('month-label')).toHaveText(monthLabel(NEXT));
    await expectDimmed(page, NEXT);
    const wifeDate = previousBusinessDay(`${NEXT}-10`);
    const pending = calendarCell(page, wifeDate)
      .getByTestId('calendar-item')
      .filter({ hasText: 'Wypłata żona' });
    await expect(pending).toHaveAttribute('data-status', 'pending');
    await pending.click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: /Wypłata żona/ })).toBeVisible();
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(pending).toHaveAttribute('data-status', 'pending');
  });

  await test.step('Krok 15: the month of the first OC Toyota payment shows OC Toyota 1 200 zł on the 15th', async () => {
    for (let offset = 2; offset <= 5; offset++) {
      await page.getByTestId('next-month').click();
      await expect(page.getByTestId('month-label')).toHaveText(monthLabel(shiftMonth(M, offset)));
    }
    await expectCalendarItem(page, `${TOYOTA_MONTH}-15`, 'OC Toyota', '1 200,00');
  });

  await test.step('Krok 16: a new expense Przegląd auta 5 days ahead gives counter 1 and the item with 400 zł on the card', async () => {
    await openMonth(page, M);
    await addExpense(page, {
      name: CHECKUP,
      amount: '400',
      start: `${CHECKUP_DATE.slice(0, 7)}-01`,
      interval: '6',
      day: Number(CHECKUP_DATE.slice(8)),
    });
    await expect(badge(page)).toHaveText('1');
    await openDashboard(page);
    await expect(todoItems(page)).toHaveCount(1);
    await expect(todoItems(page).first()).toContainText(CHECKUP);
    await expect(todoItems(page).first()).toContainText(dateInText(CHECKUP_DATE));
    await expect(todoItems(page).first()).toContainText('za 5 dni · wydatek');
    await expect(todoItems(page).first()).toContainText(pln('400,00'));
  });

  await test.step('Krok 17: the lead time 3 days hides the item and the counter, 0 days keeps them hidden and 7 days brings them back', async () => {
    await openMonth(page, M);
    const input = page.getByTestId('payment-lead-days-input');
    await expect(input).toHaveValue('7');
    await setLeadDays(page, '3');
    await expect(badge(page)).toHaveCount(0);
    await page.reload();
    await expect(input).toHaveValue('3');
    await openDashboard(page);
    await expectAllDone(page);
    await expect(badge(page)).toHaveCount(0);

    await openMonth(page, M);
    await setLeadDays(page, '0');
    await page.reload();
    await expect(input).toHaveValue('0');
    await openDashboard(page);
    await expectAllDone(page);
    await expect(badge(page)).toHaveCount(0);

    await openMonth(page, M);
    await setLeadDays(page, '7');
    await expect(badge(page)).toHaveText('1');
    await openDashboard(page);
    await expect(todoItems(page)).toHaveCount(1);
  });

  await test.step('Krok 18: at 390 px the calendar shows the number of items in the days and a click shows the items under the grid', async () => {
    await page.goto(`/planning?month=${M}&layout=calendar`);
    await expect(page.getByTestId('planning-calendar')).toBeVisible();
    await page.setViewportSize({ width: 390, height: 844 });
    const events = monthEvents();
    for (const date of new Set(events.map((event) => event.date))) {
      const expected = events.filter((event) => event.date === date).length;
      await expect(calendarCell(page, date).getByTestId('calendar-count')).toHaveText(
        String(expected),
      );
    }
    await expect(page.getByTestId('calendar-item')).toHaveCount(0);
    await calendarCell(page, TEN).click();
    const details = page.getByTestId('calendar-details');
    await expect(details).toContainText(dateInText(TEN));
    await expect(
      details.getByTestId('calendar-detail-item').filter({ hasText: '800+' }),
    ).toHaveCount(1);
    await expect(
      details.getByTestId('calendar-detail-item').filter({ hasText: 'Rachunki' }),
    ).toHaveCount(1);
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  await test.step('Krok 19: in English the card, the reminder list, the year view and the calendar are translated and the names stay', async () => {
    await openDashboard(page);
    await page.getByRole('button', { name: 'Język' }).click();
    await page.getByRole('menuitemradio', { name: 'English' }).click();
    const card = monthCard(page);
    await expect(card.getByRole('heading', { name: /· plan$/ })).toBeVisible();
    await expect(card.getByTestId('month-card-settled')).toHaveText('Incomes settled: 3 of 3');
    await expect(todoItems(page)).toHaveCount(1);
    await expect(todoItems(page).first()).toContainText(CHECKUP);
    await expect(todoItems(page).first()).toContainText('in 5 days · expense');

    await navigation(page)
      .getByRole('link', { name: /^(Planowanie|Planning)/ })
      .click();
    await expect(page.getByRole('heading', { name: 'Planning', level: 1 })).toBeVisible();
    await page.getByRole('tab', { name: 'Year', exact: true }).click();
    await expect(page.getByRole('columnheader', { name: 'Income plan' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Free funds forecast' })).toBeVisible();
    await expect(page.getByTestId('year-total').locator('th')).toHaveText('Total');

    await page.getByRole('tab', { name: 'Month', exact: true }).click();
    await page.getByTestId('layout-calendar').click();
    await expect(page.getByTestId('planning-calendar')).toBeVisible();
    await expect(page.locator('.planning-calendar__weekdays span')).toHaveText([
      'Mon',
      'Tue',
      'Wed',
      'Thu',
      'Fri',
      'Sat',
      'Sun',
    ]);
    await expectCalendarItem(page, PAYDAY, 'Wypłata Dominik', /9[\s,.]*143[.,]27/, 'Settled');
    await expectCalendarItem(page, DEPOSIT_END, DEPOSIT, /\d/);
    await calendarCell(page, DEPOSIT_END).click();
    await expect(
      page
        .getByTestId('calendar-details')
        .getByTestId('calendar-detail-item')
        .filter({ hasText: DEPOSIT }),
    ).toContainText('Deposit ends');
  });
});
