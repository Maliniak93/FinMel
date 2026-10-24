import type { Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
  openSelect,
} from './fixtures';

// pl-PL groups only numbers from 10 000 up, with a space or a non-breaking space, so the pattern accepts either.
function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

const ACCOUNT = 'Obligacje PKO';
const SERIES = 'OTS0127';
const ACCOUNT_TYPE_BONDS = 1;
const WRAPPER_REGULAR = 0;

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function bondRow(page: Page): Locator {
  return page.getByRole('row').filter({ hasText: SERIES });
}

async function openBuyDialog(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Kup obligacje' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Zakup obligacji skarbowych' })).toBeVisible();
  return dialog;
}

async function chooseOption(page: Page, select: Locator, option: RegExp): Promise<void> {
  await openSelect(select);
  await page.getByRole('option', { name: option }).click();
}

// The recorded MF offer holds no OTS0127 on sale 73 days back, so the series is entered manually with its first-period rate.
async function fillOtsBond(page: Page, dialog: Locator, count: string): Promise<void> {
  await chooseOption(
    page,
    dialog.getByRole('combobox', { name: 'Seria z oferty' }),
    /Wprowadź ręcznie/,
  );
  await dialog.getByLabel('Nazwa').fill(SERIES);
  await dialog.getByLabel('Kod serii').fill(SERIES);
  await chooseOption(
    page,
    dialog.getByRole('combobox', { name: 'Typ obligacji' }),
    /Trzymiesięczne stałoprocentowe/,
  );
  await dialog.getByLabel('Liczba obligacji').fill(count);
  await dialog.getByLabel('Cena za obligację').fill('100');
  await dialog.getByLabel('Oprocentowanie w 1. okresie (%)').fill('2');
  await dialog.getByLabel('Opłata za wcześniejszy wykup (za obligację)').fill('0');
}

async function expectAccountCash(page: Page, amount: string): Promise<void> {
  await navigation(page).getByRole('link', { name: 'Rachunki inwestycyjne', exact: true }).click();
  await page.getByRole('link', { name: ACCOUNT, exact: true }).click();
  await expect(page.getByTestId('account-cash')).toContainText(pln(amount));
}

test('treasury bonds: buy, value and redeem early on /bonds', async ({ page, request, user }) => {
  test.setTimeout(240_000);

  const token = await logInThroughApi(request, user);
  const account = await createAccountThroughApi(request, token, {
    name: ACCOUNT,
    type: ACCOUNT_TYPE_BONDS,
    wrapper: WRAPPER_REGULAR,
  });
  await depositCashThroughApi(request, token, account.cashAssetId, {
    quantity: 5000,
    date: warsawDate(80),
  });

  await page.goto('/login');
  await logInThroughUi(page, user);
  await navigation(page).getByRole('link', { name: 'Obligacje', exact: true }).click();

  await test.step('Krok 1: the purchase dialog for Obligacje PKO shows the account cash 5 000,00 zł', async () => {
    const dialog = await openBuyDialog(page);
    await chooseOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), /Obligacje PKO/);
    await expect(dialog.getByTestId('account-cash')).toContainText(pln('5 000,00'));
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 2: 10 OTS0127 bonds at 100,00 zł bought 73 days back appear on the list with book value 1 000,00 zł', async () => {
    const dialog = await openBuyDialog(page);
    await chooseOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), /Obligacje PKO/);
    await dialog.getByLabel('Data zakupu').fill(warsawDate(73));
    await fillOtsBond(page, dialog, '10');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await expect(bondRow(page).locator('.mat-column-bondCount')).toContainText('10');
    await expect(bondRow(page).locator('.mat-column-bookValue')).toContainText(pln('1 000,00'));
  });

  await test.step('Krok 3: the OTS0127 row shows the value today 1 004,00 zł gross and 1 000,00 zł net', async () => {
    await expect(bondRow(page).getByTestId('gross-today')).toContainText(pln('1 004,00'));
    await expect(bondRow(page).getByTestId('net-today')).toContainText(pln('1 000,00'));
  });

  await test.step('Krok 4: the Obligacje PKO account details show the cash 4 000,00 zł', async () => {
    await expectAccountCash(page, '4 000,00');
  });

  await test.step('Krok 5: buying 50 more bonds today is refused for lack of funds and the list stays at 10', async () => {
    await navigation(page).getByRole('link', { name: 'Obligacje', exact: true }).click();
    const dialog = await openBuyDialog(page);
    await chooseOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), /Obligacje PKO/);
    await fillOtsBond(page, dialog, '50');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog.getByRole('alert')).toContainText('Brak środków na rachunku');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(bondRow(page).locator('.mat-column-bondCount')).toContainText('10');
  });

  await test.step('Krok 6: the early redemption of 10 OTS0127 bonds today previews the fee 0,00 zł and the payout 1 000,00 zł', async () => {
    await bondRow(page)
      .getByRole('button', { name: `Akcje dla: ${SERIES}` })
      .click();
    await page.getByRole('menuitem', { name: 'Wykup przed terminem', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Przedterminowy wykup' })).toBeVisible();
    await dialog.getByTestId('early-redeem-count').fill('10');
    const preview = dialog.getByTestId('early-redemption-preview');
    await expect(
      preview.locator('div').filter({ hasText: 'Opłata za przedterminowy wykup' }),
    ).toContainText(pln('0,00'));
    await expect(preview.locator('div').filter({ hasText: 'Do wypłaty' })).toContainText(
      pln('1 000,00'),
    );
  });

  await test.step('Krok 7: after the confirmation OTS0127 leaves the active list and shows as redeemed with Pokaż wykupione', async () => {
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Wykup przed terminem', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(bondRow(page)).toHaveCount(0);
    await page.getByTestId('show-redeemed').getByRole('switch').click();
    await expect(bondRow(page)).toBeVisible();
    await expect(bondRow(page).locator('.mat-column-status')).toContainText('Wykupiona');
  });

  await test.step('Krok 8: the Obligacje PKO account details show the cash 5 000,00 zł', async () => {
    await expectAccountCash(page, '5 000,00');
  });
});
