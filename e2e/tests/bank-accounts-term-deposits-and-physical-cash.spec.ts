import {
  expect,
  logInThroughUi,
  registerThroughUi,
  test,
  openSelect,
  WEALTH_TABS,
  wealthTab,
} from './fixtures';
import type { Locator, Page } from '@playwright/test';

// pl-PL groups only numbers from 10 000 up, with a space or a non-breaking space, and may use a true minus sign, so the patterns accept either.
function money(amount: string, unit = 'zł'): RegExp {
  const negative = amount.startsWith('-');
  const digits = (negative ? amount.slice(1) : amount).replace(/ /g, '\\s?');
  return new RegExp(`${negative ? '[-\\u2212]' : ''}${digits}\\s${unit}`);
}

function pln(amount: string): RegExp {
  return money(amount);
}

function usd(amount: string): RegExp {
  return money(amount, 'USD');
}

const FIXED_DEPOSIT = 'Lokata mBank';
const METAL = 'Sztabka 1 g';

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

async function openPage(page: Page, link: string): Promise<void> {
  if (WEALTH_TABS.includes(link)) {
    await wealthTab(page, link);
    return;
  }
  await navigation(page).getByRole('link', { name: link, exact: true }).click();
}

async function selectOption(page: Page, select: Locator, option: string | RegExp): Promise<void> {
  await openSelect(select);
  await page
    .getByRole(
      'option',
      typeof option === 'string' ? { name: option, exact: true } : { name: option },
    )
    .click();
}

function bankRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

