import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
} from './fixtures';
import { addExpense, addIncome, choose, openAddDialog, openMonth, pln, settle } from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_SAVINGS = 1;
const TERM_UNIT_MONTHS = 1;
const CAPITALIZATION_AT_MATURITY = 0;
const ASSET_CLASS_DEPOSIT = 1;
const ASSET_CLASS_BANK_ACCOUNT = 9;
const KIND_CUSHION = 1;

const SAVINGS = 'Santander oszczędnościowe';
const DEPOSIT = 'Lokata Santander';
const CUSHION = 'Poduszka finansowa';
const PENSION = 'Emerytura';
const CLASSES = 'Plan klas';
const PREMIA = 'Premia kwartalna';
const INSURANCE = 'Ubezpieczenie OC';

const WIDE = { width: 1440, height: 900 };
const PHONE = { width: 390, height: 844 };

const TODAY = warsawDate();
const MONTH = TODAY.slice(0, 7);
const PREMIA_DATE = warsawDate(23);
const PREMIA_MONTH = PREMIA_DATE.slice(0, 7);
const INSURANCE_DATE = addDays(TODAY, 5);
const PREMIA_IN_MONTH = PREMIA_MONTH === MONTH;

function addDays(iso: string, days: number): string {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function heading(page: Page, name: string | RegExp): Locator {
  return page.getByRole('heading', { name, exact: typeof name === 'string' });
}

function structureRow(page: Page, label: string): Locator {
  return page
    .locator('.dashboard-page__structure li')
    .filter({ has: page.getByRole('link', { name: label, exact: true }) });
}

function groupRow(page: Page, name: string): Locator {
  return page.getByTestId('group-row').filter({ hasText: name });
}

function todoItem(page: Page, name: string): Locator {
  return page.getByTestId('todo-card-item').filter({ hasText: name });
}

function monthCard(page: Page): Locator {
  return page.locator('app-month-card');
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

function filterButton(page: Page): Locator {
  return page.locator('.net-worth-chart__filter');
}

async function chooseFilter(page: Page, name: string): Promise<void> {
  await filterButton(page).click();
  await page.getByRole('menuitem', { name, exact: true }).click();
}

async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(heading(page, 'Pulpit')).toBeVisible();
}

async function openOneOffIncome(page: Page, income: { name: string; date: string }): Promise<void> {
  await openMonth(page, income.date.slice(0, 7));
  const dialog = await openAddDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('1800');
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${SAVINGS}`),
  );
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function addInsurance(page: Page): Promise<void> {
  await openMonth(page, MONTH);
  await addExpense(page, {
    name: INSURANCE,
    amount: '1200',
    start: INSURANCE_DATE,
    interval: '12',
    day: Number(INSURANCE_DATE.slice(8)),
  });
}

async function createGroup(
  request: APIRequestContext,
  token: string,
  data: Record<string, unknown>,
): Promise<string> {
  const created = await request.post('/api/portfolio/asset-groups', {
    headers: authorization(token),
    data,
  });
  expect(created.ok()).toBeTruthy();
  return ((await created.json()) as { id: string }).id;
}

async function assign(
  request: APIRequestContext,
  token: string,
  assetIds: string[],
  groupId: string,
): Promise<void> {
  const assigned = await request.put('/api/portfolio/asset-groups/assignment', {
    headers: authorization(token),
    data: { assetIds, groupId },
  });
  expect(assigned.ok()).toBeTruthy();
}

test('redesign pulpit: value, history, structure, work sections, groups, phone, dark theme and section errors', async ({
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
  const netWorth = page.getByTestId('net-worth');
  let savingsCashAssetId = '';
  let depositAssetId = '';
  let cushionId = '';
  let pensionId = '';

  await test.step('Krok 1: a fresh account sees the empty state with "Dodaj rachunek" and no value, history or lists', async () => {
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(heading(page, 'Pulpit')).toBeVisible();
    await expect(page.getByText('Nie wprowadzono jeszcze żadnego majątku.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Dodaj rachunek' })).toBeVisible();
    await expect(netWorth).toHaveCount(0);
    await expect(heading(page, 'Historia wartości majątku')).toHaveCount(0);
    await expect(heading(page, 'Wg grup')).toHaveCount(0);
  });

  await test.step('Krok 2: "Dodaj rachunek" leads to /wealth/accounts; the savings account has 60 000 zł', async () => {
    await page.getByRole('link', { name: 'Dodaj rachunek' }).click();
    await expect(page).toHaveURL(/\/wealth\/accounts$/);
    const account = await createAccountThroughApi(request, token, {
      name: SAVINGS,
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: VARIANT_SAVINGS,
      annualInterestRatePercent: 0,
    } as Parameters<typeof createAccountThroughApi>[2]);
    savingsCashAssetId = account.cashAssetId;
    await depositCashThroughApi(request, token, savingsCashAssetId, {
      quantity: 60000,
      date: warsawDate(40),
    });
  });

  await test.step('Krok 3: the empty state is gone, the value is 60 000,00 zł with the 1Y change and "Stan na"', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(netWorth).toContainText(pln('60 000,00'), { timeout: 2_000 });
      },
    );
    await expect(page.getByText('Nie wprowadzono jeszcze żadnego majątku.')).toHaveCount(0);
    await expect(page.locator('.dashboard-page__change')).toContainText('zmiana za 1Y');
    await expect(page.locator('.dashboard-page__as-of')).toContainText(/Stan na\s.*\d{4}/);
  });

  await test.step('Krok 4: after the deposit "Struktura majątku" splits 40 000 / 20 000 and the Lokata row links to /wealth/deposits', async () => {
    const created = await request.post('/api/portfolio/deposits', {
      headers,
      data: {
        name: DEPOSIT,
        sourceAccountId: (
          (await (await request.get('/api/portfolio/accounts', { headers })).json()) as {
            id: string;
            name: string;
          }[]
        ).find((account) => account.name === SAVINGS)?.id,
        principal: 20000,
        startDate: warsawDate(5),
        termLength: 1,
        termUnit: TERM_UNIT_MONTHS,
        annualInterestRatePercent: 5.1,
        capitalization: CAPITALIZATION_AT_MATURITY,
      },
    });
    expect(created.ok()).toBeTruthy();
    depositAssetId = ((await created.json()) as { assetId: string }).assetId;

    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(structureRow(page, 'Konta bankowe')).toContainText(pln('40 000,00'), {
          timeout: 2_000,
        });
        await expect(structureRow(page, 'Lokata')).toContainText(pln('20 000,00'), {
          timeout: 2_000,
        });
      },
    );
    await expect(
      page.locator('.dashboard-page__structure app-split-bar [role="img"]'),
    ).toBeVisible();
    await expect(page.locator('.dashboard-page__structure li')).toHaveCount(2);
    await expect(structureRow(page, 'Konta bankowe')).toContainText(/66,67\s?%/);
    await expect(structureRow(page, 'Lokata')).toContainText(/33,33\s?%/);
    await structureRow(page, 'Lokata').getByRole('link', { name: 'Lokata', exact: true }).click();
    await expect(page).toHaveURL(/\/wealth\/deposits$/);
  });

  await test.step('Krok 5: "Nadchodzące · 30 dni" has the "Lokata zapada" row linking to /wealth/deposits', async () => {
    await openDashboard(page);
    const upcoming = page.locator('app-upcoming-card');
    await expect(upcoming.getByRole('heading', { name: 'Nadchodzące · 30 dni' })).toBeVisible();
    const row = upcoming.locator('.upcoming-card__row').filter({ hasText: 'Lokata zapada' });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText(/20\s?\d{3},\d{2}\szł/);
    await expect(row.getByRole('link', { name: DEPOSIT })).toHaveAttribute(
      'href',
      /\/wealth\/deposits/,
    );
  });

  await test.step('Krok 6: Rachunki lists the savings account and Gotówka has no places', async () => {
    await expect(
      page.locator('app-accounts-card').getByRole('link', { name: SAVINGS }),
    ).toBeVisible();
    await expect(page.locator('app-cash-card')).toContainText('Brak miejsc z gotówką.');
  });

  await test.step('Krok 7: the "Portfel" cash place shows PLN 800,00 zł, EUR 420,00 EUR with "≈ … zł" and "Razem"', async () => {
    const place = await request.post('/api/portfolio/cash-places', {
      headers,
      data: { name: 'Portfel', currencies: ['PLN', 'EUR'] },
    });
    expect(place.ok()).toBeTruthy();
    const balances = (
      (await place.json()) as {
        balances: { currency: string; cashAssetId: string }[];
      }
    ).balances;
    for (const [currency, quantity] of [
      ['PLN', 800],
      ['EUR', 420],
    ] as const) {
      await depositCashThroughApi(
        request,
        token,
        balances.find((balance) => balance.currency === currency)?.cashAssetId as string,
        { quantity, date: warsawDate() },
      );
    }
    const cash = page.locator('app-cash-card');
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(cash).toContainText(pln('800,00'), { timeout: 2_000 });
        await expect(cash).toContainText(/420,00\sEUR/, { timeout: 2_000 });
        await expect(cash).toContainText(/≈\s*1\s?785,00\szł/, { timeout: 2_000 });
        await expect(cash).toContainText('Razem');
      },
    );
    await expect(page.locator('app-accounts-card')).toContainText('Poza rachunkami');
  });

  await test.step('Krok 8: the ranges 1M, YTD, MAX and 1Y each select and label the change', async () => {
    for (const range of ['1M', 'YTD', 'MAX', '1Y']) {
      await rangeToggle(page, range).click();
      await expect(rangeToggle(page, range)).toBeChecked();
      await expect(page.locator('.dashboard-page__change')).toContainText(
        new RegExp(`zmiana za ${range}|^\\s*—\\s*$`),
      );
      await expect(
        page
          .getByRole('img', { name: /^Wykres historii wartości/ })
          .or(page.getByText('Za mało historii, aby narysować wykres.')),
      ).toBeVisible();
    }
    await expect(netWorth).toBeVisible();
  });

  await test.step('Krok 9: the "Wszystko" menu filters by Lokata and by the class Konta bankowe and clears again', async () => {
    await expect(filterButton(page)).toContainText('Wszystko');
    await chooseFilter(page, 'Lokata');
    await expect(filterButton(page)).toContainText('Lokata');
    await expect(page.locator('.dashboard-page__change')).toContainText('Lokata');
    await chooseFilter(page, 'Konta bankowe');
    await expect(filterButton(page)).toContainText('Konta bankowe');
    await chooseFilter(page, 'Wszystko');
    await expect(filterButton(page)).toContainText('Wszystko');
  });

  await test.step('Krok 10: planning has Pensja, Premia kwartalna, Czynsz and Ubezpieczenie OC', async () => {
    await openMonth(page, MONTH);
    await addIncome(page, {
      name: 'Pensja',
      amount: '8000',
      account: SAVINGS,
      start: `${MONTH}-01`,
      interval: '1',
      day: 'last',
    });
    await addExpense(page, {
      name: 'Czynsz',
      amount: '2500',
      start: `${MONTH}-01`,
      interval: '1',
      day: 1,
    });
    await openOneOffIncome(page, { name: PREMIA, date: PREMIA_DATE });
    await addInsurance(page);
  });

  await test.step('Krok 11: "Do zrobienia" has the overdue Premia with "Rozlicz" and the upcoming Ubezpieczenie OC without a button', async () => {
    await openDashboard(page);
    await expect(heading(page, 'Do zrobienia')).toBeVisible();
    const premia = todoItem(page, PREMIA);
    await expect(premia).toHaveCount(1);
    await expect(premia).toContainText('zaległe: 23 dni');
    await expect(premia).toContainText(pln('1 800,00'));
    await expect(premia.getByRole('link', { name: 'Rozlicz' })).toBeVisible();
    const insurance = todoItem(page, INSURANCE);
    await expect(insurance).toHaveCount(1);
    await expect(insurance).toContainText('za 5 dni · wydatek');
    await expect(insurance).toContainText(pln('-1 200,00'));
    await expect(insurance.getByRole('link', { name: 'Rozlicz' })).toHaveCount(0);
    await premia.getByRole('link', { name: 'Rozlicz' }).click();
    await expect(page).toHaveURL(/\/planning/);
  });

  await test.step('Krok 12: the groups Poduszka finansowa (60 000 zł, 1 500 zł) and Emerytura (1 000 zł) exist and the deposit is in the cushion', async () => {
    cushionId = await createGroup(request, token, {
      name: CUSHION,
      color: 'orange',
      kind: KIND_CUSHION,
      targetPln: 60000,
      monthlyContributionPln: 1500,
    });
    pensionId = await createGroup(request, token, {
      name: PENSION,
      color: 'purple',
      monthlyContributionPln: 1000,
    });
    await assign(request, token, [depositAssetId], cushionId);
  });

  const settledTotal = PREMIA_IN_MONTH ? 2 : 1;

  await test.step('Krok 13: "Ten miesiąc" cascade with incomes, expenses, group contributions, free funds, the split rows, the forecast and "Wpływy załatwione"', async () => {
    await openMonth(page, MONTH);
    const planned = async (testId: string): Promise<string> =>
      (await page.getByTestId(testId).locator('dd').innerText()).trim();
    const incomesText = await planned('balance-plan');
    const expensesText = await planned('expense-cost');
    const freeText = await planned('free-funds-plan');
    const same = (text: string): RegExp => new RegExp(text.replace(/\s/g, '\\s'));
    await openDashboard(page);
    const card = monthCard(page);
    await expect(card.getByTestId('month-card-incomes')).toContainText(same(incomesText));
    const amountOf = (text: string): number =>
      Number(
        text
          .replace(/[^\d,\-−]/g, '')
          .replace('−', '-')
          .replace(',', '.'),
      );
    const cardExpenses = await card
      .getByTestId('month-card-expenses')
      .locator('.month-card__amount')
      .innerText();
    expect(amountOf(cardExpenses) + 2500).toBe(amountOf(expensesText));
    await expect(card.getByTestId('month-card-contributions')).toContainText(pln('2 500,00'));
    await expect(card.locator('app-split-bar [role="img"]')).toBeVisible();
    await expect(card.getByTestId('month-card-free')).toContainText(same(freeText));
    await expect(card.getByTestId('month-card-split-row')).toHaveCount(2);
    await expect(
      card.getByTestId('month-card-split-row').filter({ hasText: `→ ${CUSHION}` }),
    ).toHaveCount(1);
    await expect(
      card.getByTestId('month-card-split-row').filter({ hasText: `→ ${PENSION}` }),
    ).toHaveCount(1);
    await expect(card.getByTestId('month-card-forecast')).toContainText(
      'Prognoza wolnych środków:',
    );
    await expect(card.getByTestId('month-card-settled')).toHaveText(
      `Wpływy załatwione: 0 z ${settledTotal}`,
    );
  });

  await test.step('Krok 14: after settling Premia kwartalna the row leaves "Do rozliczenia" and "Wpływy załatwione" grows', async () => {
    await openMonth(page, PREMIA_MONTH);
    await settle(page, PREMIA, '1800');
    await openDashboard(page);
    await expect(todoItem(page, PREMIA)).toHaveCount(0);
    if (PREMIA_IN_MONTH) {
      await expect(monthCard(page).getByTestId('month-card-settled')).toHaveText(
        `Wpływy załatwione: 1 z ${settledTotal}`,
      );
    }
  });

  await test.step('Krok 15: a larger Emerytura contribution turns "Wolne środki" into a deficit and removes the split rows', async () => {
    const updated = await request.put(`/api/portfolio/asset-groups/${pensionId}`, {
      headers,
      data: { name: PENSION, color: 'purple', kind: 0, monthlyContributionPln: 10000 },
    });
    expect(updated.ok()).toBeTruthy();
    await openDashboard(page);
    const card = monthCard(page);
    await expect(card.getByTestId('month-card-free-deficit')).toContainText(/deficyt/i);
    await expect(card.getByTestId('month-card-split-row')).toHaveCount(0);
  });

  await test.step('Krok 16: "Wg grup" rows for Poduszka finansowa, Emerytura and Bez grupy', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(groupRow(page, CUSHION)).toContainText(pln('20 000,00'), { timeout: 2_000 });
      },
    );
    const cushion = groupRow(page, CUSHION);
    await expect(cushion.getByTestId('group-dot')).toBeVisible();
    await expect(cushion).toContainText('Poduszka');
    await expect(cushion).toContainText(/\d+(,\d+)?\s?%/);
    await expect(cushion.getByRole('progressbar')).toBeVisible();
    await expect(cushion.getByTestId('cushion-level')).toContainText(
      /20\s000,00\szł z 60\s000,00\szł, brakuje 40\s000,00\szł/,
    );
    await expect(groupRow(page, PENSION).getByRole('progressbar')).toHaveCount(0);
    const ungrouped = groupRow(page, 'Bez grupy');
    await expect(ungrouped).toContainText(/majątku/);
    await expect(ungrouped.getByRole('progressbar')).toHaveCount(0);
    const rows = page.getByTestId('group-row');
    await expect(rows.last()).toContainText('Bez grupy');
  });

  await test.step('Krok 17: a group whose class shares drift from the allocation shows the class, held share and target', async () => {
    const classesId = await createGroup(request, token, {
      name: CLASSES,
      color: 'green',
      classAllocation: [
        { assetClass: ASSET_CLASS_DEPOSIT, percent: 50 },
        { assetClass: ASSET_CLASS_BANK_ACCOUNT, percent: 50 },
      ],
    });
    await assign(request, token, [savingsCashAssetId], classesId);
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(groupRow(page, CLASSES).getByTestId('group-class-shares')).toContainText(
          /Konta bankowe: 100\s?% \(cel 50\s?%\)/,
          { timeout: 2_000 },
        );
      },
    );
  });

  await test.step('Krok 18: the group name links to /groups and does not change the history filter', async () => {
    await expect(filterButton(page)).toContainText('Wszystko');
    await groupRow(page, CUSHION).getByRole('link', { name: CUSHION }).click();
    await expect(page).toHaveURL(/\/groups$/);
    await openDashboard(page);
    await expect(filterButton(page)).toContainText('Wszystko');
  });

  await test.step('Krok 19: at 390 px the sections are one column in order, targets are at least 44 px and nothing scrolls sideways', async () => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    await expect(heading(page, 'Wg grup')).toBeVisible();
    const order = [
      heading(page, 'Historia wartości majątku'),
      heading(page, 'Do zrobienia'),
      monthCard(page).getByRole('heading', { name: /· plan$/ }),
      heading(page, 'Wg grup'),
      heading(page, 'Struktura majątku'),
      heading(page, 'Rachunki'),
      heading(page, 'Gotówka'),
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
    for (const target of [rangeToggle(page, '1Y'), filterButton(page)]) {
      const box = await target.boundingBox();
      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
    }
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 20: in the dark theme the sections and the deficit marker stay visible', async () => {
    await page.setViewportSize(WIDE);
    const theme = page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
    await theme.click();
    await theme.click();
    await expect(theme).toHaveAccessibleName(/^Motyw: ciemny/);
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect(heading(page, 'Wg grup')).toBeVisible();
    await expect(heading(page, 'Struktura majątku')).toBeVisible();
    await expect(monthCard(page).getByTestId('month-card-free-deficit')).toBeVisible();
  });

  await test.step('Krok 21: without stale valuations there is no "Nieaktualne" chip and no stale sentence (stale case: tylko manualnie)', async () => {
    await expect(page.getByText('Nieaktualne', { exact: true })).toHaveCount(0);
    await expect(page.getByText(/od dawna się nie odświeżyła/)).toHaveCount(0);
  });

  await test.step('Krok 22: a failing todo request shows an error with "Ponów" in "Do zrobienia" while the rest works, and the retry brings the list back', async () => {
    await page.route('**/api/portfolio/planning/todo*', (route) => route.abort());
    await page.reload();
    const todo = page.locator('app-todo-card');
    await expect(todo.getByRole('button', { name: 'Ponów' })).toBeVisible();
    await expect(netWorth).toBeVisible();
    await expect(heading(page, 'Wg grup')).toBeVisible();
    await page.unroute('**/api/portfolio/planning/todo*');
    await todo.getByRole('button', { name: 'Ponów' }).click();
    await expect(todoItem(page, INSURANCE)).toHaveCount(1);
  });
});
