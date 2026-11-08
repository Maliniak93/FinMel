import type { Locator, Page } from '@playwright/test';
import { expect, openSelect, test, wealthTab } from './fixtures';
import { addIncome, occurrences, openMonth, pln, settle, status } from './planning';

const IKE = 'IKE Broker';
const IKZE = 'IKZE Broker';
const INCOME = 'Wpłata na IKE';
const YEAR = '2026';
const NOVEMBER = '2026-11';
const DECEMBER = '2026-12';

function amount(value: string): string {
  return value.replace(/ /g, '\\s?');
}

function paidOf(paid: string, limit: string): RegExp {
  return new RegExp(
    `Wpłacono\\s${amount(paid)}(,00)?\\s/\\s${amount(limit)}(,00)?\\szł\\sw\\s${YEAR}`,
  );
}

function limitRow(page: Page, year: string): Locator {
  return page.locator(`[data-testid="contribution-limit-row"][data-year="${year}"]`);
}

async function selectOption(page: Page, select: Locator, option: string): Promise<void> {
  await openSelect(select);
  await page.getByRole('option', { name: option, exact: true }).click();
}

async function createAccount(page: Page, name: string, wrapper: string): Promise<void> {
  const dialog = page.getByRole('dialog');
  await page
    .getByRole('button', { name: /^(Nowy rachunek|Utwórz pierwszy rachunek)$/ })
    .first()
    .click();
  await dialog.getByLabel('Nazwa').fill(name);
  await selectOption(page, dialog.getByRole('combobox', { name: 'Typ' }), 'Maklerski');
  await selectOption(page, dialog.getByRole('combobox', { name: 'Opakowanie podatkowe' }), wrapper);
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

async function openAccountsList(page: Page): Promise<void> {
  await wealthTab(page, 'Rachunki inwestycyjne');
  await expect(accountRow(page, IKE)).toBeVisible();
}

async function openAccount(page: Page, name: string): Promise<void> {
  await openAccountsList(page);
  await accountRow(page, name).getByRole('link', { name, exact: true }).click();
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
}

async function openSettings(page: Page): Promise<void> {
  await page.goto('/settings');
  await expect(page.getByRole('heading', { name: 'Limity wpłat' })).toBeVisible();
}

async function saveYear(page: Page, year: string): Promise<void> {
  const saved = page.waitForResponse(
    (response) =>
      response.request().method() === 'PUT' &&
      response.url().endsWith(`/api/portfolio/contribution-limits/${year}`) &&
      response.ok(),
  );
  await limitRow(page, year).getByTestId('save-year').click();
  await saved;
}

async function deposit(page: Page, value: string, date: string): Promise<void> {
  const dialog = page.getByRole('dialog');
  await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
  await dialog.getByLabel('Kwota').fill(value);
  await dialog.getByLabel('Data', { exact: true }).fill(date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

function dashboardRow(page: Page, name: string): Locator {
  return page
    .locator('.contribution-limits-card__row')
    .filter({ has: page.getByRole('link', { name, exact: true }) });
}

function warning(over: string): RegExp {
  return new RegExp(`Planowane wpłaty na ${IKE} w ${YEAR} przekroczą limit o ${pln(over).source}`);
}

test('limit IKE i IKZE: set once in Ustawienia and shown on accounts, Pulpit and the Planowanie warning', async ({
  signedInPage: page,
}) => {
  test.setTimeout(300_000);
  const bar = page.getByTestId('contribution-bar');
  const hint = page.getByRole('link', { name: `Nie ustawiono limitu na ${YEAR}` });
  const puts: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'PUT' && request.url().includes('/contribution-limits/')) {
      puts.push(request.url());
    }
  });

  await test.step('Krok 1: IKE Broker and IKZE Broker are on the list without a limit indicator', async () => {
    await wealthTab(page, 'Rachunki inwestycyjne');
    await createAccount(page, IKE, 'IKE');
    await createAccount(page, IKZE, 'IKZE');
    await expect(accountRow(page, IKE)).toBeVisible();
    await expect(accountRow(page, IKZE)).toBeVisible();
    await expect(bar).toHaveCount(0);
  });

  await test.step('Krok 2: IKE Broker detail has no "Limit wpłat" button, shows 0 zł paid and a hint to Ustawienia', async () => {
    await openAccount(page, IKE);
    await expect(page.getByRole('button', { name: 'Limit wpłat', exact: true })).toHaveCount(0);
    await expect(bar).toContainText(new RegExp(`Wpłacono\\s0,00\\szł\\sw\\s${YEAR}`));
    await expect(hint).toBeVisible();
  });

  await test.step('Krok 3: the hint opens Ustawienia with an empty "Limity wpłat" section', async () => {
    await hint.click();
    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('heading', { name: 'Limity wpłat' })).toBeVisible();
    await expect(page.getByText('Nie ustawiono jeszcze żadnych limitów.')).toBeVisible();
    await expect(page.getByTestId('contribution-limit-row')).toHaveCount(0);
  });

  await test.step('Krok 4: adding 2026 with IKE 28260 and IKZE 11304 shows both amounts', async () => {
    await page.getByTestId('add-year').click();
    const row = limitRow(page, YEAR);
    await expect(row).toBeVisible();
    await row.getByTestId('ike-amount').fill('28260');
    await row.getByTestId('ikze-amount').fill('11304');
    await saveYear(page, YEAR);
    await expect(row.getByTestId('ike-amount')).toHaveValue('28260');
    await expect(row.getByTestId('ikze-amount')).toHaveValue('11304');
  });

  await test.step('Krok 5: after a reload 28 260 zł and 11 304 zł are still there', async () => {
    await page.reload();
    const row = limitRow(page, YEAR);
    await expect(row.getByTestId('ike-amount')).toHaveValue('28260');
    await expect(row.getByTestId('ikze-amount')).toHaveValue('11304');
  });

  await test.step('Krok 6: IKE 0 and -5 show an error on the field and block the save, 28260 is restored', async () => {
    const row = limitRow(page, YEAR);
    const ike = row.getByTestId('ike-amount');
    const before = puts.length;
    for (const invalid of ['0', '-5']) {
      await ike.fill(invalid);
      await row.getByTestId('save-year').click();
      await expect(row.getByText('Podaj kwotę większą od 0.')).toBeVisible();
    }
    expect(puts.length).toBe(before);
    await ike.fill('28260');
    await saveYear(page, YEAR);
    await expect(row.getByText('Podaj kwotę większą od 0.')).toHaveCount(0);
  });

  await test.step('Krok 7: IKE Broker detail shows "wpłacono 0 z 28 260 zł" and no hint', async () => {
    await openAccount(page, IKE);
    await expect(bar).toContainText(paidOf('0', '28 260'));
    await expect(hint).toHaveCount(0);
  });

  await test.step('Krok 8: the list shows 0 of 28 260 zł on IKE Broker and 0 of 11 304 zł on IKZE Broker', async () => {
    await openAccountsList(page);
    await expect(accountRow(page, IKE).getByTestId('contribution-bar')).toContainText(
      paidOf('0', '28 260'),
    );
    await expect(accountRow(page, IKZE).getByTestId('contribution-bar')).toContainText(
      paidOf('0', '11 304'),
    );
  });

  await test.step('Krok 9: a 25000 zł deposit dated 2026-10-01 gives 25 000 of 28 260 zł in the calm state', async () => {
    await openAccount(page, IKE);
    await deposit(page, '25000', '2026-10-01');
    await expect(bar).toContainText(paidOf('25 000', '28 260'));
    await expect(bar).toHaveAttribute('data-state', 'calm');
  });

  await test.step('Krok 10: a further 1000 zł deposit dated 2026-10-15 gives 26 000 of 28 260 zł with a warning', async () => {
    await deposit(page, '1000', '2026-10-15');
    await expect(bar).toContainText(paidOf('26 000', '28 260'));
    await expect(bar).toHaveAttribute('data-state', 'warning');
  });

  await test.step('Krok 11: Pulpit card "Limity wpłat" shows 2 260 zł left on IKE Broker and 11 304 zł on IKZE Broker', async () => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', { name: 'Limity wpłat' })).toBeVisible();
    await expect(dashboardRow(page, IKE)).toContainText(
      new RegExp(`Pozostało\\s${pln('2 260,00').source}`),
    );
    await expect(dashboardRow(page, IKZE)).toContainText(
      new RegExp(`Pozostało\\s${pln('11 304,00').source}`),
    );
  });

  await test.step('Krok 12: a monthly 1500 zł income on IKE Broker warns that planned deposits exceed the limit by 740 zł', async () => {
    await openMonth(page, NOVEMBER);
    await addIncome(page, {
      name: INCOME,
      amount: '1500',
      account: IKE,
      start: `${NOVEMBER}-01`,
      interval: '1',
      day: 15,
    });
    await expect(page.getByTestId('contribution-limit-warning')).toHaveText(warning('740,00'));
  });

  await test.step('Krok 13: settling November at 1500 zł with a deposit gives 27 500 of 28 260 zł and the warning still says 740 zł', async () => {
    await openMonth(page, NOVEMBER);
    await settle(page, INCOME, '1500');
    await expect(page.getByTestId('contribution-limit-warning')).toHaveText(warning('740,00'));
    await openAccount(page, IKE);
    await expect(bar).toContainText(paidOf('27 500', '28 260'));
  });

  await test.step('Krok 14: skipping the December occurrence removes the warning', async () => {
    await openMonth(page, DECEMBER);
    const row = occurrences(page, INCOME);
    await row.getByTestId('skip-occurrence').click();
    await expect(status(row)).toHaveText('pominięte');
    await openMonth(page, NOVEMBER);
    await expect(page.getByTestId('contribution-limit-warning')).toHaveCount(0);
  });

  await test.step('Krok 15: a 1000 zł deposit dated 2026-11-05 gives 28 500 of 28 260 zł exceeded and Pulpit shows 240 zł over', async () => {
    await openAccount(page, IKE);
    await deposit(page, '1000', '2026-11-05');
    await expect(bar).toContainText(new RegExp(`Wpłacono\\s${amount('28 500')}(,00)?\\s/`));
    await expect(bar).toHaveAttribute('data-state', 'error');
    await page.goto('/dashboard');
    await expect(dashboardRow(page, IKE)).toContainText(
      new RegExp(`Przekroczono o\\s${pln('240,00').source}`),
    );
  });

  await test.step('Krok 16: a new year 2027 is prefilled with 28 260 and 11 304 and disappears after a reload when not saved', async () => {
    await openSettings(page);
    await page.getByTestId('add-year').click();
    const row = limitRow(page, '2027');
    await expect(row.getByTestId('ike-amount')).toHaveValue('28260');
    await expect(row.getByTestId('ikze-amount')).toHaveValue('11304');
    await page.reload();
    await expect(limitRow(page, YEAR)).toBeVisible();
    await expect(limitRow(page, '2027')).toHaveCount(0);
  });

  await test.step('Krok 17: adding 2027 again and confirming lists it with both amounts', async () => {
    await page.getByTestId('add-year').click();
    const row = limitRow(page, '2027');
    await saveYear(page, '2027');
    await expect(row.getByTestId('ike-amount')).toHaveValue('28260');
    await expect(row.getByTestId('ikze-amount')).toHaveValue('11304');
    await page.reload();
    await expect(limitRow(page, '2027').getByTestId('ike-amount')).toHaveValue('28260');
    await expect(limitRow(page, '2027').getByTestId('ikze-amount')).toHaveValue('11304');
  });

  await test.step('Krok 18: removing 2026 removes the year from the list', async () => {
    const removed = page.waitForResponse(
      (response) =>
        response.request().method() === 'DELETE' &&
        response.url().endsWith(`/api/portfolio/contribution-limits/${YEAR}`),
    );
    await limitRow(page, YEAR).getByTestId('remove-year').click();
    await removed;
    await expect(limitRow(page, YEAR)).toHaveCount(0);
    await expect(limitRow(page, '2027')).toBeVisible();
  });

  await test.step('Krok 19: IKE Broker 2026 shows the no-limit hint, the list has no indicator and Pulpit has no card', async () => {
    await openAccount(page, IKE);
    await expect(hint).toBeVisible();
    await expect(bar).toContainText(
      new RegExp(`Wpłacono\\s${amount('28 500')}(,00)?\\szł\\sw\\s${YEAR}`),
    );
    await expect(bar).not.toContainText('/');
    await openAccountsList(page);
    await expect(page.getByTestId('contribution-bar')).toHaveCount(0);
    await page.goto('/dashboard');
    await expect(page.getByTestId('month-card-actual')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Limity wpłat' })).toHaveCount(0);
  });
});