async function openRowMenu(page: Page, row: Locator, name: string, item: string): Promise<void> {
  await row.getByRole('button', { name: `Akcje dla: ${name}`, exact: true }).click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

async function addBankAccount(
  page: Page,
  account: { name: string; variant: string; currency: RegExp; bank?: string; rate: string },
): Promise<void> {
  await page.getByRole('button', { name: 'Dodaj konto', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(account.name);
  await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta' }), account.currency);
  await selectOption(page, dialog.getByRole('combobox', { name: 'Wariant' }), account.variant);
  if (account.bank) {
    await dialog.getByRole('textbox', { name: 'Bank', exact: true }).fill(account.bank);
  }
  await dialog.getByLabel('Roczne oprocentowanie (%)').fill(account.rate);
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
}

async function cashOperation(
  page: Page,
  dialog: Locator,
  operation: { amount: string; date?: string },
): Promise<void> {
  await dialog.getByLabel('Kwota').fill(operation.amount);
  if (operation.date) {
    await dialog.getByLabel('Data').fill(operation.date);
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function expectBankBalance(page: Page, name: string, amount: RegExp): Promise<void> {
  await openPage(page, 'Konta bankowe');
  await expect(bankRow(page, name).locator('.mat-column-balance')).toContainText(amount);
}

function cashPlace(page: Page, name: string): Locator {
  return page
    .locator('mat-card.cash-place')
    .filter({ has: page.getByRole('heading', { name, exact: true }) });
}

function placeBalance(page: Page, place: string, currency: string): Locator {
  return cashPlace(page, place).locator('.cash-place__balance').filter({ hasText: currency });
}

async function openPlaceMenu(
  page: Page,
  place: string,
  currency: string,
  item: string,
): Promise<void> {
  await placeBalance(page, place, currency)
    .getByRole('button', { name: `Akcje dla: ${place} · ${currency}`, exact: true })
    .click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

function depositRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ hasText: name });
}

function metalRow(page: Page): Locator {
  return page.getByRole('row').filter({ hasText: METAL });
}

async function openMetalDialog(page: Page, item: 'Kup' | 'Sprzedaj'): Promise<Locator> {
  await openPage(page, 'Metale szlachetne');
  await openRowMenu(page, metalRow(page), METAL, item);
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Sztuki')).toBeVisible();
  return dialog;
}

test('bank accounts, term deposits and physical cash: money moves only between accounts, deposits and cash places', async ({
  page,
  user,
}) => {
  test.setTimeout(420_000);
  const dialog = page.getByRole('dialog');

  await registerThroughUi(page, user);
  await logInThroughUi(page, user);

  await test.step('Krok 1: the menu lists Pulpit, Majątek, Planowanie, Grupy, Ustawienia in that order', async () => {
    await expect(navigation(page).locator('.shell__nav-label')).toHaveText([
      /^Pulpit$/,
      /^Majątek$/,
      /^Planowanie$/,
      /^Grupy$/,
      /^Ustawienia$/,
    ]);
  });

  await test.step('Krok 2: brokerage IKE account XTB IKE is on the list with 0,00 zł', async () => {
    await openPage(page, 'Rachunki inwestycyjne');
    await page
      .getByRole('button', { name: /^(Nowy rachunek|Utwórz pierwszy rachunek)$/ })
      .first()
      .click();
    await dialog.getByLabel('Nazwa').fill('XTB IKE');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Typ' }), 'Maklerski');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Opakowanie podatkowe' }), 'IKE');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row', { name: /XTB IKE/ })).toContainText(pln('0,00'));
  });

  await test.step('Krok 3: mBank (current, PLN, bank mBank, 0 %) is on the list with 0,00 zł and the form has no tax wrapper', async () => {
    await openPage(page, 'Konta bankowe');
    await page.getByRole('button', { name: 'Dodaj pierwsze konto bankowe' }).click();
    await expect(dialog.getByRole('combobox', { name: 'Waluta' })).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Wariant' })).toBeVisible();
    await expect(dialog.getByRole('textbox', { name: 'Bank', exact: true })).toBeVisible();
    await expect(dialog.getByLabel('Roczne oprocentowanie (%)')).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Opakowanie podatkowe' })).toHaveCount(0);
    await dialog.getByLabel('Nazwa').fill('mBank');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta' }), /^PLN/);
    await selectOption(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Bieżące');
    await dialog.getByRole('textbox', { name: 'Bank', exact: true }).fill('mBank');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('0');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(bankRow(page, 'mBank').locator('.mat-column-balance')).toContainText(pln('0,00'));
  });

  await test.step('Krok 4: Nest Oszczędnościowe (savings, PLN, Nest Bank, 5 %) is on the list', async () => {
    await addBankAccount(page, {
      name: 'Nest Oszczędnościowe',
      variant: 'Oszczędnościowe',
      currency: /^PLN/,
      bank: 'Nest Bank',
      rate: '5',
    });
    await expect(bankRow(page, 'Nest Oszczędnościowe')).toBeVisible();
  });

  await test.step('Krok 5: mBank USD (current, USD, 0 %) is on the list with currency USD', async () => {
    await addBankAccount(page, {
      name: 'mBank USD',
      variant: 'Bieżące',
      currency: /^USD/,
      rate: '0',
    });
    await expect(bankRow(page, 'mBank USD').locator('.mat-column-currency')).toHaveText('USD');
  });

  await test.step('Krok 6: the Oszczędnościowe filter shows only Nest Oszczędnościowe, Wszystkie shows three accounts', async () => {
    await page.getByRole('option', { name: 'Oszczędnościowe', exact: true }).click();
    const accountRows = page.getByRole('row').filter({ has: page.getByRole('link') });
    await expect(accountRows).toHaveCount(1);
    await expect(bankRow(page, 'Nest Oszczędnościowe')).toBeVisible();
    await page.getByRole('option', { name: 'Wszystkie', exact: true }).click();
    await expect(accountRows).toHaveCount(3);
  });

  await test.step('Krok 7: deposit 20 000 zł on 2026-07-01 into mBank, balance 20 000,00 zł', async () => {
    await openRowMenu(page, bankRow(page, 'mBank'), 'mBank', 'Wpłata');
    await cashOperation(page, dialog, { amount: '20000', date: '2026-07-01' });
    await expect(bankRow(page, 'mBank').locator('.mat-column-balance')).toContainText(
      pln('20 000,00'),
    );
  });

  await test.step('Krok 8: deposit 10 000 zł on 2026-08-01 into Nest Oszczędnościowe, balance 10 000,00 zł and the interest-due marker', async () => {
    const nest = bankRow(page, 'Nest Oszczędnościowe');
    await openRowMenu(page, nest, 'Nest Oszczędnościowe', 'Wpłata');
    await cashOperation(page, dialog, { amount: '10000', date: '2026-08-01' });
    await expect(nest.locator('.mat-column-balance')).toContainText(pln('10 000,00'));
    await expect(nest.locator('.mat-column-status')).toContainText('Odsetki do rozliczenia');
  });

  await test.step('Krok 9: deposit 100 USD with today date into mBank USD, balance 100,00 USD', async () => {
    const row = bankRow(page, 'mBank USD');
    await openRowMenu(page, row, 'mBank USD', 'Wpłata');
    await cashOperation(page, dialog, { amount: '100' });
    await expect(row.locator('.mat-column-balance')).toContainText(usd('100,00'));
  });

  const nest = bankRow(page, 'Nest Oszczędnościowe');

  async function settleAugust(): Promise<void> {
    await openRowMenu(page, nest, 'Nest Oszczędnościowe', 'Rozlicz odsetki');
    await expect(dialog.getByRole('heading', { name: 'Rozlicz odsetki' })).toBeVisible();
    await expect(dialog).toContainText(/sie\s2026/);
    await expect(dialog.getByLabel('Odsetki brutto')).toHaveValue('42.47');
    await expect(dialog.getByLabel('Podatek')).toHaveValue('8.07');
    await expect(dialog.locator('dl').last()).toContainText(pln('34,40'));
  }

  await test.step('Krok 10: settling interest shows August 2026 with gross 42,47 zł, tax 8,07 zł, net 34,40 zł; after approval balance 10 034,40 zł and interest credited 34,40 zł', async () => {
    await settleAugust();
    await dialog.getByRole('button', { name: 'Rozlicz', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(nest.locator('.mat-column-balance')).toContainText(pln('10 034,40'));
    await expect(nest.locator('.mat-column-interestCredited')).toContainText(pln('34,40'));
  });

  await test.step('Krok 11: undoing the last settlement returns balance 10 000,00 zł and interest credited 0,00 zł', async () => {
    await openRowMenu(page, nest, 'Nest Oszczędnościowe', 'Cofnij ostatnie rozliczenie');
    await dialog.getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(nest.locator('.mat-column-balance')).toContainText(pln('10 000,00'));
    await expect(nest.locator('.mat-column-interestCredited')).toContainText(pln('0,00'));
  });

  await test.step('Krok 12: settling again returns balance 10 034,40 zł and interest credited 34,40 zł', async () => {
    await settleAugust();
    await dialog.getByRole('button', { name: 'Rozlicz', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(nest.locator('.mat-column-balance')).toContainText(pln('10 034,40'));
    await expect(nest.locator('.mat-column-interestCredited')).toContainText(pln('34,40'));
  });

  await test.step('Krok 13: transfer 3 000 zł from the mBank page to XTB IKE offers Nest Oszczędnościowe and XTB IKE only; mBank 17 000,00 zł, XTB IKE 3 000,00 zł and "Wpłacono 3 000,00 zł" this year', async () => {
    await bankRow(page, 'mBank').getByRole('link', { name: 'mBank', exact: true }).click();
    await page.getByRole('button', { name: 'Przelew', exact: true }).click();
    await openSelect(dialog.getByRole('combobox', { name: 'Rachunek docelowy' }));
    await expect(page.getByRole('option', { name: /^Nest Oszczędnościowe/ })).toBeVisible();
    await expect(page.getByRole('option', { name: /^XTB IKE/ })).toBeVisible();
    await expect(page.getByRole('option', { name: /^mBank USD/ })).toHaveCount(0);
    await page.getByRole('option', { name: /^XTB IKE/ }).click();
    await dialog.getByLabel('Kwota').fill('3000');
    await dialog.getByRole('button', { name: 'Przelej' }).click();
    await expect(dialog).toBeHidden();
    await expect(
      page.getByTestId('summary-strip').locator('div').filter({ hasText: 'Saldo' }),
    ).toContainText(pln('17 000,00'));

    await openPage(page, 'Rachunki inwestycyjne');
    await expect(page.getByRole('row', { name: /XTB IKE/ })).toContainText(pln('3 000,00'));
    await page.getByRole('link', { name: 'XTB IKE', exact: true }).click();
    await expect(page.locator('.contribution-bar')).toContainText(/Wpłacono\s3\s?000,00\szł/);
  });

  await test.step('Krok 14: a new deposit from mBank has a read-only PLN currency, "Zwrot na" offers the source account and Nest Oszczędnościowe without mBank USD, and no tax-exemption switch', async () => {
    await openPage(page, 'Lokaty');
    await page
      .getByRole('button', { name: /^(Dodaj lokatę|Dodaj pierwszą lokatę)$/ })
      .first()
      .click();
    await selectOption(page, dialog.getByRole('combobox', { name: 'Z konta' }), /^mBank \(/);
    await expect(
      dialog.locator('.deposit-form-dialog__funding').filter({ hasText: 'Waluta' }),
    ).toContainText('PLN');
    await openSelect(dialog.getByRole('combobox', { name: 'Zwrot na' }));
    await expect(page.getByRole('option', { name: 'Konto źródłowe', exact: true })).toBeVisible();
    await expect(page.getByRole('option', { name: /^Nest Oszczędnościowe/ })).toBeVisible();
    await expect(page.getByRole('option', { name: /^mBank USD/ })).toHaveCount(0);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('listbox')).toBeHidden();
    await expect(dialog.getByRole('checkbox')).toHaveCount(0);
    await expect(dialog.getByRole('switch')).toHaveCount(0);
  });

  await test.step('Krok 15: deposit 10 000 zł from 2026-07-01 for 3 months at 6 % returning to Nest Oszczędnościowe lists source mBank, maturity 2026-10-01, net profit 122,49 zł, final amount 10 122,49 zł, status Zapadła; mBank 7 000,00 zł', async () => {
    await dialog.getByLabel('Nazwa').fill(FIXED_DEPOSIT);
    await selectOption(
      page,
      dialog.getByRole('combobox', { name: 'Zwrot na' }),
      /^Nest Oszczędnościowe/,
    );
    await dialog.getByLabel('Kapitał').fill('10000');
    await dialog.getByLabel('Data rozpoczęcia').fill('2026-07-01');
    await dialog.getByLabel('Okres', { exact: true }).fill('3');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('6');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    const row = depositRow(page, FIXED_DEPOSIT);
    await expect(row.locator('.mat-column-sourceAccount')).toHaveText(/^\s*mBank\s*$/);
    await expect(row.locator('.mat-column-returnAccount')).toContainText('Nest Oszczędnościowe');
    await expect(row.locator('.mat-column-maturityDate')).toContainText(/1\spaź\s2026/);
    await expect(row.locator('.mat-column-netInterest')).toContainText(pln('122,49'));
    await expect(row.locator('.mat-column-finalAmount')).toContainText(pln('10 122,49'));
    await expect(row.locator('.mat-column-status')).toContainText('Zapadła');
    await expectBankBalance(page, 'mBank', pln('7 000,00'));
  });

  await test.step('Krok 16: a new deposit of 8 000 zł from mBank with today date shows "Brak środków na koncie" and saves nothing', async () => {
    await openPage(page, 'Lokaty');
    await page.getByRole('button', { name: 'Dodaj lokatę', exact: true }).click();
    await dialog.getByLabel('Nazwa').fill('Lokata za duża');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Z konta' }), /^mBank \(/);
    await dialog.getByLabel('Kapitał').fill('8000');
    await dialog.getByLabel('Okres', { exact: true }).fill('3');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('6');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog.getByText('Brak środków na koncie')).toBeVisible();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(depositRow(page, 'Lokata za duża')).toHaveCount(0);
    await expect(depositRow(page, FIXED_DEPOSIT)).toHaveCount(1);
  });

  await test.step('Krok 17: settling the matured deposit with payout to Nest Oszczędnościowe gives status Wypłacona and Nest Oszczędnościowe 20 156,89 zł', async () => {
    await page
      .getByRole('button', { name: `Rozlicz zapadalność lokaty ${FIXED_DEPOSIT}`, exact: true })
      .click();
    await expect(dialog.getByLabel('Odsetki brutto')).not.toHaveValue('');
    await dialog.getByRole('checkbox', { name: 'Wypłać na konto Nest Oszczędnościowe' }).check();
    await dialog.getByRole('button', { name: 'Rozlicz', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(depositRow(page, FIXED_DEPOSIT).locator('.mat-column-status')).toContainText(
      'Wypłacona',
    );
    await expectBankBalance(page, 'Nest Oszczędnościowe', pln('20 156,89'));
  });

  await test.step('Krok 18: the cash place Sejf with PLN and USD shows balances 0,00 zł and 0,00 USD', async () => {
    await openPage(page, 'Gotówka');
    await page.getByRole('button', { name: 'Dodaj miejsce', exact: true }).first().click();
    await dialog.getByLabel('Nazwa').fill('Sejf');
    await dialog.getByRole('checkbox', { name: /^PLN/ }).setChecked(true);
    await dialog.getByRole('checkbox', { name: /^USD/ }).setChecked(true);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(placeBalance(page, 'Sejf', 'PLN')).toContainText(pln('0,00'));
    await expect(placeBalance(page, 'Sejf', 'USD')).toContainText(usd('0,00'));
  });

  await test.step('Krok 19: deposit 2 000 zł into Sejf PLN, balance 2 000,00 zł', async () => {
    await openPlaceMenu(page, 'Sejf', 'PLN', 'Wpłata');
    await cashOperation(page, dialog, { amount: '2000' });
    await expect(placeBalance(page, 'Sejf', 'PLN')).toContainText(pln('2 000,00'));
  });

  await test.step('Krok 20: deposit 300 USD into Sejf USD, balance 300,00 USD worth 1 200,00 zł, place total 3 200,00 zł', async () => {
    await openPlaceMenu(page, 'Sejf', 'USD', 'Wpłata');
    await cashOperation(page, dialog, { amount: '300' });
    await expect(placeBalance(page, 'Sejf', 'USD')).toContainText(usd('300,00'));
    await expect(async () => {
      await page.reload();
      await expect(
        placeBalance(page, 'Sejf', 'USD').locator('.cash-place__amount-pln'),
      ).toContainText(pln('1 200,00'), { timeout: 3_000 });
      await expect(cashPlace(page, 'Sejf').locator('.cash-place__value')).toContainText(
        pln('3 200,00'),
        { timeout: 3_000 },
      );
    }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 3_000] });
  });

  await test.step('Krok 21: ATM withdrawal of 500 zł from mBank into Sejf PLN offers PLN bank accounts only; Sejf PLN 2 500,00 zł, mBank 6 500,00 zł', async () => {
    await openPlaceMenu(page, 'Sejf', 'PLN', 'Wypłata z bankomatu');
    await openSelect(dialog.getByRole('combobox', { name: 'Konto bankowe' }));
    await expect(page.getByRole('option', { name: /^mBank \(/ })).toBeVisible();
    await expect(page.getByRole('option', { name: /^Nest Oszczędnościowe/ })).toBeVisible();
    await expect(page.getByRole('option', { name: /^mBank USD/ })).toHaveCount(0);
    await page.getByRole('option', { name: /^mBank \(/ }).click();
    await dialog.getByLabel('Kwota').fill('500');
    await dialog.getByRole('button', { name: 'Przelej' }).click();
    await expect(dialog).toBeHidden();
    await expect(placeBalance(page, 'Sejf', 'PLN')).toContainText(pln('2 500,00'));
    await expectBankBalance(page, 'mBank', pln('6 500,00'));
  });

  await test.step('Krok 22: bank deposit of 200 zł from Sejf PLN to mBank; Sejf PLN 2 300,00 zł, mBank 6 700,00 zł', async () => {
    await openPage(page, 'Gotówka');
    await openPlaceMenu(page, 'Sejf', 'PLN', 'Wpłata do banku');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Konto bankowe' }), /^mBank \(/);
    await dialog.getByLabel('Kwota').fill('200');
    await dialog.getByRole('button', { name: 'Przelej' }).click();
    await expect(dialog).toBeHidden();
    await expect(placeBalance(page, 'Sejf', 'PLN')).toContainText(pln('2 300,00'));
    await expectBankBalance(page, 'mBank', pln('6 700,00'));
  });

  await test.step('Krok 23: withdrawing 5 000 zł from Sejf PLN shows "Brak środków" and leaves the balance unchanged', async () => {
    await openPage(page, 'Gotówka');
    await openPlaceMenu(page, 'Sejf', 'PLN', 'Wypłata');
    await dialog.getByLabel('Kwota').fill('5000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByText(/Brak środków/).first()).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(placeBalance(page, 'Sejf', 'PLN')).toContainText(pln('2 300,00'));
  });

  await test.step('Krok 24: adding the gold metal Sztabka 1 g (1 g) has no first-purchase section and lists 0 pieces', async () => {
    await openPage(page, 'Metale szlachetne');
    await page.getByRole('button', { name: 'Dodaj metal szlachetny', exact: true }).first().click();
    await expect(dialog.getByRole('checkbox')).toHaveCount(0);
    await expect(dialog.getByText('Dodaj pierwszą transakcję')).toHaveCount(0);
    await selectOption(page, dialog.getByRole('combobox', { name: 'Metal' }), 'Złoto');
    await dialog.getByLabel('Nazwa').fill(METAL);
    await dialog.getByLabel('Waga kruszcu w sztuce').fill('1');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(metalRow(page).locator('.mat-column-pieces')).toHaveText(/^\s*0\s*$/);
  });

  await test.step('Krok 25: buying 1 piece for an invoice of 1 150,00 zł settled from Sejf PLN offers the groups Gotówka and Konta bankowe; Sejf PLN 1 150,00 zł', async () => {
    const buy = await openMetalDialog(page, 'Kup');
    await buy.getByLabel('Sztuki').fill('1');
    await buy.getByLabel('Kwota z faktury (PLN)').fill('1150');
    await openSelect(buy.getByRole('combobox', { name: 'Rozlicz z' }));
    await expect(page.getByRole('group', { name: 'Gotówka' })).toBeVisible();
    await expect(page.getByRole('group', { name: 'Konta bankowe' })).toBeVisible();
    await page.getByRole('option', { name: /^Sejf · PLN/ }).click();
    await buy.getByRole('button', { name: 'Zapisz' }).click();
    await expect(buy).toBeHidden();
    await openPage(page, 'Gotówka');
    await expect(placeBalance(page, 'Sejf', 'PLN')).toContainText(pln('1 150,00'));
  });

  await test.step('Krok 26: buying 1 piece for 1 500,00 zł from Sejf PLN shows "Brak środków" and saves nothing', async () => {
    const buy = await openMetalDialog(page, 'Kup');
    await buy.getByLabel('Sztuki').fill('1');
    await buy.getByLabel('Kwota z faktury (PLN)').fill('1500');
    await selectOption(page, buy.getByRole('combobox', { name: 'Rozlicz z' }), /^Sejf · PLN/);
    await buy.getByRole('button', { name: 'Zapisz' }).click();
    await expect(buy.getByText(/Brak środków/).first()).toBeVisible();
    await buy.getByRole('button', { name: 'Anuluj' }).click();
    await expect(buy).toBeHidden();
    await expect(metalRow(page).locator('.mat-column-pieces')).toHaveText(/^\s*1\s*$/);
  });

  await test.step('Krok 27: selling 1 piece for 1 250,00 zł into mBank gives mBank 7 950,00 zł and the transaction list shows 1 150,00 zł (Sejf) and 1 250,00 zł (mBank)', async () => {
    const sell = await openMetalDialog(page, 'Sprzedaj');
    await sell.getByLabel('Sztuki').fill('1');
    await sell.getByLabel('Kwota z faktury (PLN)').fill('1250');
    await selectOption(page, sell.getByRole('combobox', { name: 'Rozlicz z' }), /^mBank \(/);
    await sell.getByRole('button', { name: 'Zapisz' }).click();
    await expect(sell).toBeHidden();
    await expectBankBalance(page, 'mBank', pln('7 950,00'));

    await openPage(page, 'Metale szlachetne');
    await metalRow(page).getByRole('link', { name: METAL, exact: true }).click();
    const bought = page.getByRole('row').filter({ hasText: pln('1 150,00') });
    const sold = page.getByRole('row').filter({ hasText: pln('1 250,00') });
    await expect(bought).toHaveCount(1);
    await expect(bought).toContainText('Sejf');
    await expect(sold).toHaveCount(1);
    await expect(sold).toContainText('mBank');
  });

  await test.step('Krok 28: the dashboard shows net worth 33 856,89 zł, classes Konta bankowe 28 506,89 zł and Gotówka 5 350,00 zł, the cash card 1 150,00 zł and 300,00 USD totalling 2 350,00 zł, XTB IKE 3 000,00 zł and Sejf under Poza rachunkami', async () => {
    await expect(async () => {
      await openPage(page, 'Rachunki inwestycyjne');
      await openPage(page, 'Pulpit');
      await expect(page.getByTestId('net-worth')).toContainText(pln('33 856,89'), {
        timeout: 3_000,
      });
      await expect(
        page.getByRole('button', { name: /^Konta bankowe — 28\s?506,89\szł/ }),
      ).toBeVisible({ timeout: 3_000 });
      await expect(page.getByRole('button', { name: /^Gotówka — 5\s?350,00\szł/ })).toBeVisible({
        timeout: 3_000,
      });
      const cashCard = page.locator('.cash-card');
      await expect(cashCard).toContainText(pln('1 150,00'), { timeout: 3_000 });
      await expect(cashCard).toContainText(usd('300,00'), { timeout: 3_000 });
      await expect(cashCard.locator('.cash-card__total')).toContainText(pln('2 350,00'), {
        timeout: 3_000,
      });
      const accountsCard = page.locator('.accounts-card');
      await expect(
        accountsCard.locator('.accounts-card__row').filter({ hasText: 'XTB IKE' }),
      ).toContainText(pln('3 000,00'), { timeout: 3_000 });
      await expect(
        accountsCard.locator('.accounts-card__row').filter({ hasText: 'Poza rachunkami' }),
      ).toBeVisible({ timeout: 3_000 });
    }).toPass({ timeout: 90_000, intervals: [1_000, 2_000, 3_000] });
  });

  await test.step('Krok 29: the Nadchodzące card shows interest due on Nest Oszczędnościowe and its link leads to Konta bankowe', async () => {
    const item = page
      .locator('.upcoming-card__row')
      .filter({ hasText: 'Nest Oszczędnościowe' })
      .filter({ hasText: 'Odsetki na koncie bankowym do rozliczenia' });
    await expect(item).toBeVisible();
    await item.getByRole('link', { name: 'Nest Oszczędnościowe', exact: true }).click();
    await expect(page).toHaveURL(/\/wealth\/bank$/);
  });
});
