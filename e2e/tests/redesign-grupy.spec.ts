import type { Locator, Page } from '@playwright/test';
import { expect, openSelect, test } from './fixtures';
import { expectProjected } from './projection';

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

const WIDE = { width: 1280, height: 800 };
const SYNC_TIMEOUT = 120_000;

function sidebar(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function dialogOf(page: Page): Locator {
  return page.getByRole('dialog');
}

function tab(page: Page, name: string): Locator {
  return page.getByRole('tab', { name: new RegExp(`^${name}(\\s|$)`) });
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

async function openWealth(page: Page): Promise<void> {
  await sidebar(page).getByRole('link', { name: 'Majątek', exact: true }).click();
  await expect(page).toHaveURL(/\/wealth$/);
}

async function openWealthTab(page: Page, name: string): Promise<void> {
  await openWealth(page);
  await tab(page, name).focus();
  await page.keyboard.press('Enter');
}

async function newAsset(page: Page, entry: string): Promise<void> {
  await page.getByRole('button', { name: 'Nowe aktywo' }).first().click();
  await page.getByRole('menuitem', { name: entry, exact: true }).click();
}

async function cashOperation(dialog: Locator, amount: string): Promise<void> {
  await dialog.getByLabel('Kwota').fill(amount);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function createCashPlace(page: Page, name: string, deposit: string): Promise<void> {
  const dialog = dialogOf(page);
  await openWealth(page);
  await newAsset(page, 'Miejsce gotówki');
  await dialog.getByLabel('Nazwa').fill(name);
  await dialog.getByRole('checkbox', { name: /^PLN/ }).setChecked(true);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();

  await tab(page, 'Gotówka').focus();
  await page.keyboard.press('Enter');
  await page
    .locator('mat-card.cash-place')
    .filter({ has: page.getByRole('heading', { name, exact: true }) })
    .getByRole('button', { name: `Akcje dla: ${name} · PLN`, exact: true })
    .click();
  await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
  await cashOperation(dialog, deposit);
}

async function syncNow(page: Page): Promise<void> {
  await sidebar(page).getByRole('link', { name: 'Ustawienia', exact: true }).click();
  const button = page.getByRole('button', { name: 'Synchronizuj teraz' });
  await button.click();
  await expect(button).toBeEnabled();
}

async function openGroups(page: Page): Promise<void> {
  await sidebar(page).getByRole('link', { name: 'Grupy', exact: true }).click();
  await expect(page).toHaveURL(/\/groups$/);
  await expect(page.getByRole('heading', { name: /^Grupy/ })).toBeVisible();
}

async function refetchGroups(page: Page): Promise<void> {
  await page.goto('/groups');
  await expect(page.getByRole('heading', { name: /^Grupy/ })).toBeVisible();
}

function card(page: Page, name: string): Locator {
  return page
    .getByTestId('group-card')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

function stripItem(page: Page, label: string): Locator {
  return page.locator('.summary-strip__item').filter({
    has: page.locator('dt', { hasText: new RegExp(`^${label}$`) }),
  });
}

function panelOf(page: Page): Locator {
  return page.getByTestId('assets-panel');
}

async function openCardPanel(page: Page, name: string): Promise<void> {
  await card(page, name).getByTestId('select-group').click();
  await expect(panelOf(page).getByTestId('assets-panel-title')).toContainText(name);
}

async function savePanel(page: Page): Promise<void> {
  await panelOf(page).getByTestId('save-assignment').click();
  await expect(panelOf(page).getByTestId('assets-panel-title')).toHaveCount(0);
}

async function openCardMenu(page: Page, name: string, item: 'edit-group' | 'delete-group') {
  await card(page, name).getByTestId('group-menu').click();
  await page.getByTestId(item).click();
}

async function addAllocationRow(
  page: Page,
  dialog: Locator,
  index: number,
  assetClass: string,
  percent: string,
): Promise<void> {
  await dialog.getByTestId('add-allocation-row').click();
  await selectOption(page, dialog.getByRole('combobox', { name: 'Klasa' }).nth(index), assetClass);
  await dialog.getByRole('spinbutton', { name: 'Udział (%)' }).nth(index).fill(percent);
}

function kindToggle(dialog: Locator, kind: string): Locator {
  return dialog.getByTestId('kind-toggle').getByText(kind, { exact: true });
}

test('redesign Grupy: cards, summary, assets panel, dialog and deletion', async ({
  signedInPage: page,
}) => {
  test.setTimeout(900_000);
  await page.setViewportSize(WIDE);
  const dialog = dialogOf(page);

  await test.step('Krok 1: the empty Grupy page shows the heading, the subtitle, the empty state with "Dodaj grupę" and no strip or "Bez grupy" card', async () => {
    await openGroups(page);
    await expect(page.getByRole('heading', { name: /^Grupy/ })).toBeVisible();
    await expect(
      page.getByText('Cele z kwotą docelową, wpłatą miesięczną i alokacją'),
    ).toBeVisible();
    const empty = page.getByTestId('group-empty');
    await expect(empty).toContainText('Nie masz jeszcze grup.');
    await expect(empty.getByRole('button', { name: 'Dodaj grupę' })).toBeVisible();
    await expect(page.locator('.summary-strip__item')).toHaveCount(0);
    await expect(page.getByTestId('ungrouped-card')).toHaveCount(0);
  });

  await test.step('Krok 2: the savings account (60 000 zł deposit) and the 12-month deposit of 50 000 zł give the account 10 000,00 zł and the deposit 50 000,00 zł', async () => {
    await openWealth(page);
    await newAsset(page, 'Konto bankowe');
    await dialog.getByLabel('Nazwa').fill('Santander oszczędnościowe');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Oszczędnościowe');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('1');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await tab(page, 'Konta bankowe').focus();
    await page.keyboard.press('Enter');
    await page
      .getByRole('row')
      .filter({ has: page.getByRole('link', { name: 'Santander oszczędnościowe', exact: true }) })
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

    await expectProjected(
      async () => {
        await page.goto('/wealth');
        await expect(page.getByRole('heading', { name: 'Majątek', exact: true })).toBeVisible();
      },
      async () => {
        await expect(tab(page, 'Konta bankowe')).toContainText(pln('10 000,00'), {
          timeout: 3_000,
        });
        await expect(tab(page, 'Lokaty')).toContainText(pln('50 000,00'), { timeout: 3_000 });
      },
    );
  });

  await test.step('Krok 3: the cash place Portfel with 800 zł and "Synchronizuj teraz" bring the Pulpit net worth to 60 800,00 zł', async () => {
    await createCashPlace(page, 'Portfel', '800');
    await syncNow(page);
    await expectProjected(
      async () => {
        await page.goto('/dashboard');
      },
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(pln('60 800,00'), {
          timeout: 3_000,
        });
      },
      { timeout: SYNC_TIMEOUT },
    );
  });

  await test.step('Krok 4: the cushion "Poduszka finansowa" with a live allocation bar saves into a card with the shield marker, 0,00 zł / 80 000,00 zł, an empty progress bar and the contribution line', async () => {
    await openGroups(page);
    await page.getByTestId('add-group').click();
    await dialog.getByLabel('Nazwa').fill('Poduszka finansowa');
    await dialog.getByRole('radio', { name: 'Niebieski' }).click();
    await kindToggle(dialog, 'Poduszka').click();
    await dialog.getByLabel('Wpłata miesięczna (PLN)').fill('1000');
    await dialog.getByLabel('Cel (PLN)').fill('80000');
    await addAllocationRow(page, dialog, 0, 'Lokata', '70');
    await addAllocationRow(page, dialog, 1, 'Konta bankowe', '30');
    await expect(dialog.locator('.group-form-dialog__live-segment')).toHaveCount(2);
    await expect(dialog.getByTestId('allocation-sum')).toContainText('Suma: 100% z 100%');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    const cushion = card(page, 'Poduszka finansowa');
    await expect(cushion).toBeVisible();
    await expect(cushion.locator('.group-card__bar')).toHaveCSS('background-color', /^rgb/);
    await expect(cushion.getByTestId('group-kind')).toContainText('Poduszka');
    await expect(cushion.getByTestId('group-kind').locator('mat-icon')).toHaveText('shield');
    await expect(cushion.getByTestId('group-value')).toContainText(pln('0,00'));
    await expect(cushion.getByTestId('group-value')).toContainText(/\/\s80\s?000,00\szł/);
    await expect(cushion.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
    await expect(cushion.getByTestId('group-contribution')).toContainText(
      /Wpłata miesięczna:\s1\s?000,00\szł/,
    );
    await expect(cushion.getByTestId('group-count')).toContainText('Aktywa: 0');
  });

  await test.step('Krok 5: a first rate of 50 % shows "Suma: 80% z 100%" and "brakuje 20 pp" with "Zapisz" disabled; the same class twice shows the duplicate message', async () => {
    await page.getByTestId('add-group').click();
    await dialog.getByLabel('Nazwa').fill('Tymczasowa');
    await dialog.getByRole('radio', { name: 'Zielony' }).click();
    await addAllocationRow(page, dialog, 0, 'Lokata', '50');
    await addAllocationRow(page, dialog, 1, 'Konta bankowe', '30');
    await expect(dialog.getByTestId('allocation-sum')).toContainText('Suma: 80% z 100%');
    await expect(dialog.getByTestId('allocation-sum')).toContainText('brakuje 20 pp');
    await expect(dialog.getByRole('button', { name: 'Zapisz' })).toBeDisabled();

    await selectOption(page, dialog.getByRole('combobox', { name: 'Klasa' }).nth(1), 'Lokata');
    await expect(dialog.getByTestId('allocation-error')).toContainText(
      'Każda klasa może wystąpić tylko raz.',
    );
    await expect(dialog.getByRole('button', { name: 'Zapisz' })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 6: "Wakacje 2027" (Zwykła, no target, no allocation, 0 zł) gets a card without progress, allocation and contribution; the first colour is crossed out; the counter reads "2 z 12"', async () => {
    await page.getByTestId('add-group').click();
    await dialog.getByLabel('Nazwa').fill('Wakacje 2027');
    await kindToggle(dialog, 'Zwykła').click();
    await dialog.getByLabel('Wpłata miesięczna (PLN)').fill('0');
    const first = dialog.getByRole('radio', { name: 'Niebieski' });
    await expect(first).toBeDisabled();
    await expect(first.locator('mat-icon')).toHaveText('close');
    await expect(dialog.getByTestId('palette-caption')).toContainText('Wybrany:');
    await dialog.getByRole('radio', { name: 'Zielony' }).click();
    await expect(dialog.getByTestId('palette-caption')).toContainText('Zielony');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    const holiday = card(page, 'Wakacje 2027');
    await expect(holiday).toBeVisible();
    await expect(holiday.getByRole('progressbar')).toHaveCount(0);
    await expect(holiday.locator('.allocation-bar__legend')).toHaveCount(0);
    await expect(holiday.getByTestId('group-contribution')).toHaveCount(0);
    await expect(holiday.getByTestId('group-kind')).toContainText('Zwykła');
    await expect(page.getByTestId('group-counter')).toContainText('2 z 12');
  });

  await test.step('Krok 7: the strip shows "W grupach" 0,00 zł, "Bez grupy" 60 800,00 zł and "Wpłaty miesięczne razem" 1 000,00 zł, and the link leads to Planowanie', async () => {
    await expectProjected(
      () => refetchGroups(page),
      async () => {
        await expect(stripItem(page, 'W grupach')).toContainText(pln('0,00'), { timeout: 3_000 });
        await expect(stripItem(page, 'Bez grupy')).toContainText(pln('60 800,00'), {
          timeout: 3_000,
        });
        await expect(stripItem(page, 'Wpłaty miesięczne razem')).toContainText(pln('1 000,00'), {
          timeout: 3_000,
        });
      },
    );
    await page.getByRole('link', { name: 'w Planowaniu jako wydatki →' }).click();
    await expect(page).toHaveURL(/\/planning$/);
    await openGroups(page);
  });

  await test.step('Krok 8: the "Aktywa" side panel of "Poduszka finansowa" assigns the deposit by class filter and the account by text filter; the card shows "Aktywa: 2"', async () => {
    await openCardPanel(page, 'Poduszka finansowa');
    const panel = panelOf(page);
    await selectOption(page, panel.getByRole('combobox', { name: 'Klasa' }), 'Lokata');
    await panel.getByTestId('select-all-shown').click();
    await selectOption(page, panel.getByRole('combobox', { name: 'Klasa' }), 'Wszystkie klasy');
    await panel.getByTestId('asset-filter').fill('Santander');
    await panel.getByTestId('select-all-shown').click();
    await savePanel(page);
    await expect(card(page, 'Poduszka finansowa').getByTestId('group-count')).toContainText(
      'Aktywa: 2',
    );
  });

  await test.step('Krok 9: after a refresh the card shows 60 000,00 zł / 80 000,00 zł, 75 %, "Brakuje: 20 000,00 zł.", the target and actual allocation with deviations, the flag, and the strip 60 000,00 / 800,00 zł', async () => {
    await expectProjected(
      () => refetchGroups(page),
      async () => {
        const cushion = card(page, 'Poduszka finansowa');
        await expect(cushion.getByTestId('group-value')).toContainText(pln('60 000,00'), {
          timeout: 3_000,
        });
        await expect(cushion.getByTestId('group-value')).toContainText(/\/\s80\s?000,00\szł/, {
          timeout: 3_000,
        });
        await expect(cushion.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '75', {
          timeout: 3_000,
        });
        await expect(cushion.getByTestId('group-progress-caption')).toContainText(
          /Brakuje:\s20\s?000,00\szł\./,
          { timeout: 3_000 },
        );
        const deposit = cushion
          .locator('.allocation-bar__legend-row')
          .filter({ hasText: 'Lokata' });
        await expect(deposit).toContainText(/70\s?%/, { timeout: 3_000 });
        await expect(deposit).toContainText(/83,3\s?%/, { timeout: 3_000 });
        await expect(deposit).toContainText(/\+13,3\s?pp/, { timeout: 3_000 });
        await expect(deposit.locator('.allocation-bar__deviation-icon')).toHaveCount(1, {
          timeout: 3_000,
        });
        const bank = cushion
          .locator('.allocation-bar__legend-row')
          .filter({ hasText: 'Konta bankowe' });
        await expect(bank).toContainText(/30\s?%/, { timeout: 3_000 });
        await expect(bank).toContainText(/16,7\s?%/, { timeout: 3_000 });
        await expect(bank).toContainText(/[−-]13,3\s?pp/, { timeout: 3_000 });
        await expect(bank.locator('.allocation-bar__deviation-icon')).toHaveCount(1, {
          timeout: 3_000,
        });
        await expect(cushion.getByTestId('group-deviation-flag')).toContainText(
          'odchylenie 13,3 pp',
          { timeout: 3_000 },
        );
        await expect(stripItem(page, 'W grupach')).toContainText(pln('60 000,00'), {
          timeout: 3_000,
        });
        await expect(stripItem(page, 'Bez grupy')).toContainText(pln('800,00'), {
          timeout: 3_000,
        });
      },
    );
  });

  await test.step('Krok 10: the dashed "Bez grupy" card shows 800,00 zł, "Aktywa: 1", no menu, and its panel is read-only with "Portfel" and the hint', async () => {
    const ungrouped = page.getByTestId('ungrouped-card');
    await expect(ungrouped.getByTestId('ungrouped-value')).toContainText(pln('800,00'));
    await expect(ungrouped.getByTestId('ungrouped-count')).toContainText('Aktywa: 1');
    await expect(ungrouped.getByTestId('group-menu')).toHaveCount(0);
    await ungrouped.getByTestId('ungrouped-assets').click();
    const panel = panelOf(page);
    await expect(panel.getByTestId('assets-panel-title')).toHaveText('Bez grupy');
    await expect(panel.getByTestId('asset-row').filter({ hasText: 'Portfel' })).toBeVisible();
    await expect(panel.getByRole('checkbox')).toHaveCount(0);
    await expect(panel.getByTestId('save-assignment')).toHaveCount(0);
    await expect(panel.getByTestId('ungrouped-hint')).toContainText(
      'otwórz panel Aktywa na karcie grupy',
    );
    await panel.getByTestId('close-assets-panel').click();
    await expect(panel.getByTestId('assets-panel-title')).toHaveCount(0);
  });

  await test.step('Krok 11: editing "Poduszka finansowa" to a 60 000 zł target shows a full bar with "Cel osiągnięty"', async () => {
    await openCardMenu(page, 'Poduszka finansowa', 'edit-group');
    await expect(dialog.getByRole('heading', { name: 'Edytuj grupę' })).toBeVisible();
    await dialog.getByLabel('Cel (PLN)').fill('60000');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    const cushion = card(page, 'Poduszka finansowa');
    await expect(cushion.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '100');
    await expect(cushion.getByTestId('group-progress-caption')).toContainText('Cel osiągnięty');
    await expect(cushion.getByTestId('group-progress-caption').locator('mat-icon')).toHaveText(
      'check_circle',
    );
  });

  await test.step('Krok 12: without a target the cushion cannot be saved; as "Zwykła" the target is optional and "Zapisz" is enabled', async () => {
    await openCardMenu(page, 'Poduszka finansowa', 'edit-group');
    const target = dialog.getByLabel('Cel (PLN)');
    await expect(async () => {
      await target.fill('');
      await expect(target).toHaveValue('', { timeout: 1_000 });
    }).toPass({ timeout: 15_000 });
    await expect(dialog.getByRole('button', { name: 'Zapisz' })).toBeDisabled();
    await kindToggle(dialog, 'Zwykła').click();
    await expect(dialog.getByRole('button', { name: 'Zapisz' })).toBeEnabled();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(card(page, 'Poduszka finansowa').getByTestId('group-kind')).toContainText(
      'Poduszka',
    );
  });

  await test.step('Krok 13: a new cash place assigned to the group is valued after "Synchronizuj teraz": no "wartość niepełna" message and the value 60 100,00 zł (the pre-sync incomplete state is tylko manualnie: the e2e stack syncs every 15 s)', async () => {
    await createCashPlace(page, 'Skarbonka', '100');
    await openGroups(page);
    await openCardPanel(page, 'Poduszka finansowa');
    await panelOf(page).getByTestId('asset-filter').fill('Skarbonka');
    await panelOf(page).getByTestId('select-all-shown').click();
    await savePanel(page);

    const cushion = card(page, 'Poduszka finansowa');
    await expect(cushion.getByTestId('group-count')).toContainText('Aktywa: 3');

    await syncNow(page);
    await expectProjected(
      () => refetchGroups(page),
      async () => {
        await expect(cushion.getByTestId('group-value')).toContainText(pln('60 100,00'), {
          timeout: 3_000,
        });
        await expect(cushion.getByTestId('group-incomplete')).toHaveCount(0, { timeout: 3_000 });
      },
      { timeout: SYNC_TIMEOUT },
    );
  });

  await test.step('Krok 14: deleting "Wakacje 2027" asks "Usunąć grupę?" with a destructive "Usuń" and focus on "Anuluj"; cancelling keeps it, confirming removes it, closes the panel and the counter reads "1 z 12"', async () => {
    await openCardPanel(page, 'Wakacje 2027');
    await openCardMenu(page, 'Wakacje 2027', 'delete-group');
    await expect(dialog.getByRole('heading', { name: 'Usunąć grupę?' })).toBeVisible();
    await expect(dialog).toContainText(/Jej historia trafi do „Bez grupy/);
    await expect(dialog.getByRole('button', { name: 'Usuń', exact: true })).toHaveClass(
      /confirm-dialog__confirm--destructive/,
    );
    await expect(dialog.getByRole('button', { name: 'Anuluj' })).toBeFocused();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(card(page, 'Wakacje 2027')).toBeVisible();

    await openCardMenu(page, 'Wakacje 2027', 'delete-group');
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(card(page, 'Wakacje 2027')).toHaveCount(0);
    await expect(panelOf(page).getByTestId('assets-panel-title')).toHaveCount(0);
    await expect(page.getByTestId('group-counter')).toContainText('1 z 12');
  });
});
