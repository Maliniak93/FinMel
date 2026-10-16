import { type Locator, type Page } from '@playwright/test';
import { expect, logInThroughApi, logInThroughUi, test } from './fixtures';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;

const MONTH_NAMES = [
  'styczeń',
  'luty',
  'marzec',
  'kwiecień',
  'maj',
  'czerwiec',
  'lipiec',
  'sierpień',
  'wrzesień',
  'październik',
  'listopad',
  'grudzień',
];

const MONTH_ABBREVIATIONS = [
  'sty',
  'lut',
  'mar',
  'kwi',
  'maj',
  'cze',
  'lip',
  'sie',
  'wrz',
  'paź',
  'lis',
  'gru',
];

const MBANK = 'mBank ROR';
const COMPANY = 'Konto firmowe';
const REVOLUT = 'Revolut EUR';

// pl-PL leaves out the grouping space in four-digit numbers on some ICU builds, so the space is optional.
function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

function eur(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\s(?:EUR|€)`);
}

function monthTotal(amount: string): RegExp {
  return new RegExp(`Suma miesiąca:\\s${amount.replace(/ /g, '\\s?')}\\szł`);
}

function shortDate(iso: string): RegExp {
  const [year, month, day] = iso.split('-').map(Number) as [number, number, number];
  return new RegExp(`^${day}\\s${MONTH_ABBREVIATIONS[month - 1]}\\.?\\s${year}`);
}

function monthLabel(month: string): string {
  const [year, number] = month.split('-').map(Number) as [number, number];
  return `${MONTH_NAMES[number - 1]} ${year}`;
}

function exactly(text: string): RegExp {
  return new RegExp(`^${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
}

async function openMonth(page: Page, month: string): Promise<void> {
  await page.goto(`/planning?month=${month}`);
  await expect(page.getByTestId('month-label')).toHaveText(monthLabel(month));
  await expect(page.locator('.planning-section__total')).toBeVisible();
}

function occurrences(page: Page, name: string): Locator {
  return page
    .getByTestId('occurrence-row')
    .filter({ has: page.getByRole('cell', { name: exactly(name) }) });
}

function occurrence(page: Page, name: string, iso: string): Locator {
  return occurrences(page, name).filter({ has: page.getByRole('cell', { name: shortDate(iso) }) });
}

function definition(page: Page, name: string): Locator {
  return page.getByTestId('definition-row').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

async function choose(page: Page, select: Locator, option: string | RegExp): Promise<void> {
  await select.click();
  await page
    .getByRole(
      'option',
      typeof option === 'string' ? { name: option, exact: true } : { name: option },
    )
    .click();
}

interface IncomeInput {
  name: string;
  amount: string;
  account: string;
  start: string;
  end?: string;
  interval: string;
  day: number | 'last';
  shift?: 'Bez przesunięcia' | 'Poprzedni dzień roboczy' | 'Następny dzień roboczy';
}

async function fillRule(page: Page, dialog: Locator, income: IncomeInput): Promise<void> {
  await dialog.getByRole('spinbutton', { name: 'Powtarzaj co (miesięcy)' }).fill(income.interval);
  const dayKind = dialog.getByRole('combobox', { name: 'Dzień', exact: true });
  if (income.day === 'last') {
    await choose(page, dayKind, 'Ostatni dzień roboczy');
    return;
  }
  await choose(page, dayKind, 'Dzień miesiąca');
  await dialog.getByRole('spinbutton', { name: 'Dzień miesiąca' }).fill(String(income.day));
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Przesunięcie na dzień roboczy' }),
    income.shift ?? 'Bez przesunięcia',
  );
}

async function openAddDialog(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Dodaj wpływ' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Dodaj wpływ cykliczny' })).toBeVisible();
  return dialog;
}

