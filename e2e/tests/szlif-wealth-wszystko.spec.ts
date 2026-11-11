import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
  wealthTab,
} from './fixtures';
import { choose } from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_STOCK = 2;
const TYPE_BUY = 0;
const TERM_UNIT_MONTHS = 1;
const CAPITALIZATION_AT_MATURITY = 0;

const NOTE = 'bez kont poza majątkiem';
const BANK = 'Konta bankowe';
const STOCKS = 'Akcje i ETF';

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function bankTab(page: Page): Locator {
  return page.getByRole('tab', { name: /^Konta bankowe(\s|$)/ });
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

async function openRowMenu(page: Page, name: string, item: string): Promise<void> {
  await accountRow(page, name)
    .getByRole('button', { name: `Akcje dla: ${name}`, exact: true })
    .click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

async function addBankAccount(
  page: Page,
  account: { name: string; outside: boolean; deposit: string },
): Promise<void> {
  await page.getByRole('button', { name: 'Dodaj konto', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(account.name);
  await choose(page, dialog.getByRole('combobox', { name: 'Waluta' }), /^PLN/);
  await choose(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Bieżące');
  await dialog.getByLabel('Roczne oprocentowanie (%)').fill('0');
  if (account.outside) {
    const flag = dialog.getByRole('switch', { name: /Poza majątkiem/ });
    await flag.click();
    await expect(flag).toBeChecked();
  }
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
  await expect(accountRow(page, account.name)).toBeVisible();

  await openRowMenu(page, account.name, 'Wpłata');
  const deposit = page.getByRole('dialog');
  await deposit.getByLabel('Kwota').fill(account.deposit);
  await deposit.getByRole('button', { name: 'Zapisz' }).click();
  await expect(deposit).toBeHidden();
}

function section(page: Page, name: string): Locator {
  return page.locator('tbody[data-section]').filter({
    has: page.locator('.wealth-all__section-name', { hasText: new RegExp(`^${name}$`) }),
  });
}

function sectionHeader(page: Page, name: string): Locator {
  return section(page, name).locator('tr.wealth-all__section');
}

function row(page: Page, name: string | RegExp): Locator {
  return page
    .locator('tr.wealth-all__row')
    .filter({ has: page.locator('.wealth-all__name', { hasText: name }) });
}

async function refetchWealth(page: Page): Promise<void> {
  await page.goto('/wealth');
  await expect(page.getByRole('heading', { name: 'Majątek', exact: true })).toBeVisible();
  await expect(page.getByTestId('total')).toBeVisible();
}

async function expandAll(page: Page): Promise<void> {
  await expect(page.locator('tbody[data-section]').first()).toBeVisible();
  const toggles = page.locator('.wealth-all__toggle');
  const count = await toggles.count();
  for (let index = 0; index < count; index += 1) {
    const toggle = toggles.nth(index);
    if ((await toggle.getAttribute('aria-expanded')) === 'false') {
      await toggle.click();
      await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    }
  }
}

async function box(locator: Locator): Promise<{ x: number; y: number; w: number; h: number }> {
  const found = await locator.boundingBox();
  expect(found, 'bounding box').not.toBeNull();
  const value = found as { x: number; y: number; width: number; height: number };
  return { x: value.x, y: value.y, w: value.width, h: value.height };
}

function near(actual: number, expected: number, message: string): void {
  expect(Math.abs(actual - expected), message).toBeLessThanOrEqual(1);
}

interface TextRect {
  left: number;
  right: number;
  top: number;
  bottom: number;
}

async function textRect(locator: Locator): Promise<TextRect> {
  return locator.evaluate((element) => {
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    let node = walker.nextNode();
    while (
      node &&
      (!(node.textContent ?? '').trim() || node.parentElement?.closest('mat-icon') != null)
    ) {
      node = walker.nextNode();
    }
    if (!node) {
      throw new Error('no text node');
    }
    const text = node.textContent ?? '';
    const start = text.length - text.trimStart().length;
    const range = document.createRange();
    range.setStart(node, start);
    range.setEnd(node, start + text.trim().length);
    const rect = range.getBoundingClientRect();
    return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
  });
}

async function typography(locator: Locator): Promise<Record<string, string>> {
  return locator.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      fontFamily: style.fontFamily,
      fontSize: style.fontSize,
      fontWeight: style.fontWeight,
      color: style.color,
    };
  });
}

async function frame(locator: Locator): Promise<Record<string, string>> {
  return locator.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      borderTopWidth: style.borderTopWidth,
      borderTopColor: style.borderTopColor,
      borderTopLeftRadius: style.borderTopLeftRadius,
      borderBottomRightRadius: style.borderBottomRightRadius,
    };
  });
}

