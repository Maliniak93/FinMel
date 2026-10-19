import { expect, test, warsawDate } from './fixtures';
import type { Locator, Page } from '@playwright/test';

const ACCOUNT = 'IKE Bossa';
const YEAR = warsawDate().slice(0, 4);

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

function bar(contributed: string, limit: string): RegExp {
  return new RegExp(
    `Wpłacono\\s${contributed.replace(/ /g, '\\s?')}\\s/\\s${limit.replace(/ /g, '\\s?')}\\szł\\sw\\s${YEAR}`,
  );
}

async function selectOption(page: Page, select: Locator, option: string): Promise<void> {
  await select.click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

function accountRow(page: Page): Locator {
  return page
    .getByRole('row')
    .filter({ has: page.getByRole('link', { name: ACCOUNT, exact: true }) });
}

async function openRowMenu(page: Page, item: string): Promise<void> {
  await accountRow(page)
    .getByRole('button', { name: `Akcje dla: ${ACCOUNT}`, exact: true })
    .click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

test('accounts: an IKE account with a contribution limit, deposits, withdrawals, archiving and restoring', async ({
  signedInPage: page,
}) => {
  const dialog = page.getByRole('dialog');
  const cash = page.getByTestId('account-cash');
  const contribution = page.getByTestId('contribution-bar');

  async function cashOperation(button: string, amount: string): Promise<void> {
    await page.getByRole('button', { name: button, exact: true }).click();
    await dialog.getByLabel('Kwota').fill(amount);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
  }

  await test.step('Krok 1: the IKE Bossa brokerage account with the IKE wrapper is on the list with 0,00 zł', async () => {
    await page
      .locator('mat-sidenav')
      .getByRole('link', { name: 'Rachunki inwestycyjne', exact: true })
      .click();
    await page
      .getByRole('button', { name: /^(Nowy rachunek|Utwórz pierwszy rachunek)$/ })
      .first()
      .click();
    await dialog.getByLabel('Nazwa').fill(ACCOUNT);
    await selectOption(page, dialog.getByRole('combobox', { name: 'Typ' }), 'Maklerski');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Opakowanie podatkowe' }), 'IKE');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(accountRow(page)).toContainText('IKE');
    await expect(accountRow(page)).toContainText(pln('0,00'));
  });

  await test.step('Krok 2: setting the limit to 20 000,00 zł for the current year shows the bar "Wpłacono 0,00 / 20 000,00 zł"', async () => {
    await accountRow(page).getByRole('link', { name: ACCOUNT, exact: true }).click();
    await page.getByRole('button', { name: 'Limit wpłat', exact: true }).click();
    await dialog.getByLabel('Limit (zł)').fill('20000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(contribution).toContainText(bar('0,00', '20 000,00'));
  });

  await test.step('Krok 3: depositing 15 000,00 zł gives cash 15 000,00 zł and the bar "Wpłacono 15 000,00 / 20 000,00 zł" without the exceeded mark', async () => {
    await cashOperation('Wpłata', '15000');
    await expect(dialog).toBeHidden();
    await expect(cash).toContainText(pln('15 000,00'));
    await expect(contribution).toContainText(bar('15 000,00', '20 000,00'));
    await expect(contribution).not.toHaveClass(/contribution-bar--exceeded/);
  });

  await test.step('Krok 4: depositing 10 000,00 zł is accepted, cash 25 000,00 zł and the bar "Wpłacono 25 000,00 / 20 000,00 zł" is marked as exceeded', async () => {
    await cashOperation('Wpłata', '10000');
    await expect(dialog).toBeHidden();
    await expect(cash).toContainText(pln('25 000,00'));
    await expect(contribution).toContainText(bar('25 000,00', '20 000,00'));
    await expect(contribution).toHaveClass(/contribution-bar--exceeded/);
  });

  await test.step('Krok 5: withdrawing 3 000,00 zł gives cash 22 000,00 zł and the bar stays "Wpłacono 25 000,00 / 20 000,00 zł"', async () => {
    await cashOperation('Wypłata', '3000');
    await expect(dialog).toBeHidden();
    await expect(cash).toContainText(pln('22 000,00'));
    await expect(contribution).toContainText(bar('25 000,00', '20 000,00'));
  });

  await test.step('Krok 6: withdrawing 30 000,00 zł shows the insufficient funds error on the amount, the dialog stays open and cash stays 22 000,00 zł', async () => {
    await cashOperation('Wypłata', '30000');
    await expect(dialog.getByText('Brak środków na rachunku')).toBeVisible();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(cash).toContainText(pln('22 000,00'));
  });

  await test.step('Krok 7: withdrawing 22 000,00 zł gives cash 0,00 zł', async () => {
    await cashOperation('Wypłata', '22000');
    await expect(dialog).toBeHidden();
    await expect(cash).toContainText(pln('0,00'));
  });

  await test.step('Krok 8: archiving IKE Bossa removes it from the list, and "Pokaż zarchiwizowane" shows it with the "Zarchiwizowany" mark', async () => {
    await page.getByRole('link', { name: 'Wróć do rachunków' }).click();
    await openRowMenu(page, 'Archiwizuj');
    await dialog.getByRole('button', { name: 'Archiwizuj', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('link', { name: ACCOUNT, exact: true })).toHaveCount(0);
    await page.getByRole('switch', { name: 'Pokaż zarchiwizowane' }).click();
    await expect(accountRow(page)).toContainText('Zarchiwizowany');
  });

  await test.step('Krok 9: restoring IKE Bossa removes the mark and the account stays on the list after "Pokaż zarchiwizowane" is turned off', async () => {
    await openRowMenu(page, 'Przywróć');
    await dialog.getByRole('button', { name: 'Przywróć', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(accountRow(page)).not.toContainText('Zarchiwizowany');
    await page.getByRole('switch', { name: 'Pokaż zarchiwizowane' }).click();
    await expect(accountRow(page)).toBeVisible();
    await expect(accountRow(page)).not.toContainText('Zarchiwizowany');
  });
});