async function fillIncome(page: Page, dialog: Locator, income: IncomeInput): Promise<void> {
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill(income.amount);
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${income.account}`),
  );
  await dialog.getByLabel('Data początku').fill(income.start);
  if (income.end) {
    await dialog.getByLabel('Data końca').fill(income.end);
  }
  await fillRule(page, dialog, income);
}

async function addIncome(page: Page, income: IncomeInput): Promise<void> {
  const dialog = await openAddDialog(page);
  await fillIncome(page, dialog, income);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function openEditDialog(page: Page, name: string): Promise<Locator> {
  await definition(page, name).getByTestId('edit-income').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj wpływ cykliczny' })).toBeVisible();
  return dialog;
}

async function saveEdit(dialog: Locator): Promise<void> {
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function confirmDelete(page: Page): Promise<void> {
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
  await expect(dialog).toBeHidden();
}

test('recurring incomes: define incomes, read any month of /planning, change them from a month', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(600_000);

  const token = await logInThroughApi(request, user);
  const headers = { Authorization: `Bearer ${token}` };
  for (const account of [
    { name: MBANK, currency: 'PLN' },
    { name: COMPANY, currency: 'PLN' },
    { name: REVOLUT, currency: 'EUR' },
  ]) {
    const created = await request.post('/api/portfolio/accounts', {
      headers,
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

  await page.goto('/login');
  await logInThroughUi(page, user);

  await test.step('Krok 1: Planowanie opens /planning with the current month, no incomes and a total of 0,00 zł', async () => {
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Planowanie', exact: true })
      .click();
    await expect(page).toHaveURL(/\/planning/);
    await expect(page.getByRole('heading', { name: 'Planowanie', exact: true })).toBeVisible();
    await expect(page.getByTestId('month-label')).toHaveText(/\S+ \d{4}/);
    await expect(page.getByText('W tym miesiącu nie oczekujesz żadnych wpływów.')).toBeVisible();
    await expect(page.locator('.planning-section__total')).toContainText(monthTotal('0,00'));
  });

  await test.step('Krok 2: /planning?month=2026-10 shows październik 2026, the arrows go to listopad 2026 and back', async () => {
    await openMonth(page, '2026-10');
    await page.getByRole('button', { name: 'Następny miesiąc' }).click();
    await expect(page.getByTestId('month-label')).toHaveText('listopad 2026');
    await page.getByRole('button', { name: 'Poprzedni miesiąc' }).click();
    await expect(page.getByTestId('month-label')).toHaveText('październik 2026');
    await expect(page).toHaveURL(/month=2026-10/);
  });

  await test.step('Krok 3: Wypłata Dominik 9 200 zł without a currency question occurs on 30.10.2026 on mBank ROR', async () => {
    const dialog = await openAddDialog(page);
    await expect(dialog.getByRole('combobox', { name: 'Waluta' })).toHaveCount(0);
    await fillIncome(page, dialog, {
      name: 'Wypłata Dominik',
      amount: '9200',
      account: MBANK,
      start: '2026-01-01',
      interval: '1',
      day: 'last',
    });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    const row = occurrence(page, 'Wypłata Dominik', '2026-10-30');
    await expect(row).toBeVisible();
    await expect(row).toContainText(pln('9 200,00'));
    await expect(row).toContainText(MBANK);
  });

  await test.step('Krok 4: Wypłata żona 6 500 zł occurs on 09.10.2026 because 10.10 is a Saturday', async () => {
    await addIncome(page, {
      name: 'Wypłata żona',
      amount: '6500',
      account: MBANK,
      start: '2026-01-01',
      interval: '1',
      day: 10,
      shift: 'Poprzedni dzień roboczy',
    });
    const row = occurrence(page, 'Wypłata żona', '2026-10-09');
    await expect(row).toBeVisible();
    await expect(row).toContainText(pln('6 500,00'));
  });

  await test.step('Krok 5: 800+ occurs on 10.10.2026 and the October total is 16 500,00 zł', async () => {
    await addIncome(page, {
      name: '800+',
      amount: '800',
      account: MBANK,
      start: '2024-06-10',
      end: '2032-03-15',
      interval: '1',
      day: 10,
    });
    const row = occurrence(page, '800+', '2026-10-10');
    await expect(row).toBeVisible();
    await expect(row).toContainText(pln('800,00'));
    await expect(page.locator('.planning-section__total')).toContainText(monthTotal('16 500,00'));
  });

  await test.step('Krok 6: a second 800+ is rejected with the taken-name message', async () => {
    const dialog = await openAddDialog(page);
    await fillIncome(page, dialog, {
      name: '800+',
      amount: '800',
      account: MBANK,
      start: '2024-06-10',
      interval: '1',
      day: 10,
    });
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByText('Wpływ o tej nazwie już istnieje.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(definition(page, '800+')).toHaveCount(1);
  });

  await test.step('Krok 7: listopad 2026 has żona 10.11, 800+ 10.11, Dominik 30.11 and the total 16 500,00 zł', async () => {
    await page.getByRole('button', { name: 'Następny miesiąc' }).click();
    await expect(page.getByTestId('month-label')).toHaveText('listopad 2026');
    await expect(occurrence(page, 'Wypłata żona', '2026-11-10')).toContainText(pln('6 500,00'));
    await expect(occurrence(page, '800+', '2026-11-10')).toContainText(pln('800,00'));
    await expect(occurrence(page, 'Wypłata Dominik', '2026-11-30')).toContainText(pln('9 200,00'));
    await expect(page.locator('.planning-section__total')).toContainText(monthTotal('16 500,00'));
  });

  await test.step('Krok 8: marzec 2032 has 800+ on 10.03.2032, kwiecień 2032 has none and the definition is Zakończony', async () => {
    await openMonth(page, '2032-03');
    await expect(occurrence(page, '800+', '2032-03-10')).toBeVisible();
    await page.getByRole('button', { name: 'Następny miesiąc' }).click();
    await expect(page.getByTestId('month-label')).toHaveText('kwiecień 2032');
    await expect(occurrences(page, '800+')).toHaveCount(0);
    await expect(definition(page, '800+')).toContainText('Zakończony');
  });

  await test.step('Krok 9: Premia roczna is Nadchodzący in October 2026, occurs 25.03.2027 and not in April 2027', async () => {
    await openMonth(page, '2026-10');
    await addIncome(page, {
      name: 'Premia roczna',
      amount: '12000',
      account: MBANK,
      start: '2027-03-01',
      interval: '12',
      day: 25,
    });
    await expect(definition(page, 'Premia roczna')).toContainText('Nadchodzący');
    await expect(occurrences(page, 'Premia roczna')).toHaveCount(0);
    await openMonth(page, '2027-03');
    const row = occurrence(page, 'Premia roczna', '2027-03-25');
    await expect(row).toBeVisible();
    await expect(row).toContainText(pln('12 000,00'));
    await openMonth(page, '2027-04');
    await expect(occurrences(page, 'Premia roczna')).toHaveCount(0);
  });

  await test.step('Krok 10: Najem EUR shows 1 000 EUR with an estimate of 4 250,00 zł on 15.10.2026 and the total 20 750,00 zł', async () => {
    await openMonth(page, '2026-10');
    await addIncome(page, {
      name: 'Najem EUR',
      amount: '1000',
      account: REVOLUT,
      start: '2026-10-01',
      interval: '1',
      day: 15,
    });
    const row = occurrence(page, 'Najem EUR', '2026-10-15');
    await expect(row).toContainText(eur('1 000,00'));
    // The rate reaches MarketData asynchronously, so each attempt opens the month afresh.
    await expect(async () => {
      await openMonth(page, '2026-10');
      await expect(row).toContainText(/≈\s4\s?250,00\szł/, { timeout: 3_000 });
      await expect(page.locator('.planning-section__total')).toContainText(
        monthTotal('20 750,00'),
        { timeout: 3_000 },
      );
    }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 3_000] });
  });

  await test.step('Krok 11: Zwrot kosztów has 01.07 and 31.07.2026 in lipiec and nothing in sierpień', async () => {
    await addIncome(page, {
      name: 'Zwrot kosztów',
      amount: '300',
      account: MBANK,
      start: '2026-07-01',
      end: '2026-08-31',
      interval: '1',
      day: 1,
      shift: 'Poprzedni dzień roboczy',
    });
    await openMonth(page, '2026-07');
    await expect(occurrences(page, 'Zwrot kosztów')).toHaveCount(2);
    await expect(occurrence(page, 'Zwrot kosztów', '2026-07-01')).toContainText(pln('300,00'));
    await expect(occurrence(page, 'Zwrot kosztów', '2026-07-31')).toContainText(pln('300,00'));
    await openMonth(page, '2026-08');
    await expect(occurrences(page, 'Zwrot kosztów')).toHaveCount(0);
  });

  await test.step('Krok 12: Bon świąteczny occurs on 12.11.2026 because 11.11 is a holiday', async () => {
    await addIncome(page, {
      name: 'Bon świąteczny',
      amount: '500',
      account: MBANK,
      start: '2026-11-01',
      interval: '12',
      day: 11,
      shift: 'Następny dzień roboczy',
    });
    await openMonth(page, '2026-11');
    const row = occurrence(page, 'Bon świąteczny', '2026-11-12');
    await expect(row).toBeVisible();
    await expect(row).toContainText(pln('500,00'));
  });

  await test.step('Krok 13: ending Wypłata żona on 2026-12-31 keeps 10.12.2026, drops styczeń 2027 and keeps 09.10.2026', async () => {
    await definition(page, 'Wypłata żona')
      .getByRole('button', { name: 'Zakończ', exact: true })
      .click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Data końca').fill('2026-12-31');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await openMonth(page, '2026-12');
    await expect(occurrence(page, 'Wypłata żona', '2026-12-10')).toBeVisible();
    await openMonth(page, '2027-01');
    await expect(occurrences(page, 'Wypłata żona')).toHaveCount(0);
    await openMonth(page, '2026-10');
    await expect(occurrence(page, 'Wypłata żona', '2026-10-09')).toBeVisible();
  });

  await test.step('Krok 14: Wypłata Dominik 9 800 zł from 2027-01 gives 9 200 zł on 31.12.2026, 9 800 zł on 29.01.2027 and a scheduled change in the list', async () => {
    const dialog = await openEditDialog(page, 'Wypłata Dominik');
    await dialog.getByLabel('Obowiązuje od').fill('2027-01');
    await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('9800');
    await saveEdit(dialog);
    const change = definition(page, 'Wypłata Dominik').getByTestId('next-change');
    await expect(change).toContainText('01.2027');
    await expect(change).toContainText(pln('9 800,00'));
    await openMonth(page, '2026-12');
    await expect(occurrence(page, 'Wypłata Dominik', '2026-12-31')).toContainText(pln('9 200,00'));
    await openMonth(page, '2027-01');
    await expect(occurrence(page, 'Wypłata Dominik', '2027-01-29')).toContainText(pln('9 800,00'));
  });

  await test.step('Krok 15: Wypłata Dominik on Konto firmowe from 2026-11 keeps październik on mBank ROR and the 9 800 zł change in styczeń 2027', async () => {
    await openMonth(page, '2026-10');
    const dialog = await openEditDialog(page, 'Wypłata Dominik');
    await dialog.getByLabel('Obowiązuje od').fill('2026-11');
    await choose(
      page,
      dialog.getByRole('combobox', { name: 'Konto', exact: true }),
      new RegExp(`^${COMPANY}`),
    );
    await saveEdit(dialog);
    await expect(occurrence(page, 'Wypłata Dominik', '2026-10-30')).toContainText(MBANK);
    await openMonth(page, '2026-11');
    await expect(occurrence(page, 'Wypłata Dominik', '2026-11-30')).toContainText(COMPANY);
    await openMonth(page, '2026-12');
    await expect(occurrence(page, 'Wypłata Dominik', '2026-12-31')).toContainText(COMPANY);
    await openMonth(page, '2027-01');
    const row = occurrence(page, 'Wypłata Dominik', '2027-01-29');
    await expect(row).toContainText(pln('9 800,00'));
    await expect(row).toContainText(MBANK);
  });

  await test.step('Krok 16: deleting the change from 01.2027 gives 9 200 zł on Konto firmowe on 29.01.2027 and no scheduled change', async () => {
    await openMonth(page, '2026-12');
    const change = definition(page, 'Wypłata Dominik').getByTestId('next-change');
    await expect(change).toContainText('01.2027');
    await change.getByTestId('delete-change').click();
    await confirmDelete(page);
    await expect(change).toHaveCount(0);
    await openMonth(page, '2027-01');
    const row = occurrence(page, 'Wypłata Dominik', '2027-01-29');
    await expect(row).toContainText(pln('9 200,00'));
    await expect(row).toContainText(COMPANY);
  });

  await test.step('Krok 17: renaming to Wypłata from grudzień 2026 shows the new name in październik 2026 and styczeń 2026', async () => {
    await openMonth(page, '2026-12');
    const dialog = await openEditDialog(page, 'Wypłata Dominik');
    await dialog.getByLabel('Obowiązuje od').fill('2026-12');
    await dialog.getByLabel('Nazwa').fill('Wypłata');
    await saveEdit(dialog);
    await openMonth(page, '2026-10');
    await expect(occurrence(page, 'Wypłata', '2026-10-30')).toBeVisible();
    await openMonth(page, '2026-01');
    await expect(occurrence(page, 'Wypłata', '2026-01-30')).toBeVisible();
  });

  await test.step('Krok 18: archiving Konto firmowe works and listopad 2026 still counts Wypłata with an archived-account warning', async () => {
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Konta bankowe', exact: true })
      .click();
    await page.getByRole('button', { name: `Akcje dla: ${COMPANY}`, exact: true }).click();
    await page.getByRole('menuitem', { name: 'Archiwizuj', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Archiwizuj', exact: true }).click();
    await expect(dialog).toBeHidden();
    await openMonth(page, '2026-11');
    const row = occurrence(page, 'Wypłata', '2026-11-30');
    await expect(row.getByTestId('archived-warning')).toContainText('konto zarchiwizowane');
    await expect(page.locator('.planning-section__total')).toContainText(monthTotal('21 250,00'));
  });

  await test.step('Krok 19: deleting Bon świąteczny removes 12.11.2026 and the definition', async () => {
    await definition(page, 'Bon świąteczny')
      .getByRole('button', { name: 'Usuń', exact: true })
      .click();
    await confirmDelete(page);
    await expect(occurrences(page, 'Bon świąteczny')).toHaveCount(0);
    await expect(definition(page, 'Bon świąteczny')).toHaveCount(0);
    await expect(page.locator('.planning-section__total')).toContainText(monthTotal('20 750,00'));
  });

  await test.step('Krok 20: styczeń 2026 lists żona 09.01, 800+ 10.01 and Wypłata 30.01 for 9 200 zł on mBank ROR', async () => {
    await openMonth(page, '2026-01');
    await expect(occurrence(page, 'Wypłata żona', '2026-01-09')).toBeVisible();
    await expect(occurrence(page, '800+', '2026-01-10')).toBeVisible();
    const row = occurrence(page, 'Wypłata', '2026-01-30');
    await expect(row).toContainText(pln('9 200,00'));
    await expect(row).toContainText(MBANK);
  });
});
