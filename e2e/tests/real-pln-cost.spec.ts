import { expect, logInThroughUi, registerThroughUi, test, openSelect, wealthTab } from './fixtures';
import type { Locator, Page } from '@playwright/test';

// pl-PL groups only numbers from 10 000 up, with a space or a non-breaking space, and may use a true minus sign, so the patterns accept either.
function pln(amount: string): RegExp {
  const negative = amount.startsWith('-');
  const digits = (negative ? amount.slice(1) : amount).replace(/ /g, '\\s?');
  return new RegExp(`${negative ? '[-\\u2212]' : ''}${digits}\\szł`);
}

const ACCOUNT = 'XTB';
const VWCE_PATTERN = /VWCE\.DE|Vanguard FTSE All-World/;

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

async function openEtfRow(page: Page): Promise<Locator> {
  await wealthTab(page, 'Akcje i ETF');
  await page.getByRole('tab', { name: 'ETF', exact: true }).click();
  return page.getByRole('row').filter({ hasText: VWCE_PATTERN });
}

async function openVwceMenu(page: Page, item: string): Promise<void> {
  const row = await openEtfRow(page);
  await row.getByRole('button', { name: /^Akcje dla/ }).click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

async function openVwceTransactions(page: Page): Promise<void> {
  await openVwceMenu(page, 'Historia transakcji');
  await expect(page.getByRole('heading', { name: /Transakcje — / })).toBeVisible();
}

async function selectOption(page: Page, select: Locator, option: string): Promise<void> {
  await openSelect(select);
  await page.getByRole('option', { name: option, exact: true }).click();
}

async function expectAccountCash(page: Page, amount: string): Promise<void> {
  await wealthTab(page, 'Rachunki inwestycyjne');
  await page.getByRole('link', { name: ACCOUNT, exact: true }).click();
  const cash = page
    .getByTestId('summary-strip')
    .locator('div')
    .filter({ has: page.locator('dt', { hasText: /^\s*Wolne środki\s*$/ }) })
    .locator('dd');
  await expect(cash).toContainText(pln(amount));
}

function transactionRow(page: Page, type: string, value: string): Locator {
  return page
    .getByRole('row')
    .filter({ hasText: type })
    .filter({ has: page.locator('.mat-column-value', { hasText: pln(value) }) });
}

async function editTransaction(page: Page, value: string): Promise<Locator> {
  await transactionRow(page, 'Kupno', value)
    .getByRole('button', { name: /^Akcje dla/ })
    .click();
  await page.getByRole('menuitem', { name: 'Edytuj', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj transakcję' })).toBeVisible();
  return dialog;
}

async function fillTrade(
  dialog: Locator,
  trade: { quantity: string; unitPrice: string; fee: string },
): Promise<void> {
  await dialog.getByLabel('Ilość').fill(trade.quantity);
  await dialog.getByLabel('Cena jednostkowa').fill(trade.unitPrice);
  await dialog.getByLabel('Prowizja (PLN)').fill(trade.fee);
}

test('real PLN cost: the PLN really paid drives cost basis, P/L and account cash', async ({
  page,
  user,
}) => {
  test.setTimeout(240_000);
  const sidebar = navigation(page);

  await test.step('Krok 1: register and log in, the dashboard is visible', async () => {
    await registerThroughUi(page, user);
    await logInThroughUi(page, user);
    await expect(page.getByRole('heading', { name: 'Pulpit' })).toBeVisible();
  });

  await test.step('Krok 2: new Brokerage / Regular account XTB with 0,00 PLN cash', async () => {
    await wealthTab(page, 'Rachunki inwestycyjne');
    await page.getByRole('button', { name: 'Utwórz pierwszy rachunek' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill(ACCOUNT);
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row', { name: new RegExp(ACCOUNT) })).toContainText(pln('0,00'));
  });

  await test.step('Krok 3: deposit 10 000,00 PLN, cash is 10 000,00 PLN', async () => {
    await page.getByRole('link', { name: ACCOUNT, exact: true }).click();
    await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Kwota').fill('10000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expectAccountCash(page, '10 000,00');
  });

  await test.step('Krok 4: add VWCE.DE with a first buy, the payment currency defaults to EUR', async () => {
    await wealthTab(page, 'Akcje i ETF');
    await page.getByRole('tab', { name: 'ETF', exact: true }).click();
    await page.getByRole('button', { name: 'Dodaj', exact: true }).first().click();
    const dialog = page.getByRole('dialog');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), ACCOUNT);
    await dialog.getByLabel('Instrument finansowy').fill('VWCE');
    await page.getByRole('option', { name: /VWCE\.DE/ }).click();
    await dialog.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await fillTrade(dialog, { quantity: '10', unitPrice: '100', fee: '5' });
    await expect(dialog.getByRole('combobox', { name: 'Waluta zapłaty' })).toContainText('EUR');
  });

  const dialog = page.getByRole('dialog');

  await test.step('Krok 5: paying in PLN requires an amount in PLN', async () => {
    await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta zapłaty' }), 'PLN');
    await expect(dialog.getByLabel('Kwota w PLN')).toBeVisible();
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText('Podaj zapłaconą kwotę w PLN.')).toBeVisible();
  });

  await test.step('Krok 6: 4 320,00 hints 432,00 PLN / szt. at 4,3200 and VWCE.DE is an ETF with quantity 10', async () => {
    await dialog.getByLabel('Kwota w PLN').fill('4320');
    await expect(dialog).toContainText('432,00 PLN / szt. · kurs 4,3200');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    const row = page.getByRole('row').filter({ hasText: VWCE_PATTERN });
    await expect(row.locator('.mat-column-quantity')).toHaveText(/^\s*10\s*$/);
  });

  await test.step('Krok 7: after a market data sync VWCE.DE shows the average price 100,12 EUR, PLN cost basis, value and P/L', async () => {
    await sidebar.getByRole('link', { name: 'Ustawienia' }).click();
    const sync = page.getByRole('button', { name: 'Synchronizuj teraz' });
    await sync.click();
    await expect(sync).toBeEnabled();

    await expect(async () => {
      await sidebar.getByRole('link', { name: 'Pulpit' }).click();
      const row = await openEtfRow(page);
      await expect(row.locator('.mat-column-averageBuyPrice')).toContainText(/100,12\sEUR/, {
        timeout: 3_000,
      });
      await expect(row.locator('.mat-column-averageBuyPricePln')).toContainText(pln('432,50'), {
        timeout: 3_000,
      });
      await expect(row.locator('.mat-column-value')).toContainText(pln('4 250,00'), {
        timeout: 3_000,
      });
      await expect(row.locator('.mat-column-gain')).toContainText(pln('-75,00'), {
        timeout: 3_000,
      });
    }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 3_000] });
  });

  await test.step('Krok 8: XTB cash is 5 675,00 PLN', async () => {
    await expectAccountCash(page, '5 675,00');
  });

  await test.step('Krok 9: VWCE.DE transactions list the buy at 4 320,00 with fee 5,00', async () => {
    await openVwceTransactions(page);
    const buy = transactionRow(page, 'Kupno', '4 320,00');
    await expect(buy).toBeVisible();
    await expect(buy.locator('.mat-column-fee')).toContainText(pln('5,00'));
  });

  await test.step('Krok 10: buy 10 more at 100,00 EUR paid in EUR, the cash hint shows only the balance', async () => {
    await openVwceMenu(page, 'Kup');
    await fillTrade(dialog, { quantity: '10', unitPrice: '100', fee: '5' });
    await expect(dialog.getByRole('combobox', { name: 'Waluta zapłaty' })).toContainText('EUR');
    await expect(dialog).toContainText(`Gotówka rachunku ${ACCOUNT} · saldo`);
    await expect(dialog).toContainText(pln('5 675,00'));
    await expect(dialog).not.toContainText('Z gotówki rachunku');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 11: second buy is valued 4 250,00 at the market rate, XTB cash is 1 420,00 PLN', async () => {
    await openVwceTransactions(page);
    await expect(transactionRow(page, 'Kupno', '4 250,00')).toBeVisible();
    await expectAccountCash(page, '1 420,00');
  });

  await test.step('Krok 12: VWCE.DE has quantity 20, average PLN price 429,00, value 8 500,00, P/L -80,00', async () => {
    const row = await openEtfRow(page);
    await expect(row.locator('.mat-column-quantity')).toHaveText(/^\s*20\s*$/);
    await expect(row.locator('.mat-column-averageBuyPricePln')).toContainText(pln('429,00'));
    await expect(row.locator('.mat-column-value')).toContainText(pln('8 500,00'));
    await expect(row.locator('.mat-column-gain')).toContainText(pln('-80,00'));
  });

  await test.step('Krok 13: sell 10 at 100,00 EUR for 4 200,00 PLN, hint 420,00 PLN / szt. at 4,2000, cash +4 195,00', async () => {
    await openVwceMenu(page, 'Sprzedaj');
    await fillTrade(dialog, { quantity: '10', unitPrice: '100', fee: '5' });
    await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta zapłaty' }), 'PLN');
    await dialog.getByLabel('Kwota w PLN').fill('4200');
    await expect(dialog).toContainText('420,00 PLN / szt. · kurs 4,2000');
    await expect(dialog).toContainText(`Na gotówkę rachunku ${ACCOUNT}`);
    await expect(dialog).toContainText(pln('4 195,00'));
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 14: XTB cash is 5 615,00 PLN', async () => {
    await expectAccountCash(page, '5 615,00');
  });

  await test.step('Krok 15: VWCE.DE transactions list the sale at 4 200,00', async () => {
    await openVwceTransactions(page);
    await expect(transactionRow(page, 'Sprzedaż', '4 200,00')).toBeVisible();
  });

  await test.step('Krok 16: VWCE.DE has quantity 10, average PLN price 429,00, value 4 250,00, P/L -40,00', async () => {
    const row = await openEtfRow(page);
    await expect(row.locator('.mat-column-quantity')).toHaveText(/^\s*10\s*$/);
    await expect(row.locator('.mat-column-averageBuyPricePln')).toContainText(pln('429,00'));
    await expect(row.locator('.mat-column-value')).toContainText(pln('4 250,00'));
    await expect(row.locator('.mat-column-gain')).toContainText(pln('-40,00'));
  });

  await test.step('Krok 17: editing the first buy opens with payment currency PLN and amount 4 320,00', async () => {
    await openVwceTransactions(page);
    const edit = await editTransaction(page, '4 320,00');
    await expect(edit.getByRole('combobox', { name: 'Waluta zapłaty' })).toContainText('PLN');
    await expect(edit.getByLabel('Kwota w PLN')).toHaveValue('4320');
  });

  await test.step('Krok 18: amount 4 400,00 hints 440,00 PLN / szt. at 4,4000, the buy is valued 4 400,00, cash is 5 535,00', async () => {
    await dialog.getByLabel('Kwota w PLN').fill('4400');
    await expect(dialog).toContainText('440,00 PLN / szt. · kurs 4,4000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(transactionRow(page, 'Kupno', '4 400,00')).toBeVisible();
    await expectAccountCash(page, '5 535,00');
  });

  await test.step('Krok 19: VWCE.DE average PLN price is 433,00 and P/L -80,00', async () => {
    const row = await openEtfRow(page);
    await expect(row.locator('.mat-column-averageBuyPricePln')).toContainText(pln('433,00'));
    await expect(row.locator('.mat-column-gain')).toContainText(pln('-80,00'));
  });

  await test.step('Krok 20: switching the first buy to EUR values it 4 250,00, cash is 5 685,00', async () => {
    await openVwceTransactions(page);
    const edit = await editTransaction(page, '4 400,00');
    await selectOption(page, edit.getByRole('combobox', { name: 'Waluta zapłaty' }), 'EUR');
    await edit.getByRole('button', { name: 'Zapisz' }).click();
    await expect(edit).toBeHidden();
    await expect(transactionRow(page, 'Kupno', '4 250,00')).toHaveCount(2);
    await expectAccountCash(page, '5 685,00');
  });

  await test.step('Krok 21: VWCE.DE average PLN price is 425,50 and P/L -5,00', async () => {
    const row = await openEtfRow(page);
    await expect(row.locator('.mat-column-averageBuyPricePln')).toContainText(pln('425,50'));
    await expect(row.locator('.mat-column-gain')).toContainText(pln('-5,00'));
  });

  await test.step('Krok 22: the dividend dialog has no payment currency choice', async () => {
    await openVwceMenu(page, 'Dywidenda');
    await expect(dialog.getByRole('heading', { name: 'Nowa transakcja' })).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Waluta zapłaty' })).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 23: a PLN instrument (CDR.WA) offers no payment currency choice', async () => {
    await wealthTab(page, 'Akcje i ETF');
    await page.getByRole('tab', { name: 'Akcje', exact: true }).click();
    await page.getByRole('button', { name: 'Dodaj', exact: true }).first().click();
    await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), ACCOUNT);
    await dialog.getByLabel('Instrument finansowy').fill('CDR');
    await page.getByRole('option', { name: /CDR\.WA/ }).click();
    await dialog.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await expect(dialog.getByLabel('Cena jednostkowa')).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Waluta zapłaty' })).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });
});
