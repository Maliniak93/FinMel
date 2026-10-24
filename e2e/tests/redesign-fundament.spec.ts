import type { Locator, Page } from '@playwright/test';
import { expect, test, openSelect } from './fixtures';
import { expectProjected } from './projection';

const TEN_THOUSAND_PLN = /10\s000,00\szł/;
const FOUR_TWENTY_EUR = /420,00\sEUR/;

const WIDE = { width: 1280, height: 800 };
const RAIL = { width: 1000, height: 800 };
const NARROW = { width: 600, height: 800 };

function sidebar(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function themeButton(page: Page): Locator {
  return page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
}

function dataTheme(page: Page): Locator {
  return page.locator('html');
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

test('redesign foundation: Classical look, three-mode shell, shared patterns and auth pages', async ({
  page,
  user,
}) => {
  test.setTimeout(300_000);
  await page.setViewportSize(WIDE);

  const externalFontRequests: string[] = [];
  page.on('request', (request) => {
    if (/fonts\.(googleapis|gstatic)\.com/.test(request.url())) {
      externalFontRequests.push(request.url());
    }
  });

  await test.step('Krok 1: the login page is a centred column with a serif title, labels above the fields, a full-width button, the register link and the PL switch in the corner', async () => {
    await page.goto('/login');
    const brand = page.getByText('Skarbiec', { exact: true });
    await expect(brand).toBeVisible();
    await expect(brand).toHaveCSS('font-family', /Cormorant Garamond/);
    await expect(page.locator('label[for="login-email"]')).toHaveText('E-mail');
    await expect(page.locator('label[for="login-password"]')).toHaveText('Hasło');

    const emailLabel = await page.locator('label[for="login-email"]').boundingBox();
    const emailInput = await page.locator('#login-email').boundingBox();
    expect(emailLabel!.y + emailLabel!.height).toBeLessThanOrEqual(emailInput!.y);

    const submit = await page.getByRole('button', { name: 'Zaloguj się' }).boundingBox();
    const column = await page.locator('.auth-shell__column').boundingBox();
    expect(Math.abs(submit!.width - column!.width)).toBeLessThan(2);
    const centre = column!.x + column!.width / 2;
    expect(Math.abs(centre - WIDE.width / 2)).toBeLessThan(2);

    await expect(page.getByRole('link', { name: 'Zarejestruj się' })).toBeVisible();
    const language = page.getByRole('button', { name: 'Język' });
    await expect(language).toHaveText('PL');
    const box = await language.boundingBox();
    expect(box!.x).toBeGreaterThan(WIDE.width / 2);
    expect(box!.y).toBeLessThan(100);
  });

  await test.step('Krok 2: a wrong password shows the error with an icon and keeps the fields', async () => {
    await page.getByLabel('E-mail').fill('jan@example.pl');
    await page.getByLabel('Hasło').fill('zleHaslo1');
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    const alert = page.getByRole('alert');
    await expect(alert).toContainText('Invalid email or password.');
    await expect(alert.locator('mat-icon')).toBeVisible();
    await expect(page.getByLabel('E-mail')).toHaveValue('jan@example.pl');
    await expect(page.getByLabel('Hasło')).toHaveValue('zleHaslo1');
  });

  await test.step('Krok 3: register uses the same look and returns to the login page', async () => {
    await page.getByRole('link', { name: 'Zarejestruj się' }).click();
    await expect(page).toHaveURL(/\/register$/);
    await expect(page.getByText('Skarbiec', { exact: true })).toHaveCSS(
      'font-family',
      /Cormorant Garamond/,
    );
    await expect(page.locator('.auth-shell__column')).toBeVisible();
    await page.getByLabel('Nazwa wyświetlana').fill(user.displayName);
    await page.getByLabel('E-mail').fill(user.email);
    await page.getByLabel('Hasło').fill(user.password);
    await page.getByRole('button', { name: 'Zarejestruj się' }).click();
    await expect(page).toHaveURL(/\/login$/);
  });

  await test.step('Krok 4: after logging in the Dashboard shows a 232 px sidebar with sections, an active marker and Settings above the footer, and no top bar', async () => {
    await page.getByLabel('E-mail').fill(user.email);
    await page.getByLabel('Hasło').fill(user.password);
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('heading', { name: 'Pulpit' })).toBeVisible();

    await expect(sidebar(page)).toBeVisible();
    expect((await sidebar(page).boundingBox())!.width).toBe(232);
    await expect(page.getByTestId('top-bar')).toHaveCount(0);
    await expect(page.getByTestId('nav-section')).toHaveText([/Przegląd/, /Majątek/]);
    await expect(sidebar(page).getByRole('link', { name: 'Pulpit' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(
      page.getByTestId('nav-pinned').getByRole('link', { name: 'Ustawienia' }),
    ).toBeVisible();
    await expect(page.getByTestId('nav-footer')).toBeVisible();
  });

  await test.step('Krok 5: headings use Cormorant, body text Lora and numbers are tabular', async () => {
    await expect(page.locator('.shell__brand')).toHaveCSS('font-family', /Cormorant Garamond/);
    await expect(page.locator('body')).toHaveCSS('font-family', /Lora/);
    await expect(page.locator('body')).toHaveCSS('font-variant-numeric', /tabular-nums/);
    const loaded = await page.evaluate(async () => {
      await document.fonts.load('600 22px "Cormorant Garamond"');
      await document.fonts.load('400 14px "Lora"');
      return (
        document.fonts.check('600 22px "Cormorant Garamond"') &&
        document.fonts.check('400 14px "Lora"')
      );
    });
    expect(loaded).toBe(true);
  });

  await test.step('Krok 6: the theme button cycles light, dark, system with a "Motyw:" label and the choice survives a reload', async () => {
    await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: systemowy/);
    await themeButton(page).click();
    await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: jasny/);
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'light');
    await themeButton(page).click();
    await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: ciemny/);
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'dark');

    await page.reload();
    await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: ciemny/);
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'dark');

    await themeButton(page).click();
    await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: systemowy/);
  });

  await test.step('Krok 7: in system mode the app follows the operating system scheme without a reload', async () => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'dark');
    await page.emulateMedia({ colorScheme: 'light' });
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'light');
  });

  await test.step('Krok 8: the language menu switches to English and back to Polish', async () => {
    const footer = page.getByTestId('nav-footer');
    await footer.getByRole('button', { name: 'Język' }).click();
    await page.getByRole('menuitemradio', { name: 'English' }).click();
    await expect(page.getByTestId('nav-section')).toHaveText([/Overview/, /Wealth/]);
    await expect(sidebar(page).getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(footer.getByRole('button', { name: 'Language' })).toHaveText('EN');

    await footer.getByRole('button', { name: 'Language' }).click();
    await page.getByRole('menuitemradio', { name: 'Polski' }).click();
    await expect(page.getByTestId('nav-section')).toHaveText([/Przegląd/, /Majątek/]);
    await expect(footer.getByRole('button', { name: 'Język' })).toHaveText('PL');
  });

  await test.step('Krok 9: create the XTB account and deposit 10 000 zł, shown as 10 000,00 zł', async () => {
    await sidebar(page).getByRole('link', { name: 'Rachunki inwestycyjne' }).click();
    await page.getByRole('button', { name: 'Utwórz pierwszy rachunek' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill('XTB');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await page.getByRole('link', { name: 'XTB', exact: true }).click();
    await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
    await dialog.getByLabel('Kwota').fill('10000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await sidebar(page).getByRole('link', { name: 'Rachunki inwestycyjne' }).click();
    await expect(page.getByRole('row', { name: /XTB/ })).toContainText(TEN_THOUSAND_PLN);
  });

  await test.step('Krok 10: the Revolut EUR bank account shows 420,00 EUR with the code and no euro sign', async () => {
    await sidebar(page).getByRole('link', { name: 'Konta bankowe', exact: true }).click();
    await page.getByRole('button', { name: 'Dodaj konto', exact: true }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill('Revolut EUR');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta' }), /^EUR/);
    await selectOption(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Bieżące');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('0');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    const row = page
      .getByRole('row')
      .filter({ has: page.getByRole('link', { name: 'Revolut EUR', exact: true }) });
    await row.getByRole('button', { name: 'Akcje dla: Revolut EUR', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await dialog.getByLabel('Kwota').fill('420');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    const balance = row.locator('.mat-column-balance');
    await expect(balance).toContainText(FOUR_TWENTY_EUR);
    await expect(balance).not.toContainText('€');
  });

  await test.step('Krok 11: a red group swatch is red and lighter in the dark theme', async () => {
    await sidebar(page).getByRole('link', { name: 'Grupy', exact: true }).click();
    await page.getByTestId('add-group').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill('Wakacje 2027');
    await dialog.getByRole('radio', { name: 'Czerwony' }).click();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    const swatch = page
      .getByTestId('group-row')
      .filter({ hasText: 'Wakacje 2027' })
      .getByTestId('group-swatch');
    await themeButton(page).click();
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'light');
    await expect(swatch).toHaveCSS('background-color', 'rgb(192, 73, 58)');

    await themeButton(page).click();
    await expect(dataTheme(page)).toHaveAttribute('data-theme', 'dark');
    await expect(swatch).toHaveCSS('background-color', 'rgb(232, 138, 124)');

    await themeButton(page).click();
    await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: systemowy/);
  });

  await test.step('Krok 12: the Dashboard allocation legend has a coloured swatch and a class label for every entry', async () => {
    await expectProjected(
      async () => {
        await sidebar(page).getByRole('link', { name: 'Grupy', exact: true }).click();
        await sidebar(page).getByRole('link', { name: 'Pulpit' }).click();
      },
      async () => {
        const items = page
          .locator('.dashboard-page__legend')
          .first()
          .locator('.dashboard-page__legend-item');
        await expect(items.filter({ hasText: 'Gotówka' })).toHaveCount(1, { timeout: 3_000 });
        for (const item of await items.all()) {
          await expect(item).toHaveText(/\S+\s—\s/);
          await expect(item.locator('.dashboard-page__swatch')).not.toHaveCSS(
            'background-color',
            'rgba(0, 0, 0, 0)',
          );
        }
      },
    );
  });

  await test.step('Krok 13: at about 1000 px the sidebar is a 72 px rail with icons and short labels, a scrolling list and pinned Settings, theme, language and log out', async () => {
    await page.setViewportSize(RAIL);
    await expect.poll(async () => (await sidebar(page).boundingBox())?.width).toBe(72);
    const links = page.getByTestId('nav-list').locator('.shell__nav-link');
    await expect(links).toHaveCount(10);
    for (const link of await links.all()) {
      await expect(link.locator('mat-icon')).toBeVisible();
      await expect(link.locator('.shell__nav-label')).not.toBeEmpty();
    }
    await expect(links.nth(2).locator('.shell__nav-label')).toHaveText('Rachunki');
    await expect(page.getByTestId('nav-list')).toHaveCSS('overflow-y', 'auto');

    const pinned = page.getByTestId('nav-pinned');
    await expect(pinned.getByRole('link', { name: 'Ustawienia' })).toBeVisible();
    const footer = page.getByTestId('nav-footer');
    await expect(footer.getByRole('button', { name: /^Motyw:/ })).toBeVisible();
    await expect(footer.getByRole('button', { name: 'Język' })).toBeVisible();
    await expect(footer.getByRole('button', { name: 'Wyloguj' })).toBeVisible();
  });

  await test.step('Krok 14: at about 600 px there is a bottom navigation and a thin top bar with the name, Settings, theme, language and log out', async () => {
    await page.setViewportSize(NARROW);
    const bottom = page.getByTestId('bottom-nav');
    await expect(bottom).toBeVisible();
    await expect(bottom.getByRole('link', { name: 'Pulpit' })).toBeVisible();
    await expect(bottom.getByRole('link', { name: 'Planowanie' })).toBeVisible();
    await expect(bottom.getByRole('link', { name: 'Grupy' })).toBeVisible();
    await expect(bottom.getByRole('button', { name: 'Więcej' })).toBeVisible();

    const top = page.getByTestId('top-bar');
    await expect(top).toBeVisible();
    await expect(top).toContainText('Skarbiec');
    await expect(top.getByRole('link', { name: 'Ustawienia' })).toBeVisible();
    await expect(top.getByRole('button', { name: /^Motyw:/ })).toBeVisible();
    await expect(top.getByRole('button', { name: 'Język' })).toBeVisible();
    await expect(top.getByRole('button', { name: 'Wyloguj' })).toBeVisible();
    expect((await top.boundingBox())!.height).toBeLessThan(72);
  });

  await test.step('Krok 15: "Więcej" opens the Majątek sheet with seven asset types and Lokaty closes it and highlights "Więcej"', async () => {
    await page.getByTestId('bottom-nav').getByRole('button', { name: 'Więcej' }).click();
    const sheet = page.locator('mat-bottom-sheet-container');
    await expect(sheet.getByRole('heading', { name: 'Majątek' })).toBeVisible();
    await expect(sheet.getByRole('link')).toHaveCount(7);
    await sheet.getByRole('link', { name: 'Lokaty' }).click();
    await expect(sheet).toBeHidden();
    await expect(page).toHaveURL(/\/deposits$/);
    await expect(
      page.getByTestId('bottom-nav').getByRole('button', { name: 'Więcej' }),
    ).toHaveAttribute('aria-current', 'page');
  });

  await test.step('Krok 16: Settings has a page header with "Synchronizuj teraz" and a grey skeleton while the status loads', async () => {
    await page.route('**/api/marketdata/sync/status', async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 3_000));
      await route.continue();
    });
    await page.getByTestId('top-bar').getByRole('link', { name: 'Ustawienia' }).click();
    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('button', { name: 'Synchronizuj teraz' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Ustawienia' })).toHaveCSS(
      'font-family',
      /Cormorant Garamond/,
    );
    await expect(page.locator('app-skeleton .skeleton__row').first()).toBeVisible();
    await expect(page.locator('mat-spinner')).toHaveCount(0);
    await expect(page.locator('app-skeleton')).toHaveCount(0, { timeout: 15_000 });
    await page.unroute('**/api/marketdata/sync/status');
  });

  await test.step('Krok 17: removing XTB asks for confirmation with focus on "Anuluj", a destructive confirm button, and Enter does not delete', async () => {
    await page.setViewportSize(WIDE);
    await sidebar(page).getByRole('link', { name: 'Rachunki inwestycyjne' }).click();
    const row = page.getByRole('row', { name: /XTB/ });
    await row.getByRole('button', { name: 'Akcje dla: XTB', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Usuń', exact: true }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Anuluj' })).toBeFocused();
    await expect(dialog.getByRole('button', { name: 'Usuń', exact: true })).toHaveClass(
      /confirm-dialog__confirm--destructive/,
    );
    await page.keyboard.press('Enter');
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row', { name: /XTB/ })).toBeVisible();
  });

  await test.step('Krok 18: no Google Fonts requests and the sidebar icons are symbols, not words', async () => {
    expect(externalFontRequests).toEqual([]);
    const icon = sidebar(page).locator('.shell__nav-icon').first();
    await expect(icon).toHaveCSS('font-family', /Material Symbols Outlined/);
    const symbolsLoaded = await page.evaluate(async () => {
      await document.fonts.load('24px "Material Symbols Outlined"', 'dashboard');
      return document.fonts.check('24px "Material Symbols Outlined"', 'dashboard');
    });
    expect(symbolsLoaded).toBe(true);
    const iconWidth = (await icon.boundingBox())!.width;
    expect(iconWidth).toBeLessThan(40);
  });

  await test.step('Krok 19: logging out from the footer returns to the login screen', async () => {
    await page.getByTestId('nav-footer').getByRole('button', { name: 'Wyloguj' }).click();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('button', { name: 'Zaloguj się' })).toBeVisible();
  });
});
