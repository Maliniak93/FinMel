import type { Locator, Page } from '@playwright/test';
import { expect, openSelect, test } from './fixtures';
import { expectProjected } from './projection';

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

const WIDE = { width: 1280, height: 800 };
const RAIL = { width: 1000, height: 800 };
const NARROW = { width: 600, height: 800 };

const TAB_NAMES = [
  'Wszystko',
  'Rachunki inwestycyjne',
  'Konta bankowe',
  'Gotówka',
  'Lokaty',
  'Obligacje',
  'Akcje i ETF',
  'Metale szlachetne',
];

function sidebar(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function dialogOf(page: Page): Locator {
  return page.getByRole('dialog');
}

function tab(page: Page, name: string): Locator {
  return page.getByRole('tab', { name: new RegExp(`^${name}(\\s|$)`) });
}

async function clickTab(page: Page, name: string): Promise<void> {
  await tab(page, name).focus();
  await page.keyboard.press('Enter');
}

async function openWealth(page: Page): Promise<void> {
  await sidebar(page).getByRole('link', { name: 'Majątek', exact: true }).click();
  await expect(page).toHaveURL(/\/wealth$/);
}

async function openTab(page: Page, name: string): Promise<void> {
  await openWealth(page);
  await clickTab(page, name);
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

async function chooseGroupFilter(page: Page, group: string): Promise<void> {
  const filter = page.getByRole('combobox', { name: 'Grupa', exact: true });
  await expect(async () => {
    await page.keyboard.press('Escape');
    await selectOption(page, filter, group);
    await expect(filter).toContainText(`Grupa: ${group}`, { timeout: 2_000 });
  }).toPass({ timeout: 20_000 });
}

async function newAsset(page: Page, entry: string): Promise<void> {
  await page.getByRole('button', { name: 'Nowe aktywo' }).first().click();
  await page.getByRole('menuitem', { name: entry, exact: true }).click();
}

function section(page: Page, name: string): Locator {
  return page.locator('tbody[data-section]').filter({
    has: page.locator('.wealth-all__section-name', { hasText: new RegExp(`^${name}$`) }),
  });
}

function allRow(page: Page, name: string | RegExp): Locator {
  return page
    .locator('tr.wealth-all__row')
    .filter({ has: page.locator('.wealth-all__name', { hasText: name }) });
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

function cashPlace(page: Page, name: string): Locator {
  return page
    .locator('mat-card.cash-place')
    .filter({ has: page.getByRole('heading', { name, exact: true }) });
}

async function cashOperation(dialog: Locator, amount: string): Promise<void> {
  await dialog.getByLabel('Kwota').fill(amount);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function depositOnAccountPage(page: Page, account: string, amount: string): Promise<void> {
  await openTab(page, 'Rachunki inwestycyjne');
  await accountRow(page, account).getByRole('link', { name: account, exact: true }).click();
  await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
  await cashOperation(dialogOf(page), amount);
}

async function refetchWealth(page: Page): Promise<void> {
  await page.goto('/wealth');
  await expect(page.getByRole('heading', { name: 'Majątek', exact: true })).toBeVisible();
}

async function syncNow(page: Page): Promise<void> {
  await sidebar(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
  const button = page.getByRole('button', { name: 'Synchronizuj teraz' });
  await button.click();
  await expect(button).toBeEnabled();
}

test('redesign Majątek: navigation, Wszystko view, tabs, filters, archive, asset page and widths', async ({
  signedInPage: page,
}) => {
  test.setTimeout(420_000);
  await page.setViewportSize(WIDE);
  const dialog = dialogOf(page);
  const total = page.getByTestId('total');
  let assetUrl = '';

  await test.step('Krok 1: the sidebar lists Pulpit, Majątek, Planowanie, Grupy and Ustawienia, not seven asset types', async () => {
    await expect(sidebar(page).locator('.shell__nav-label')).toHaveText([
      /^Pulpit$/,
      /^Majątek$/,
      /^Planowanie$/,
      /^Grupy$/,
      /^Ustawienia$/,
    ]);
    await expect(sidebar(page).getByRole('link', { name: 'Lokaty', exact: true })).toHaveCount(0);
    await expect(sidebar(page).getByRole('link', { name: 'Obligacje', exact: true })).toHaveCount(
      0,
    );
  });

  await test.step('Krok 2: Majątek opens /wealth with the heading, eight tabs and the empty state with "Nowe aktywo"', async () => {
    await openWealth(page);
    await expect(page.getByRole('heading', { name: 'Majątek', exact: true })).toBeVisible();
    for (const name of TAB_NAMES) {
      await expect(tab(page, name)).toBeVisible();
    }
    await expect(page.getByRole('tab')).toHaveCount(TAB_NAMES.length);
    await expect(page.getByText('Nie masz jeszcze żadnego majątku.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Nowe aktywo' })).toHaveCount(2);
  });

  await test.step('Krok 3: creating the XTB IKE account (limit 20 000 zł, deposit 10 000 zł) puts it in "Rachunki inwestycyjne" with 10 000,00 zł and "1 rachunek" under the tab', async () => {
    await newAsset(page, 'Rachunek inwestycyjny');
    await dialog.getByLabel('Nazwa').fill('XTB IKE');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Opakowanie podatkowe' }), 'IKE');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page).toHaveURL(/\/wealth$/);
    await expect(allRow(page, 'XTB IKE')).toBeVisible();

    await allRow(page, 'XTB IKE').getByRole('link', { name: 'XTB IKE', exact: true }).click();
    await page.getByRole('button', { name: 'Limit wpłat', exact: true }).click();
    await dialog.getByLabel('Limit (zł)').fill('20000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, '10000');

    await openWealth(page);
    await expectProjected(
      () => refetchWealth(page),
      async () => {
        await expect(
          section(page, 'Rachunki inwestycyjne').getByRole('row', { name: /XTB IKE/ }),
        ).toContainText(pln('10 000,00'), { timeout: 3_000 });
        await expect(tab(page, 'Rachunki inwestycyjne')).toContainText('1 rachunek', {
          timeout: 3_000,
        });
      },
    );
  });

  await test.step('Krok 4: the Rachunki inwestycyjne tab is /wealth/accounts and XTB IKE shows a calm limit bar "Wpłacono 10 000,00 / 20 000,00 zł w <rok> · zostało 10 000,00 zł"', async () => {
    await clickTab(page, 'Rachunki inwestycyjne');
    await expect(page).toHaveURL(/\/wealth\/accounts$/);
    const bar = accountRow(page, 'XTB IKE').getByTestId('contribution-bar');
    await expect(bar).toHaveAttribute('data-state', 'calm');
    await expect(bar).toContainText(
      /Wpłacono\s10\s?000,00\s\/\s20\s?000,00\szł\sw\s\d{4}\s·\szostało\s10\s?000,00\szł/,
    );
  });

  await test.step('Krok 5: the account name opens /wealth/accounts/<id> with the breadcrumb "Majątek · Rachunki inwestycyjne" and the deposit, transfer and limit actions', async () => {
    await accountRow(page, 'XTB IKE').getByRole('link', { name: 'XTB IKE', exact: true }).click();
    await expect(page).toHaveURL(/\/wealth\/accounts\/[0-9a-f-]+$/);
    await expect(page.getByTestId('breadcrumb')).toContainText('Majątek · Rachunki inwestycyjne');
    await expect(page.getByRole('button', { name: 'Wpłata', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Przelew', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Limit wpłat', exact: true })).toBeVisible();
  });

  await test.step('Krok 6: the stock CD Projekt (CDR.WA), 70 pieces at 200,00 zł on XTB IKE, appears in "Akcje i ETF" with the value "—"', async () => {
    await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, '10000');

    await openWealth(page);
    await newAsset(page, 'Akcja');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), /XTB IKE/);
    await dialog.getByLabel('Instrument finansowy').fill('CDR');
    await page.getByRole('option', { name: /CDR\.WA/ }).click();
    await expect(dialog.getByText('Zweryfikowano: CDR.WA')).toBeVisible();
    await dialog.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await dialog.getByLabel(/^Ilość/).fill('70');
    await dialog.getByLabel(/^Cena jednostkowa/).fill('200');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    const stocks = section(page, 'Akcje i ETF');
    await expect(allRow(page, /CD Projekt/)).toBeVisible();
    await expect(stocks).toContainText(/CD Projekt/);
    await expect(allRow(page, /CD Projekt/).locator('.wealth-all__num')).toHaveText(
      /^(—|7\s?000,00\szł)/,
    );
  });

  await test.step('Krok 7: after "Synchronizuj teraz" CD Projekt has a value with a signed change, the totals rise and "Stan na <data>" is shown', async () => {
    await syncNow(page);
    await expectProjected(
      async () => {
        await refetchWealth(page);
      },
      async () => {
        const row = allRow(page, /CD Projekt/);
        await expect(row.locator('.wealth-all__num')).toContainText(pln('7 000,00'), {
          timeout: 3_000,
        });
        await expect(row.locator('.wealth-all__change')).toContainText(/[-−+]/, {
          timeout: 3_000,
        });
        await expect(section(page, 'Akcje i ETF')).toContainText(pln('7 000,00'), {
          timeout: 3_000,
        });
        await expect(total).toContainText(/\d\szł$/, { timeout: 3_000 });
        await expect(page.locator('.wealth-all__as-of')).toContainText('Stan na', {
          timeout: 3_000,
        });
      },
    );
  });

  await test.step('Krok 8: the bank account Santander oszczędnościowe and the 12-month deposit show in "Konta bankowe" and "Lokaty" with sums under the tabs', async () => {
    await newAsset(page, 'Konto bankowe');
    await dialog.getByLabel('Nazwa').fill('Santander oszczędnościowe');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Oszczędnościowe');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('1');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await clickTab(page, 'Konta bankowe');
    await accountRow(page, 'Santander oszczędnościowe')
      .getByRole('button', { name: 'Akcje dla: Santander oszczędnościowe', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, '60000');

    await openWealth(page);
    await newAsset(page, 'Lokata');
    await dialog.getByLabel('Nazwa').fill('Lokata Santander');
    await selectOption(
      page,
      dialog.getByRole('combobox', { name: 'Z konta' }),
      /^Santander oszczędnościowe \(/,
    );
    await dialog.getByLabel('Kapitał').fill('50000');
    await dialog.getByLabel('Okres', { exact: true }).fill('12');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('5.1');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await expect(
      section(page, 'Konta bankowe').getByRole('row', { name: /Santander oszczędnościowe/ }),
    ).toBeVisible();
    await expect(
      section(page, 'Lokaty').getByRole('row', { name: /Lokata Santander/ }),
    ).toBeVisible();
    await expectProjected(
      () => refetchWealth(page),
      async () => {
        await expect(tab(page, 'Konta bankowe')).toContainText(pln('10 000,00'), {
          timeout: 3_000,
        });
        await expect(tab(page, 'Lokaty')).toContainText(pln('50 000,00'), { timeout: 3_000 });
      },
    );
  });

  await test.step('Krok 9: the cash place Portfel (800 zł, then 420 EUR) shows PLN and EUR rows in the Gotówka tab, EUR with "≈ … zł" or "Kurs niedostępny"', async () => {
    await newAsset(page, 'Miejsce gotówki');
    await dialog.getByLabel('Nazwa').fill('Portfel');
    await dialog.getByRole('checkbox', { name: /^PLN/ }).setChecked(true);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await clickTab(page, 'Gotówka');
    const place = cashPlace(page, 'Portfel');
    await place.getByRole('button', { name: 'Akcje dla: Portfel · PLN', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, '800');

    await place.getByRole('button', { name: 'Akcje dla: Portfel', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Dodaj walutę', exact: true }).click();
    await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta' }), /^EUR/);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await place.getByRole('button', { name: 'Akcje dla: Portfel · EUR', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, '420');

    const pln_ = place.locator('.cash-place__balance').filter({ hasText: 'PLN' });
    const eur = place.locator('.cash-place__balance').filter({ hasText: 'EUR' });
    await expect(pln_).toContainText(pln('800,00'));
    await expect(eur).toContainText(/420,00\sEUR/);
    await expect(eur).toContainText(/≈\s?.*zł|Kurs niedostępny/);

    await openWealth(page);
    await expect(section(page, 'Gotówka').getByRole('row', { name: /Portfel/ })).toBeVisible();
  });

  await test.step('Krok 10: an EDO series, 600 pieces on a new bond account, shows in "Obligacje"; the subtabs "Moje obligacje" and "Aktualna oferta" work', async () => {
    await newAsset(page, 'Rachunek inwestycyjny');
    await dialog.getByLabel('Nazwa').fill('Obligacje PKO');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Typ' }), 'Obligacji');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await depositOnAccountPage(page, 'Obligacje PKO', '60000');

    await openWealth(page);
    await newAsset(page, 'Obligacja');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), 'Obligacje PKO');
    await selectOption(
      page,
      dialog.getByRole('combobox', { name: 'Seria z oferty' }),
      /Wprowadź ręcznie/,
    );
    await dialog.getByLabel('Nazwa').fill('EDO0136');
    await dialog.getByLabel('Kod serii').fill('EDO0136');
    await selectOption(
      page,
      dialog.getByRole('combobox', { name: 'Typ obligacji' }),
      'Dziesięcioletnie indeksowane inflacją',
    );
    await dialog.getByLabel('Liczba obligacji').fill('600');
    await dialog.getByLabel('Cena za obligację').fill('100');
    await dialog.getByLabel('Oprocentowanie w 1. okresie (%)').fill('2');
    await dialog.getByLabel('Marża (%)').fill('1.5');
    await dialog.getByLabel('Opłata za wcześniejszy wykup (za obligację)').fill('0');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await expect(section(page, 'Obligacje').getByRole('row', { name: /EDO0136/ })).toBeVisible();

    await clickTab(page, 'Obligacje');
    await expect(page).toHaveURL(/\/wealth\/bonds$/);
    await page.getByRole('tab', { name: 'Aktualna oferta', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'Aktualna oferta', selected: true })).toBeVisible();
    await page.getByRole('tab', { name: 'Moje obligacje', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'Moje obligacje', selected: true })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: 'EDO0136' })).toBeVisible();
  });

  await test.step('Krok 11: the precious metal Krugerrand 1 oz (gold, 31,10 g) shows in "Metale szlachetne" and the Metale tab shows the price per gram', async () => {
    await openWealth(page);
    await newAsset(page, 'Metal szlachetny');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Metal' }), 'Złoto');
    await dialog.getByLabel('Nazwa').fill('Krugerrand 1 oz');
    await dialog.getByLabel('Waga kruszcu w sztuce').fill('31.10');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(
      section(page, 'Metale szlachetne').getByRole('row', { name: /Krugerrand 1 oz/ }),
    ).toBeVisible();

    await clickTab(page, 'Metale szlachetne');
    await expect(page).toHaveURL(/\/wealth\/metals$/);
    const prices = page.getByRole('region', { name: 'Ceny spot' });
    await expect(prices).toContainText(/\/\sg/);
  });

  await test.step('Krok 12: typing "Cd" in "Szukaj" leaves only CD Projekt, clearing the field brings all rows back', async () => {
    await openWealth(page);
    await expect(allRow(page, 'XTB IKE')).toBeVisible();
    await page.getByLabel('Szukaj').fill('Cd');
    await expect(allRow(page, /CD Projekt/)).toBeVisible();
    await expect(page.locator('tr.wealth-all__row')).toHaveCount(1);
    await page.getByLabel('Szukaj').fill('');
    await expect(allRow(page, 'XTB IKE')).toBeVisible();
    await expect(allRow(page, 'Lokata Santander')).toBeVisible();
    await expect(allRow(page, 'Krugerrand 1 oz')).toBeVisible();
    await expect(allRow(page, 'Portfel')).toBeVisible();
    await expect(allRow(page, 'EDO0136')).toBeVisible();
  });

  await test.step('Krok 13: after assigning the deposit to the group "Poduszka finansowa", the group filter shows only the deposit while the totals stay unchanged', async () => {
    await sidebar(page).getByRole('link', { name: 'Grupy', exact: true }).click();
    await page.getByTestId('add-group').click();
    await dialog.getByLabel('Nazwa').fill('Poduszka finansowa');
    await dialog.getByRole('radio', { name: 'Czerwony' }).click();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await page
      .getByTestId('group-row')
      .filter({
        has: page.getByTestId('group-name').getByText('Poduszka finansowa', { exact: true }),
      })
      .getByTestId('select-group')
      .click();
    const panel = page.locator('mat-card.groups-page__assignment');
    await expect(panel).toBeVisible();
    await panel
      .getByTestId('asset-row')
      .filter({ hasText: 'Lokata Santander' })
      .getByRole('checkbox')
      .check();
    const save = panel.getByTestId('save-assignment');
    await save.click();
    await expect(save).toBeDisabled();

    await openWealth(page);
    await expect(allRow(page, 'XTB IKE')).toBeVisible();
    const totalBefore = await total.innerText();
    const accountsTabBefore = (await tab(page, 'Rachunki inwestycyjne').textContent()) ?? '';
    await chooseGroupFilter(page, 'Poduszka finansowa');
    await expect(page.locator('tr.wealth-all__row')).toHaveCount(1);
    await expect(allRow(page, 'Lokata Santander')).toBeVisible();
    await expect(section(page, 'Lokaty')).toContainText(pln('50 000,00'));
    await expect(total).toHaveText(totalBefore);
    await expect(tab(page, 'Rachunki inwestycyjne')).toHaveText(accountsTabBefore);
  });

  await test.step('Krok 14: with "Grupa: Wszystkie" archiving IKE do archiwum in the Rachunki inwestycyjne tab removes it from Wszystko and from the count under the tab', async () => {
    await chooseGroupFilter(page, 'Wszystkie');
    await newAsset(page, 'Rachunek inwestycyjny');
    await dialog.getByLabel('Nazwa').fill('IKE do archiwum');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(allRow(page, 'IKE do archiwum')).toBeVisible();
    await expect(tab(page, 'Rachunki inwestycyjne')).toContainText('3 rachunki');

    await clickTab(page, 'Rachunki inwestycyjne');
    await accountRow(page, 'IKE do archiwum')
      .getByRole('button', { name: 'Akcje dla: IKE do archiwum', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Archiwizuj', exact: true }).click();
    await dialog.getByRole('button', { name: 'Archiwizuj', exact: true }).click();
    await expect(dialog).toBeHidden();

    await clickTab(page, 'Wszystko');
    await expect(allRow(page, 'Lokata Santander')).toBeVisible();
    await expect(allRow(page, 'IKE do archiwum')).toHaveCount(0);
    await expect(tab(page, 'Rachunki inwestycyjne')).toContainText('2 rachunki');
  });

  await test.step('Krok 15: "Pokaż zarchiwizowane" brings IKE do archiwum back dimmed with the "Zarchiwizowany" mark and in the count under the tab; switching it off hides it again', async () => {
    const toggle = page.getByRole('switch', { name: 'Pokaż zarchiwizowane' });
    await toggle.click();
    const row = allRow(page, 'IKE do archiwum');
    await expect(row).toBeVisible();
    await expect(row).toContainText('Zarchiwizowany');
    await expect(row).toHaveClass(/wealth-all__row--archived/);
    await expect(tab(page, 'Rachunki inwestycyjne')).toContainText('3 rachunki');
    await toggle.click();
    await expect(allRow(page, 'IKE do archiwum')).toHaveCount(0);
  });

  await test.step('Krok 16: restoring IKE do archiwum from the row menu brings the row back in full colour', async () => {
    await clickTab(page, 'Rachunki inwestycyjne');
    await page.getByRole('switch', { name: 'Pokaż zarchiwizowane' }).click();
    await expect(accountRow(page, 'IKE do archiwum')).toContainText('Zarchiwizowany');
    await accountRow(page, 'IKE do archiwum')
      .getByRole('button', { name: 'Akcje dla: IKE do archiwum', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Przywróć', exact: true }).click();
    await dialog.getByRole('button', { name: 'Przywróć', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(accountRow(page, 'IKE do archiwum')).not.toContainText('Zarchiwizowany');

    await clickTab(page, 'Wszystko');
    const row = allRow(page, 'IKE do archiwum');
    await expect(row).toBeVisible();
    await expect(row).not.toHaveClass(/wealth-all__row--archived/);
    await expect(row).not.toContainText('Zarchiwizowany');
  });

  await test.step('Krok 17: the row menu "Historia transakcji" of CD Projekt opens /wealth/assets/<id>/transactions with the breadcrumb "Majątek · Akcje", five tiles and a transactions table with an icon and the sign +70', async () => {
    await allRow(page, /CD Projekt/)
      .getByRole('button', { name: 'Akcje dla: CD Projekt', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Historia transakcji' }).click();
    await expect(page).toHaveURL(/\/wealth\/assets\/[0-9a-f-]+\/transactions$/);
    assetUrl = new URL(page.url()).pathname;
    const breadcrumb = page.locator('.transactions-page__breadcrumb');
    await expect(breadcrumb.getByRole('link', { name: /Majątek/ })).toBeVisible();
    await expect(breadcrumb.getByRole('link', { name: /Akcje/ })).toBeVisible();
    for (const kpi of ['Wartość', 'Ilość', 'Śr. cena', 'Zysk/strata', 'Dywidenda']) {
      await expect(page.locator('.transactions-page__kpi dt', { hasText: kpi })).toBeVisible();
    }
    await expect(page.locator('.transactions-page__kpi')).toHaveCount(5);
    const buy = page.getByRole('row').filter({ hasText: 'Kupno' });
    await expect(buy.locator('.transactions-page__type-icon')).toBeVisible();
    await expect(buy).toContainText('+70');
  });

  await test.step('Krok 18: selling 10 at 210,00 zł adds a "Sprzedaż" row with the sign −10 and a differently coloured icon, and the Ilość tile shows 60', async () => {
    await page.getByRole('button', { name: 'Sprzedaj', exact: true }).click();
    await dialog.getByLabel(/^Ilość/).fill('10');
    await dialog.getByLabel(/^Cena jednostkowa/).fill('210');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    const sell = page.getByRole('row').filter({ hasText: 'Sprzedaż' });
    await expect(sell).toContainText('−10');
    await expect(sell.locator('.transactions-page__type-icon')).toHaveClass(
      /transactions-page__type-icon--negative/,
    );
    await expect(
      page.getByRole('row').filter({ hasText: 'Kupno' }).locator('.transactions-page__type-icon'),
    ).not.toHaveClass(/transactions-page__type-icon--negative/);
    await expect(
      page.locator('.transactions-page__kpi').filter({ hasText: 'Ilość' }),
    ).toContainText('60');
  });

  await test.step('Krok 19: the breadcrumb "Majątek" returns to /wealth and "Akcje" opens the Akcje i ETF tab', async () => {
    await page
      .locator('.transactions-page__breadcrumb')
      .getByRole('link', { name: /Majątek/ })
      .click();
    await expect(page).toHaveURL(/\/wealth$/);
    await page.goto(assetUrl);
    await page
      .locator('.transactions-page__breadcrumb')
      .getByRole('link', { name: /Akcje/ })
      .click();
    await expect(page).toHaveURL(/\/wealth\/securities$/);
    await expect(page.getByRole('tab', { name: /^Akcje i ETF/, selected: true })).toBeVisible();
  });

  await test.step('Krok 20: the Pulpit cards "Rachunki" and "Najbliższe" link under /wealth and the accounts card opens the account details', async () => {
    await sidebar(page).getByRole('link', { name: 'Pulpit', exact: true }).click();
    await expectProjected(
      async () => {
        await page.goto('/dashboard');
      },
      async () => {
        await expect(
          page.locator('.accounts-card').getByRole('link', { name: 'XTB IKE', exact: true }),
        ).toBeVisible({ timeout: 3_000 });
      },
    );
    for (const link of await page.locator('.upcoming-card__name').all()) {
      await expect(link).toHaveAttribute('href', /^\/wealth(\/|$)/);
    }
    await page
      .locator('.accounts-card')
      .getByRole('link', { name: 'XTB IKE', exact: true })
      .click();
    await expect(page).toHaveURL(/\/wealth\/accounts\/[0-9a-f-]+$/);
  });

  await test.step('Krok 21: /accounts and /wealth/xyz show "Nie znaleziono strony." without a redirect', async () => {
    await page.goto('/accounts');
    await expect(page.getByText('Nie znaleziono strony.')).toBeVisible();
    await expect(page).toHaveURL(/\/accounts$/);
    await page.goto('/wealth/xyz');
    await expect(page.getByText('Nie znaleziono strony.')).toBeVisible();
    await expect(page).toHaveURL(/\/wealth\/xyz$/);
  });

  await test.step('Krok 22: at about 1000 px the rail is 72 px wide with four entries and Majątek is highlighted on every /wealth/... page, also on the asset page', async () => {
    await page.setViewportSize(RAIL);
    for (const path of ['/wealth', '/wealth/securities', '/wealth/accounts', assetUrl]) {
      await page.goto(path);
      await expect.poll(async () => (await sidebar(page).boundingBox())?.width).toBe(72);
      await expect(page.getByTestId('nav-list').locator('.shell__nav-link')).toHaveCount(4);
      await expect(sidebar(page).getByRole('link', { name: 'Majątek' })).toHaveAttribute(
        'aria-current',
        'page',
      );
    }
  });

  await test.step('Krok 23: at about 600 px the bottom navigation has four entries and no "Więcej", Majątek is highlighted and the tabs scroll horizontally', async () => {
    await page.setViewportSize(NARROW);
    await page.goto('/wealth');
    const bottom = page.getByTestId('bottom-nav');
    await expect(bottom).toBeVisible();
    await expect(bottom.getByRole('link')).toHaveCount(4);
    await expect(bottom.getByRole('link', { name: 'Pulpit' })).toBeVisible();
    await expect(bottom.getByRole('link', { name: 'Planowanie' })).toBeVisible();
    await expect(bottom.getByRole('link', { name: 'Grupy' })).toBeVisible();
    await expect(bottom.getByRole('button')).toHaveCount(0);
    await expect(bottom.getByText('Więcej')).toHaveCount(0);
    await expect(bottom.getByRole('link', { name: 'Majątek' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(page.locator('nav.wealth__tabs')).toHaveClass(
      /mat-mdc-tab-header-pagination-controls-enabled/,
    );
  });
});
