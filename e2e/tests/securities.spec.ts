import type { Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
} from './fixtures';

// pl-PL groups only numbers from 10 000 up, with a space or a non-breaking space, so the pattern accepts either.
function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

const ACCOUNT = 'XTB';
const ACCOUNT_TYPE_BROKERAGE = 0;
const WRAPPER_REGULAR = 0;

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function holdings(page: Page): Locator {
  return page.locator('app-securities-table').first();
}

function watchlist(page: Page): Locator {
  return page.locator('app-watchlist-table').first();
}

function row(table: Locator, ticker: string): Locator {
  return table.getByRole('row').filter({ hasText: ticker });
}

async function openSecurities(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Akcje i ETF', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Akcje i ETF', level: 1 })).toBeVisible();
}

async function expectAccountCash(page: Page, amount: string): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Rachunki inwestycyjne', exact: true }).click();
  await page.getByRole('link', { name: ACCOUNT, exact: true }).click();
  await expect(page.getByTestId('account-cash')).toContainText(pln(amount));
}

async function pickInstrument(page: Page, dialog: Locator, query: string, ticker: string) {
  await dialog.getByLabel('Instrument finansowy').fill(query);
  await page.getByRole('option', { name: new RegExp(ticker.replace('.', '\\.')) }).click();
  await expect(dialog.getByText(`Zweryfikowano: ${ticker}`)).toBeVisible();
}

async function syncNow(page: Page): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
  const button = page.getByRole('button', { name: 'Synchronizuj teraz' });
  await button.click();
  await expect(button).toBeEnabled();
}

async function fillTrade(dialog: Locator, quantity: string, unitPrice: string): Promise<void> {
  await dialog.getByLabel(/^Ilość/).fill(quantity);
  await dialog.getByLabel(/^Cena jednostkowa/).fill(unitPrice);
}

test('securities: buy and sell a PLN stock and buy from the watchlist on /securities', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(300_000);

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

  await page.goto('/login');
  await logInThroughUi(page, user);
  await openSecurities(page);

  await test.step('Krok 1: the add dialog offers CDR.WA for "CDR" on the XTB account', async () => {
    await holdings(page).getByRole('button', { name: 'Dodaj' }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('combobox', { name: 'Rachunek' }).click();
    await page.getByRole('option', { name: ACCOUNT }).click();
    await pickInstrument(page, dialog, 'CDR', 'CDR.WA');
  });

  await test.step('Krok 2: buying 10 at 100,00 zł without a fee offers no payment currency and CDR.WA appears with quantity 10', async () => {
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await fillTrade(dialog, '10', '100');
    await expect(dialog.getByLabel('Waluta zapłaty')).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await expect(row(holdings(page), 'CDR.WA').locator('.mat-column-quantity')).toContainText('10');
  });

  await test.step('Krok 3: the XTB account details show the cash 4 000,00 zł', async () => {
    await expectAccountCash(page, '4 000,00');
  });

  await test.step('Krok 4: after the sync CDR.WA shows price 100,00 zł, value 1 000,00 zł and gain 0,00 zł', async () => {
    await syncNow(page);
    await expect(async () => {
      await openSecurities(page);
      const cdr = row(holdings(page), 'CDR.WA');
      await expect(cdr.locator('.mat-column-price')).toContainText(pln('100,00'), {
        timeout: 3_000,
      });
      await expect(cdr.locator('.mat-column-value')).toContainText(pln('1 000,00'), {
        timeout: 3_000,
      });
      await expect(cdr.getByTestId('gain')).toContainText(pln('0,00'), { timeout: 3_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 3_000, 5_000] });
  });

  await test.step('Krok 5: selling 4 at 100,00 zł leaves CDR.WA with quantity 6 and value 600,00 zł', async () => {
    await row(holdings(page), 'CDR.WA')
      .getByRole('button', { name: /^Akcje dla:/ })
      .click();
    await page.getByRole('menuitem', { name: 'Sprzedaj' }).click();
    const dialog = page.getByRole('dialog');
    await fillTrade(dialog, '4', '100');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    const cdr = row(holdings(page), 'CDR.WA');
    await expect(cdr.locator('.mat-column-quantity')).toContainText('6');
    await expect(cdr.locator('.mat-column-value')).toContainText(pln('600,00'));
  });

  await test.step('Krok 6: the XTB account details show the cash 4 400,00 zł', async () => {
    await expectAccountCash(page, '4 400,00');
  });

  await test.step('Krok 7: selling 7 of 6 shows an error, the dialog stays open and nothing changes', async () => {
    await openSecurities(page);
    await row(holdings(page), 'CDR.WA')
      .getByRole('button', { name: /^Akcje dla:/ })
      .click();
    await page.getByRole('menuitem', { name: 'Sprzedaj' }).click();
    const dialog = page.getByRole('dialog');
    await fillTrade(dialog, '7', '100');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByRole('alert')).toBeVisible();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();

    await expect(row(holdings(page), 'CDR.WA').locator('.mat-column-quantity')).toContainText('6');
    await expectAccountCash(page, '4 400,00');
  });

  await test.step('Krok 8: watching PKO.WA puts it on the watchlist', async () => {
    await openSecurities(page);
    await watchlist(page).getByRole('button', { name: 'Obserwuj' }).first().click();
    const dialog = page.getByRole('dialog');
    await pickInstrument(page, dialog, 'PKO', 'PKO.WA');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await expect(row(watchlist(page), 'PKO.WA')).toBeVisible();
  });

  await test.step('Krok 9: after the sync PKO.WA on the watchlist shows the price 50,00 zł', async () => {
    await syncNow(page);
    await expect(async () => {
      await openSecurities(page);
      await expect(row(watchlist(page), 'PKO.WA')).toContainText(pln('50,00'), {
        timeout: 3_000,
      });
    }).toPass({ timeout: 90_000, intervals: [2_000, 3_000, 5_000] });
  });

  await test.step('Krok 10: buying 20 PKO.WA at 50,00 zł from the watchlist on XTB gives quantity 20 and value 1 000,00 zł', async () => {
    await row(watchlist(page), 'PKO.WA')
      .getByRole('button', { name: /^Akcje dla:/ })
      .click();
    await page.getByRole('menuitem', { name: 'Kup' }).click();

    const pick = page.getByRole('dialog');
    await pick.getByRole('radio', { name: new RegExp(ACCOUNT) }).check();
    await pick.getByTestId('buy-confirm').click();

    const assetDialog = page.getByRole('dialog', { name: 'Nowe aktywo' });
    await expect(assetDialog).toBeVisible();
    await assetDialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(assetDialog).toBeHidden();

    const dialog = page.getByRole('dialog', { name: 'Nowa transakcja' });
    await expect(dialog).toBeVisible();
    await fillTrade(dialog, '20', '50');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    const pko = row(holdings(page), 'PKO.WA');
    await expect(pko.locator('.mat-column-quantity')).toContainText('20');
    await expect(pko.locator('.mat-column-value')).toContainText(pln('1 000,00'));
  });

  await test.step('Krok 11: the XTB account details show the cash 3 400,00 zł', async () => {
    await expectAccountCash(page, '3 400,00');
  });
});