async function createAsset(
  request: APIRequestContext,
  token: string,
  accountId: string,
): Promise<void> {
  const search = await request.get('/api/marketdata/instruments/search?q=CDR&assetClass=Stock', {
    headers: bearer(token),
  });
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === 'CDR.WA');
  expect(found?.id).toBeTruthy();
  await expect(async () => {
    const created = await request.post('/api/portfolio/assets', {
      headers: bearer(token),
      data: {
        accountId,
        assetClass: ASSET_CLASS_STOCK,
        name: 'CDR.WA',
        currency: 'PLN',
        instrumentId: found?.id,
        initialTransaction: {
          type: TYPE_BUY,
          quantity: 10,
          unitPrice: 250,
          date: warsawDate(),
        },
      },
    });
    expect(created.ok(), await created.text()).toBeTruthy();
  }).toPass({ timeout: 90_000, intervals: [2_000] });
}

test('szlif /wealth: bez dopisku, równe wiersze i spójne pole Szukaj', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(600_000);

  const token = await logInThroughApi(request, user);
  await page.goto('/login');
  await logInThroughUi(page, user);
  const headers = bearer(token);

  await test.step('Krok 1: Konto A i Konto B z saldem 5 000,00 zł są na liście kont bankowych', async () => {
    await wealthTab(page, BANK);
    await addBankAccount(page, { name: 'Konto A', outside: false, deposit: '5000' });
    await addBankAccount(page, { name: 'Konto B', outside: false, deposit: '5000' });
    await expect(accountRow(page, 'Konto A')).toContainText(pln('5 000,00'));
    await expect(accountRow(page, 'Konto B')).toContainText(pln('5 000,00'));
  });

  await test.step('Krok 2: Konto C z saldem 2 000,00 zł poza majątkiem ma etykietę Poza majątkiem', async () => {
    await addBankAccount(page, { name: 'Konto C', outside: true, deposit: '2000' });
    await expect(accountRow(page, 'Konto C').locator('.mat-column-status')).toContainText(
      'Poza majątkiem',
    );
  });

  await test.step('Krok 3: podpis zakładki kont bankowych to 10 000,00 zł, nic pod sumą i brak dopisku', async () => {
    await expectProjected(
      () => page.goto('/wealth'),
      async () => {
        await expect(bankTab(page)).toContainText(pln('10 000,00'), { timeout: 2_000 });
      },
    );
    await expect(bankTab(page)).not.toContainText(NOTE);
    await expect(bankTab(page).locator('.wealth__tab-caption')).toHaveCount(1);
    await expect(page.getByText(NOTE)).toHaveCount(0);
    await wealthTab(page, BANK);
    await expect(page).toHaveURL(/\/wealth\/bank$/);
    await expect(page.getByText(NOTE)).toHaveCount(0);
  });

  await test.step('Krok 4: zakup 10 szt. CDR.WA po 250,00 zł z dzisiejszą datą pojawia się w majątku', async () => {
    const broker = await createAccountThroughApi(request, token, {
      name: 'Broker',
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_REGULAR,
    });
    await depositCashThroughApi(request, token, broker.cashAssetId, {
      quantity: 3000,
      date: warsawDate(1),
    });
    await createAsset(request, token, broker.id);
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Ustawienia', exact: true })
      .click();
    const sync = page.getByRole('button', { name: 'Synchronizuj teraz' });
    await sync.click();
    await expect(sync).toBeEnabled();
    await wealthTab(page, STOCKS);
    await expect(page.getByRole('row').filter({ hasText: 'CDR.WA' })).toBeVisible();
  });

  await test.step('Krok 5: gotówka 1 000,00 zł pojawia się w majątku', async () => {
    const place = await request.post('/api/portfolio/cash-places', {
      headers,
      data: { name: 'Portfel', currencies: ['PLN'] },
    });
    expect(place.ok()).toBeTruthy();
    const cashAssetId = ((await place.json()) as { balances: { cashAssetId: string }[] })
      .balances[0]?.cashAssetId as string;
    await depositCashThroughApi(request, token, cashAssetId, {
      quantity: 1000,
      date: warsawDate(),
    });
    await wealthTab(page, 'Gotówka');
    await expect(page.getByRole('heading', { name: 'Portfel', exact: true })).toBeVisible();
  });

  await test.step('Krok 6: lokata 3 000,00 zł pojawia się w majątku z podpisem pod nazwą', async () => {
    const source = await createAccountThroughApi(request, token, {
      name: 'Źródło lokaty',
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      currency: 'PLN',
      variant: 0,
      annualInterestRatePercent: 0,
      outsideNetWorth: true,
    } as Parameters<typeof createAccountThroughApi>[2]);
    await depositCashThroughApi(request, token, source.cashAssetId, {
      quantity: 3000,
      date: warsawDate(1),
    });
    const created = await request.post('/api/portfolio/deposits', {
      headers,
      data: {
        name: 'Lokata szlif',
        sourceAccountId: source.id,
        principal: 3000,
        startDate: warsawDate(),
        termLength: 12,
        termUnit: TERM_UNIT_MONTHS,
        annualInterestRatePercent: 6,
        capitalization: CAPITALIZATION_AT_MATURITY,
      },
    });
    expect(created.ok(), await created.text()).toBeTruthy();
    await wealthTab(page, 'Lokaty');
    await expect(page.getByRole('row').filter({ hasText: 'Lokata szlif' })).toBeVisible();
  });

  await test.step('Krok 7: w widoku Wszystko tylko CDR.WA ma pod kwotą linię zysku/straty', async () => {
    await expectProjected(
      async () => {
        await refetchWealth(page);
        await expandAll(page);
      },
      async () => {
        await expect(row(page, 'CDR').locator('.wealth-all__change')).toContainText(/zł/, {
          timeout: 2_000,
        });
      },
    );
    await expect(row(page, 'CDR').locator('.wealth-all__num app-money')).toContainText(
      pln('1 000,00'),
    );
    await expect(row(page, 'CDR').locator('.wealth-all__change')).toContainText(
      /[-−]1\s?500,00\szł/,
    );
    for (const name of ['Konto A', 'Konto B', 'Portfel', 'Lokata szlif']) {
      await expect(row(page, name).locator('.wealth-all__num app-money')).toContainText(/zł/);
      await expect(row(page, name).locator('.wealth-all__change')).toHaveText('');
    }
    await expect(row(page, 'Lokata szlif').locator('.wealth-all__subtitle')).toBeVisible();
    await expect(page.locator('.wealth-all__name', { hasText: 'Konto C' })).toHaveCount(0);
  });

  await test.step('Krok 8: wiersz Konto A jest tak samo wysoki jak CDR.WA, a kwota stoi na tej samej wysokości', async () => {
    const account = row(page, 'Konto A');
    const stock = row(page, 'CDR');
    near((await box(account)).h, (await box(stock)).h, 'row height');
    const accountMoney = await box(account.locator('.wealth-all__num app-money'));
    const stockMoney = await box(stock.locator('.wealth-all__num app-money'));
    near(
      accountMoney.y - (await box(account)).y,
      stockMoney.y - (await box(stock)).y,
      'amount offset in the row',
    );
  });

  await test.step('Krok 9: pod kwotą wiersza Konto A jest puste miejsce bez żadnego znaku', async () => {
    const change = row(page, 'Konto A').locator('.wealth-all__change');
    await expect(change).toHaveText('');
    await expect(change).toHaveAttribute('aria-hidden', 'true');
    expect((await box(change)).h).toBeGreaterThan(0);
  });

  await test.step('Krok 10: wiersze gotówki i lokaty mają wysokość wiersza CDR.WA', async () => {
    const stockHeight = (await box(row(page, 'CDR'))).h;
    near((await box(row(page, 'Portfel'))).h, stockHeight, 'cash row');
    near((await box(row(page, 'Lokata szlif'))).h, stockHeight, 'deposit row');
  });

  await test.step('Krok 11: nagłówek sekcji kont bankowych ma wysokość nagłówka sekcji akcji', async () => {
    near(
      (await box(sectionHeader(page, BANK))).h,
      (await box(sectionHeader(page, STOCKS))).h,
      'section header',
    );
  });

  await test.step('Krok 12: lupa i napis Szukaj leżą w jednej linii, odstęp jak w przycisku Nowe aktywo', async () => {
    const field = page.locator('.wealth-all__search');
    const icon = field.locator('mat-icon');
    const label = field.locator('.mdc-floating-label');
    const iconBox = await box(icon);
    const labelText = await textRect(label);
    near(iconBox.y + iconBox.h / 2, (labelText.top + labelText.bottom) / 2, 'vertical centres');

    const button = page.getByRole('button', { name: 'Nowe aktywo' });
    const buttonIcon = await box(button.locator('mat-icon'));
    const buttonText = await textRect(button);
    near(
      labelText.left - (iconBox.x + iconBox.w),
      buttonText.left - (buttonIcon.x + buttonIcon.w),
      'icon to text gap',
    );
  });

  await test.step('Krok 13: pole Szukaj ma wysokość, obramowanie i typografię pola Grupa', async () => {
    const search = page.locator('.wealth-all__search');
    const group = page.locator('.wealth-all__group');
    near((await box(search)).h, (await box(group)).h, 'field height');
    near(
      (await box(search.locator('.mat-mdc-text-field-wrapper'))).h,
      (await box(group.locator('.mat-mdc-text-field-wrapper'))).h,
      'wrapper height',
    );
    expect(await frame(search.locator('.mdc-notched-outline__leading'))).toEqual(
      await frame(group.locator('.mdc-notched-outline__leading')),
    );
    expect(await frame(search.locator('.mdc-notched-outline__trailing'))).toEqual(
      await frame(group.locator('.mdc-notched-outline__trailing')),
    );
    const value = await typography(group.locator('.mat-mdc-select-value-text'));
    expect(await typography(search.locator('.mdc-floating-label'))).toEqual(value);
    expect(await typography(search.locator('input'))).toEqual(value);
  });

  await test.step('Krok 14: kliknięcie w pole Szukaj unosi podpis nad pole', async () => {
    const search = page.locator('.wealth-all__search');
    await page.getByLabel('Szukaj').click();
    const label = search.locator('.mdc-floating-label');
    await expect(label).toHaveClass(/mdc-floating-label--float-above/);
    await expect(async () => {
      const input = await box(search.locator('input'));
      const raised = await box(label);
      expect(raised.y + raised.h / 2).toBeLessThan(input.y);
    }).toPass({ timeout: 30_000 });
  });

  await test.step('Krok 15: wpisanie „Konto” zostawia Konto A i Konto B, tekst wygląda jak w polu Grupa', async () => {
    await page.getByLabel('Szukaj').fill('Konto');
    await expect(page.locator('tbody[data-section]')).toHaveCount(1);
    await expect(page.locator('.wealth-all__name')).toHaveCount(2);
    expect((await page.locator('.wealth-all__name').allTextContents()).sort()).toEqual([
      'Konto A',
      'Konto B',
    ]);
    expect(await typography(page.locator('.wealth-all__search input'))).toEqual(
      await typography(page.locator('.wealth-all__group .mat-mdc-select-value-text')),
    );
  });

  await test.step('Krok 16: po wyczyszczeniu pola i oknie 400 px wiersze mają równą wysokość, a zysk CDR.WA stoi pod kwotą', async () => {
    await page.getByLabel('Szukaj').fill('');
    await expect(page.locator('tbody[data-section]')).toHaveCount(5);
    await page.setViewportSize({ width: 400, height: 900 });
    await expandAll(page);
    const stock = row(page, 'CDR');
    const stockHeight = (await box(stock)).h;
    for (const name of ['Konto A', 'Konto B', 'Portfel', 'Lokata szlif']) {
      near((await box(row(page, name))).h, stockHeight, `row ${name} at 400 px`);
    }
    const amount = await box(stock.locator('.wealth-all__num app-money'));
    const gain = await box(stock.locator('.wealth-all__change'));
    expect(gain.y).toBeGreaterThanOrEqual(amount.y + amount.h - 1);
  });
});
