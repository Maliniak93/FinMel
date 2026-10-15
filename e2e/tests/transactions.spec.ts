import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
} from './fixtures';

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

const ACCOUNT = 'XTB';
const TICKER = 'CDR.WA';
const ACCOUNT_TYPE_BROKERAGE = 0;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_STOCK = 2;
const TYPE_BUY = 0;

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function holdings(page: Page): Locator {
  return page.locator('app-securities-table').first();
}

function transactionRows(page: Page, type: string): Locator {
  return page
    .getByRole('row')
    .filter({ has: page.getByRole('button', { name: new RegExp(`^Akcje dla: ${type} z dnia`) }) });
}

async function createStockThroughApi(
  request: APIRequestContext,
  token: string,
  accountId: string,
): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const search = await request.get('/api/marketdata/instruments/search?q=CDR&assetClass=Stock', {
    headers,
  });
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === TICKER);
  expect(found?.id).toBeTruthy();

  const created = await request.post('/api/portfolio/assets', {
    headers,
    data: {
      accountId,
      assetClass: ASSET_CLASS_STOCK,
      name: TICKER,
      currency: 'PLN',
      instrumentId: found?.id,
      initialTransaction: { type: TYPE_BUY, quantity: 10, unitPrice: 100, date: warsawDate() },
    },
  });
  expect(created.ok()).toBeTruthy();
}

async function expectAccountCash(page: Page, amount: string): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Rachunki', exact: true }).click();
  await page.getByRole('link', { name: ACCOUNT, exact: true }).click();
  await expect(page.getByTestId('account-cash')).toContainText(pln(amount));
}

async function openTransactions(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Akcje i ETF', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Akcje i ETF', level: 1 })).toBeVisible();
  await holdings(page)
    .getByRole('row')
    .filter({ hasText: TICKER })
    .getByRole('button', { name: /^Akcje dla:/ })
    .click();
  await page.getByRole('menuitem', { name: 'Transakcje', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: `Transakcje — ${TICKER}`, level: 1 }),
  ).toBeVisible();
}

function quantity(page: Page, value: string): Locator {
  return page
    .locator('.transactions-page__summary')
    .filter({ hasText: new RegExp(`Ilość\\s+${value}\\b`) });
}

test('transactions: edit, add and delete the transactions of a PLN stock with the quantity, value and account cash recalculated', async ({
  page,
  request,
  user,
}) => {
  const token = await logInThroughApi(request, user);
  const account = await createAccountThroughApi(request, token, {
    name: ACCOUNT,
    type: ACCOUNT_TYPE_BROKERAGE,
    wrapper: WRAPPER_REGULAR,
  });
  await depositCashThroughApi(request, token, account.cashAssetId, {
    quantity: 5000,
    date: warsawDate(),
  });
  await createStockThroughApi(request, token, account.id);

  await page.goto('/login');
  await logInThroughUi(page, user);

  const dialog = page.getByRole('dialog');
  const buy = transactionRows(page, 'Kupno');
  const sell = transactionRows(page, 'Sprzedaż');

  async function openRowMenu(row: Locator, item: string): Promise<void> {
    await row.getByRole('button', { name: /^Akcje dla:/ }).click();
    await page.getByRole('menuitem', { name: item, exact: true }).click();
  }

  await test.step('Krok 1: the CDR.WA transactions page shows quantity 10 and one Buy worth 1 000,00 zł', async () => {
    await openTransactions(page);
    await expect(quantity(page, '10')).toBeVisible();
    await expect(buy).toHaveCount(1);
    await expect(buy).toContainText(pln('1 000,00'));
  });

  await test.step('Krok 2: editing the Buy to quantity 12 gives the value 1 200,00 zł and quantity 12', async () => {
    await openRowMenu(buy, 'Edytuj');
    await dialog.getByLabel(/^Ilość/).fill('12');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(buy).toContainText(pln('1 200,00'));
    await expect(quantity(page, '12')).toBeVisible();
  });

  await test.step('Krok 3: the XTB account details show the cash 3 800,00 zł', async () => {
    await expectAccountCash(page, '3 800,00');
  });

  await test.step('Krok 4: selling 2 at 100,00 zł gives two transactions and quantity 10', async () => {
    await openTransactions(page);
    await page.getByRole('button', { name: 'Nowa transakcja' }).click();
    await dialog.getByRole('combobox', { name: 'Typ' }).click();
    await page.getByRole('option', { name: 'Sprzedaż', exact: true }).click();
    await dialog.getByLabel(/^Ilość/).fill('2');
    await dialog.getByLabel(/^Cena jednostkowa/).fill('100');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(
      page.getByRole('row').filter({ has: page.getByRole('button', { name: /^Akcje dla:/ }) }),
    ).toHaveCount(2);
    await expect(sell).toHaveCount(1);
    await expect(quantity(page, '10')).toBeVisible();
  });

  await test.step('Krok 5: the XTB account details show the cash 4 000,00 zł', async () => {
    await expectAccountCash(page, '4 000,00');
  });

  await test.step('Krok 6: cancelling the delete confirmation keeps the Sell and quantity 10', async () => {
    await openTransactions(page);
    await openRowMenu(sell, 'Usuń');
    await expect(dialog.getByRole('heading', { name: 'Usunąć tę transakcję?' })).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(sell).toHaveCount(1);
    await expect(quantity(page, '10')).toBeVisible();
  });

  await test.step('Krok 7: confirming the delete removes the Sell and gives quantity 12', async () => {
    await openRowMenu(sell, 'Usuń');
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(sell).toHaveCount(0);
    await expect(quantity(page, '12')).toBeVisible();
  });

  await test.step('Krok 8: the XTB account details show the cash 3 800,00 zł', async () => {
    await expectAccountCash(page, '3 800,00');
  });

  await test.step('Krok 9: editing the Buy to quantity 60 shows the insufficient funds error, the dialog stays open and the Buy stays 1 200,00 zł', async () => {
    await openTransactions(page);
    await openRowMenu(buy, 'Edytuj');
    await dialog.getByLabel(/^Ilość/).fill('60');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByRole('alert')).toBeVisible();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(buy).toContainText(pln('1 200,00'));
    await expect(quantity(page, '12')).toBeVisible();
  });
});
