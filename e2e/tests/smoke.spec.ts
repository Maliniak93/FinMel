import { expect, logInThroughUi, registerThroughUi, test, wealthTab } from './fixtures';

// A grouping space and a non-breaking space between the digits and "zł" depend on the ICU build, so \s matches either.
const TEN_THOUSAND_PLN = /10\s000,00\szł/;

test('smoke: register, log in, open an account, deposit and see the balance', async ({
  page,
  user,
}) => {
  const accountName = `Smoke ${user.displayName}`;
  const navigation = page.locator('mat-sidenav');

  await test.step('register a new user', async () => {
    await registerThroughUi(page, user);
  });

  await test.step('log in', async () => {
    await logInThroughUi(page, user);
    await expect(page.getByRole('heading', { name: 'Pulpit' })).toBeVisible();
  });

  await test.step('create a Brokerage / Regular account', async () => {
    await wealthTab(page, 'Rachunki inwestycyjne');
    await page.getByRole('button', { name: 'Nowy rachunek' }).first().click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill(accountName);
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row', { name: new RegExp(accountName) })).toBeVisible();
  });

  await test.step('deposit 10 000,00 PLN', async () => {
    await page.getByRole('link', { name: accountName }).click();
    await page.getByRole('button', { name: 'Wpłata', exact: true }).click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Kwota').fill('10000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('the accounts list shows the balance', async () => {
    await wealthTab(page, 'Rachunki inwestycyjne');
    await expect(page.getByRole('row', { name: new RegExp(accountName) })).toContainText(
      TEN_THOUSAND_PLN,
    );
  });

  await test.step('the dashboard net worth reaches the deposit', async () => {
    // The dashboard loads once and the deposit reaches Reporting through an event, so each attempt opens it afresh.
    await expect(async () => {
      await wealthTab(page, 'Rachunki inwestycyjne');
      await navigation.getByRole('link', { name: 'Pulpit' }).click();
      await expect(page.getByTestId('net-worth')).toContainText(TEN_THOUSAND_PLN, {
        timeout: 3_000,
      });
    }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 3_000] });
  });
});
