import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
  type Credentials,
  openSelect,
  WEALTH_TABS,
  wealthTab,
} from './fixtures';
import { expectProjected } from './projection';

const COLORS = [
  'blue',
  'green',
  'orange',
  'purple',
  'red',
  'teal',
  'pink',
  'amber',
  'indigo',
  'lime',
  'cyan',
  'brown',
];

function num(amount: string): string {
  return amount.replace(/ /g, '\\s?');
}

function pln(amount: string): RegExp {
  return new RegExp(`${num(amount)}\\szł`);
}

function legendName(label: string, value: string, target?: { amount: string; percent: number }) {
  const body = target
    ? `${num(value)}\\s/\\s${num(target.amount)}\\szł\\s\\(${target.percent}%\\)`
    : `${num(value)}\\szł`;
  return new RegExp(`^\\s*${label} — ${body}`);
}

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

function groupCombobox(dialog: Locator, name = 'Grupa'): Locator {
  return dialog.getByRole('combobox', { name, exact: true });
}

function groupRow(page: Page, name: string): Locator {
  return page
    .getByTestId('group-row')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

function assignmentPanel(page: Page): Locator {
  return page.locator('mat-card.groups-page__assignment');
}

function assetRow(page: Page, name: string): Locator {
  return assignmentPanel(page).getByTestId('asset-row').filter({ hasText: name });
}

function groupLegend(page: Page): Locator {
  return page.locator('.dashboard-page__group-legend');
}

function groupLegendEntry(page: Page, text: RegExp): Locator {
  return groupLegend(page).locator('.dashboard-page__legend-item').filter({ hasText: text });
}

function groupLegendItem(page: Page, label: string): Locator {
  return groupLegendEntry(page, new RegExp(`^\\s*${label} — `));
}

function filterButton(page: Page): Locator {
  return page.locator('.net-worth-chart__filter');
}

async function chooseFilter(page: Page, name: string): Promise<void> {
  await filterButton(page).click();
  await page.getByRole('menuitem', { name, exact: true }).click();
  await expect(filterButton(page)).toContainText(name);
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

function historyImage(page: Page): Locator {
  return page.getByRole('img', { name: /^Wykres historii wartości/ });
}

function historyChart(page: Page): Locator {
  return historyImage(page).or(page.getByText('Za mało historii, aby narysować wykres.'));
}

async function createGroup(
  page: Page,
  group: { name: string; color: string; target?: string },
): Promise<void> {
  await page.getByTestId('add-group').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(group.name);
  await dialog.getByRole('radio', { name: group.color }).click();
  if (group.target) {
    await dialog.getByLabel('Cel (PLN)').fill(group.target);
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function fillSettled(input: Locator, value: string): Promise<void> {
  await expect(async () => {
    await input.fill(value);
    await expect(input).toHaveValue(value, { timeout: 1_000 });
  }).toPass({ timeout: 15_000 });
}

async function cashOperation(
  dialog: Locator,
  operation: { amount: string; date?: string },
): Promise<void> {
  await fillSettled(dialog.getByLabel('Kwota'), operation.amount);
  if (operation.date) {
    await fillSettled(dialog.getByLabel('Data', { exact: true }), operation.date);
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  try {
    await expect(dialog).toBeHidden();
  } catch (error) {
    throw new Error(`dialog stayed open: ${await dialog.innerText()}`, { cause: error });
  }
}

function bankRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

function cashPlace(page: Page, name: string): Locator {
  return page
    .locator('mat-card.cash-place')
    .filter({ has: page.getByRole('heading', { name, exact: true }) });
}

async function syncNow(page: Page): Promise<void> {
  await openPage(page, 'Ustawienia');
  const button = page.getByRole('button', { name: 'Synchronizuj teraz' });
  await button.click();
  await expect(button).toBeEnabled();
}

async function pickInstrument(page: Page, dialog: Locator, query: string, ticker: string) {
  await dialog.getByLabel('Instrument finansowy').fill(query);
  await page.getByRole('option', { name: new RegExp(ticker.replace('.', '\\.')) }).click();
  await expect(dialog.getByText(`Zweryfikowano: ${ticker}`)).toBeVisible();
}

async function openBuyStockDialog(
  page: Page,
  account: string,
  expectedGroup: string,
  tab?: string,
): Promise<Locator> {
  await openPage(page, 'Akcje i ETF');
  if (tab) {
    await page.getByRole('tab', { name: tab, exact: true }).click();
  }
  await page.getByRole('button', { name: 'Dodaj', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), account);
  await expect(groupCombobox(dialog)).toContainText(expectedGroup);
  return dialog;
}

async function openBondDialog(page: Page, account: string, expectedGroup: string) {
  await openPage(page, 'Obligacje');
  await page
    .getByRole('button', { name: /^(Kup obligacje|Zapisz pierwszy zakup obligacji)$/ })
    .first()
    .click();
  const dialog = page.getByRole('dialog');
  await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), account);
  await expect(groupCombobox(dialog)).toContainText(expectedGroup);
  return dialog;
}

async function fillBond(
  page: Page,
  dialog: Locator,
  bond: { code: string; type: string; margin: string },
): Promise<void> {
  await selectOption(
    page,
    dialog.getByRole('combobox', { name: 'Seria z oferty' }),
    /Wprowadź ręcznie/,
  );
  await dialog.getByLabel('Nazwa').fill(bond.code);
  await dialog.getByLabel('Kod serii').fill(bond.code);
  await selectOption(page, dialog.getByRole('combobox', { name: 'Typ obligacji' }), bond.type);
  await dialog.getByLabel('Liczba obligacji').fill('10');
  await dialog.getByLabel('Cena za obligację').fill('100');
  await dialog.getByLabel('Oprocentowanie w 1. okresie (%)').fill('2');
  await dialog.getByLabel('Marża (%)').fill(bond.margin);
  await dialog.getByLabel('Opłata za wcześniejszy wykup (za obligację)').fill('0');
}

async function loginToken(request: APIRequestContext, user: Credentials): Promise<string> {
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  return ((await login.json()) as { accessToken: string }).accessToken;
}

async function dashboardRefetch(page: Page): Promise<void> {
  await page.goto('/dashboard');
}

async function expectDashboard(
  page: Page,
  expected: { netWorth?: string; groups: RegExp[]; absent?: string },
): Promise<void> {
  await expectProjected(
    () => dashboardRefetch(page),
    async () => {
      if (expected.netWorth) {
        await expect(page.getByTestId('net-worth')).toContainText(pln(expected.netWorth), {
          timeout: 2_000,
        });
      }
      for (const entry of expected.groups) {
        await expect(groupLegendEntry(page, entry)).toBeVisible({
          timeout: 2_000,
        });
      }
      if (expected.absent) {
        const row = groupLegendItem(page, expected.absent);
        if ((await row.count()) > 0) {
          await expect(row).toContainText(pln('0,00'), { timeout: 2_000 });
        }
      }
    },
    { timeout: 60_000 },
  );
}

async function expectFilteredChart(
  page: Page,
  filter: { group?: string; assetClass?: string },
  end: RegExp,
  start?: RegExp,
): Promise<void> {
  await expectProjected(
    async () => {
      await dashboardRefetch(page);
      if (filter.assetClass) {
        await chooseFilter(page, filter.assetClass);
      }
      if (filter.group) {
        await chooseFilter(page, filter.group);
      }
    },
    async () => {
      if (filter.group) {
        await expect(filterButton(page)).toContainText(filter.group, { timeout: 2_000 });
        await expect(page.locator('.net-worth-chart__caption')).toContainText(filter.group, {
          timeout: 2_000,
        });
      }
      await expect(historyImage(page)).toHaveAccessibleName(end, { timeout: 2_000 });
      if (start) {
        await expect(historyImage(page)).toHaveAccessibleName(start, { timeout: 2_000 });
      }
    },
    { timeout: 60_000 },
  );
}

async function saveAssignment(page: Page): Promise<void> {
  const save = assignmentPanel(page).getByTestId('save-assignment');
  await save.click();
  await expect(save).toBeDisabled();
}

async function openAssignment(page: Page, group: string): Promise<void> {
  await openPage(page, 'Grupy');
  await groupRow(page, group).getByTestId('select-group').click();
  await expect(assignmentPanel(page)).toBeVisible();
}

test('asset groups: the dashboard invites to create groups once there is any wealth', async ({
  signedInPage: page,
  user,
  request,
}) => {
  const accessToken = await loginToken(request, user);
  const account = await createAccountThroughApi(request, accessToken, {
    name: 'Broker',
    type: 0,
    wrapper: 0,
  });
  await depositCashThroughApi(request, accessToken, account.cashAssetId, {
    quantity: 1000,
    date: warsawDate(),
  });

  await test.step('Krok 1: without groups the Wg grup card shows a hint linking to groups instead of a chart', async () => {
    await expectProjected(
      () => dashboardRefetch(page),
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(pln('1 000,00'), {
          timeout: 2_000,
        });
      },
    );
    await expect(page.getByRole('heading', { name: 'Wg grup' })).toBeVisible();
    await expect(page.getByText('Podziel majątek na grupy', { exact: false })).toBeVisible();
    await expect(groupLegend(page)).toHaveCount(0);
    await page.getByRole('link', { name: 'Przejdź do grup' }).click();
    await expect(page).toHaveURL(/\/groups$/);
  });
});

test('asset groups: groups split the wealth on the dashboard, filter the history and follow assignment', async ({
  signedInPage: page,
  user,
  request,
}) => {
  const accessToken = await loginToken(request, user);
  test.setTimeout(480_000);
  const dialog = page.getByRole('dialog');

  await test.step('Krok 1: a fresh dashboard shows the empty state and no Wg grup chart', async () => {
    await expect(page.getByText('Nie wprowadzono jeszcze żadnego majątku.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Wg grup' })).toHaveCount(0);
  });

  await test.step('Krok 2: Emerytura (blue), Inwestycje (green) and Poduszka (orange, target 30 000 zł) exist with 0 assets', async () => {
    await openPage(page, 'Grupy');
    await expect(page.getByTestId('group-empty')).toBeVisible();
    await createGroup(page, { name: 'Emerytura', color: 'Niebieski' });
    await createGroup(page, { name: 'Inwestycje', color: 'Zielony' });
    await createGroup(page, { name: 'Poduszka', color: 'Pomarańczowy', target: '30000' });
    await expect(page.getByTestId('group-row')).toHaveCount(3);
    for (const name of ['Emerytura', 'Inwestycje', 'Poduszka']) {
      await expect(groupRow(page, name).getByTestId('group-count')).toContainText('Aktywa: 0');
    }
    await expect(groupRow(page, 'Poduszka').getByTestId('group-target')).toContainText(
      /Cel:\s30\s?000,00\szł/,
    );
  });

  await test.step('Krok 3: a group named poduszka is refused as a duplicate', async () => {
    await page.getByTestId('add-group').click();
    await dialog.getByLabel('Nazwa').fill('poduszka');
    await dialog.getByRole('radio', { name: 'Czerwony' }).click();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog.getByText('Grupa o tej nazwie już istnieje.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('group-row')).toHaveCount(3);
  });

  await test.step('Krok 4: the colours already used are disabled in the palette', async () => {
    await page.getByTestId('add-group').click();
    for (const color of ['Niebieski', 'Zielony', 'Pomarańczowy']) {
      await expect(dialog.getByRole('radio', { name: color })).toBeDisabled();
    }
    await expect(dialog.getByRole('radio', { name: 'Czerwony' })).toBeEnabled();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 5: Broker (Inwestycje), Obligacje (Emerytura), Oszczędnościowe (Poduszka) and Sejf (Poduszka) are created with the Grupa field', async () => {
    await openPage(page, 'Rachunki inwestycyjne');
    await page
      .getByRole('button', { name: /^(Nowy rachunek|Utwórz pierwszy rachunek)$/ })
      .first()
      .click();
    await dialog.getByLabel('Nazwa').fill('Broker');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Typ' }), 'Maklerski');
    await expect(groupCombobox(dialog, 'Domyślna grupa')).toBeVisible();
    await selectOption(page, groupCombobox(dialog, 'Domyślna grupa'), 'Inwestycje');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await page.getByRole('button', { name: 'Nowy rachunek' }).first().click();
    await dialog.getByLabel('Nazwa').fill('Obligacje');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Typ' }), 'Obligacji');
    await selectOption(page, groupCombobox(dialog, 'Domyślna grupa'), 'Emerytura');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(
      page.getByRole('table').getByRole('link', { name: 'Broker', exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole('table').getByRole('link', { name: 'Obligacje', exact: true }),
    ).toBeVisible();

    await openPage(page, 'Konta bankowe');
    await page
      .getByRole('button', { name: /^(Dodaj konto|Dodaj pierwsze konto bankowe)$/ })
      .first()
      .click();
    await dialog.getByLabel('Nazwa').fill('Oszczędnościowe');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Waluta' }), /^PLN/);
    await selectOption(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Oszczędnościowe');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('0');
    await expect(groupCombobox(dialog)).toBeVisible();
    await selectOption(page, groupCombobox(dialog), 'Poduszka');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(bankRow(page, 'Oszczędnościowe')).toBeVisible();

    await openPage(page, 'Gotówka');
    await page.getByRole('button', { name: 'Dodaj miejsce', exact: true }).first().click();
    await dialog.getByLabel('Nazwa').fill('Sejf');
    await dialog.getByRole('checkbox', { name: /^PLN/ }).setChecked(true);
    await expect(groupCombobox(dialog, 'Domyślna grupa')).toBeVisible();
    await selectOption(page, groupCombobox(dialog, 'Domyślna grupa'), 'Poduszka');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(cashPlace(page, 'Sejf')).toBeVisible();
  });

  await test.step('Krok 6: after the deposits Wg grup shows Inwestycje 50 000, Emerytura 30 000, Poduszka 23 000, together 103 000 zł', async () => {
    const date = warsawDate(90);
    for (const { account, amount } of [
      { account: 'Broker', amount: '50000' },
      { account: 'Obligacje', amount: '30000' },
    ]) {
      await openPage(page, 'Rachunki inwestycyjne');
      await page.getByRole('table').getByRole('link', { name: account, exact: true }).click();
      await page.getByRole('button', { name: 'Wpłata', exact: true }).click();
      await cashOperation(dialog, { amount, date });
    }

    await openPage(page, 'Konta bankowe');
    await bankRow(page, 'Oszczędnościowe')
      .getByRole('button', { name: 'Akcje dla: Oszczędnościowe', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, { amount: '20000', date });

    await openPage(page, 'Gotówka');
    await cashPlace(page, 'Sejf')
      .getByRole('button', { name: 'Akcje dla: Sejf · PLN', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, { amount: '3000', date });

    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '50 000,00'),
        legendName('Emerytura', '30 000,00'),
        legendName('Poduszka', '23 000,00', { amount: '30 000,00', percent: 77 }),
      ],
    });
  });

  await test.step('Krok 7: 20 CDR.WA bought on Broker is offered the group Inwestycje and stays there', async () => {
    const buy = await openBuyStockDialog(page, 'Broker', 'Inwestycje');
    await pickInstrument(page, buy, 'CDR', 'CDR.WA');
    await buy.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await buy.getByLabel(/^Ilość/).fill('20');
    await buy.getByLabel(/^Cena jednostkowa/).fill('100');
    await buy.getByLabel('Data', { exact: true }).fill(warsawDate(60));
    await buy.getByRole('button', { name: 'Utwórz' }).click();
    await expect(buy).toBeHidden();
    await syncNow(page);
    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '50 000,00'),
        legendName('Emerytura', '30 000,00'),
        legendName('Poduszka', '23 000,00', { amount: '30 000,00', percent: 77 }),
      ],
    });
  });

  await test.step('Krok 8: VWCE.DE bought with the group changed to Emerytura lands in Emerytura', async () => {
    const buy = await openBuyStockDialog(page, 'Broker', 'Inwestycje', 'ETF');
    await selectOption(page, groupCombobox(buy), 'Emerytura');
    await pickInstrument(page, buy, 'VWCE', 'VWCE.DE');
    await buy.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await buy.getByLabel(/^Ilość/).fill('10');
    await buy.getByLabel(/^Cena jednostkowa/).fill('100');
    await selectOption(page, buy.getByRole('combobox', { name: 'Waluta zapłaty' }), 'PLN');
    await buy.getByLabel('Kwota w PLN').fill('4250');
    await buy.getByLabel('Data', { exact: true }).fill(warsawDate(30));
    await buy.getByRole('button', { name: 'Utwórz' }).click();
    await expect(buy).toBeHidden();
    await syncNow(page);
    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '45 750,00'),
        legendName('Emerytura', '34 250,00'),
        legendName('Poduszka', '23 000,00', { amount: '30 000,00', percent: 77 }),
      ],
    });
  });

  await test.step('Krok 9: EDO0136 stays in Emerytura, COI0136 moved to Inwestycje', async () => {
    const edo = await openBondDialog(page, 'Obligacje', 'Emerytura');
    await fillBond(page, edo, {
      code: 'EDO0136',
      type: 'Dziesięcioletnie indeksowane inflacją',
      margin: '1.5',
    });
    await edo.getByRole('button', { name: 'Utwórz' }).click();
    await expect(edo).toBeHidden();

    const coi = await openBondDialog(page, 'Obligacje', 'Emerytura');
    await selectOption(page, groupCombobox(coi), 'Inwestycje');
    await fillBond(page, coi, {
      code: 'COI0136',
      type: 'Czteroletnie indeksowane inflacją',
      margin: '1',
    });
    await coi.getByRole('button', { name: 'Utwórz' }).click();
    await expect(coi).toBeHidden();

    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '46 750,00'),
        legendName('Emerytura', '33 250,00'),
        legendName('Poduszka', '23 000,00', { amount: '30 000,00', percent: 77 }),
      ],
    });
  });

  await test.step('Krok 10: a gold bar and a 5 000 zł deposit without a group appear as Bez grupy', async () => {
    await openPage(page, 'Metale szlachetne');
    await page.getByRole('button', { name: 'Dodaj metal szlachetny', exact: true }).first().click();
    await selectOption(page, dialog.getByRole('combobox', { name: 'Metal' }), 'Złoto');
    await dialog.getByLabel('Nazwa').fill('Sztabka 1 g');
    await dialog.getByLabel('Waga kruszcu w sztuce').fill('1');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    const metal = page.getByRole('row').filter({ hasText: 'Sztabka 1 g' });
    await metal.getByRole('button', { name: 'Akcje dla: Sztabka 1 g', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Kup', exact: true }).click();
    await dialog.getByLabel('Sztuki').fill('1');
    await dialog.getByLabel('Kwota z faktury (PLN)').fill('1200');
    await selectOption(page, dialog.getByRole('combobox', { name: 'Rozlicz z' }), /^Sejf · PLN/);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await openPage(page, 'Lokaty');
    await page.getByRole('button', { name: 'Nowa lokata', exact: true }).first().click();
    await dialog.getByLabel('Nazwa').fill('Lokata 5000');
    await selectOption(
      page,
      dialog.getByRole('combobox', { name: 'Z konta' }),
      /^Oszczędnościowe \(/,
    );
    await dialog.getByLabel('Kapitał').fill('5000');
    await dialog.getByLabel('Okres', { exact: true }).fill('3');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('5');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();

    await syncNow(page);
    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '46 750,00'),
        legendName('Emerytura', '33 250,00'),
        legendName('Poduszka', '16 800,00', { amount: '30 000,00', percent: 56 }),
        legendName('Bez grupy', '6 200,00'),
      ],
    });
  });

  await test.step('Krok 11: the gold bar assigned to Emerytura and the deposit to Poduszka empty Bez grupy', async () => {
    await openAssignment(page, 'Emerytura');
    await assetRow(page, 'Sztabka 1 g').getByRole('checkbox').check();
    await saveAssignment(page);
    await openAssignment(page, 'Poduszka');
    await assetRow(page, 'Lokata 5000').getByRole('checkbox').check();
    await saveAssignment(page);
    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '46 750,00'),
        legendName('Emerytura', '34 450,00'),
        legendName('Poduszka', '21 800,00', { amount: '30 000,00', percent: 73 }),
      ],
      absent: 'Bez grupy',
    });
  });

  await test.step('Krok 12: all bond lots selected in one move go to Inwestycje', async () => {
    await openAssignment(page, 'Inwestycje');
    await selectOption(
      page,
      assignmentPanel(page).getByRole('combobox', { name: 'Klasa' }),
      'Obligacje',
    );
    await assignmentPanel(page).getByTestId('select-all-shown').click();
    await saveAssignment(page);
    await expectDashboard(page, {
      netWorth: '103 000,00',
      groups: [
        legendName('Inwestycje', '47 750,00'),
        legendName('Emerytura', '33 450,00'),
        legendName('Poduszka', '21 800,00', { amount: '30 000,00', percent: 73 }),
      ],
    });
  });

  await test.step('Krok 13: Poduszka over the target shows 33 000 / 30 000 zł (110%)', async () => {
    await openPage(page, 'Konta bankowe');
    await bankRow(page, 'Oszczędnościowe')
      .getByRole('button', { name: 'Akcje dla: Oszczędnościowe', exact: true })
      .click();
    await page.getByRole('menuitem', { name: 'Wpłata', exact: true }).click();
    await cashOperation(dialog, { amount: '11200' });
    await expectDashboard(page, {
      netWorth: '114 200,00',
      groups: [legendName('Poduszka', '33 000,00', { amount: '30 000,00', percent: 110 })],
    });
    await expect(
      groupLegend(page).locator('mat-progress-bar[aria-label="Poduszka"]'),
    ).toHaveAttribute('aria-valuenow', '100');
  });

  await test.step('Krok 14: the group filter Emerytura draws only the Emerytura assets in every range', async () => {
    await expectFilteredChart(page, { group: 'Emerytura' }, /do\s33\s?450,00\szł/);
    for (const range of ['1M', '1Y', 'YTD', 'MAX']) {
      await rangeToggle(page, range).click();
      await expect(rangeToggle(page, range)).toBeChecked();
      await expect(historyChart(page)).toBeVisible();
    }
    await rangeToggle(page, '1Y').click();
  });

  await test.step('Krok 15: the class filter and the group filter, one at a time, give their own series', async () => {
    await chooseFilter(page, 'ETF');
    await expect(historyImage(page)).toHaveAccessibleName(/do\s48\s?000,00\szł/);
    await chooseFilter(page, 'Emerytura');
    await expect(historyImage(page)).toHaveAccessibleName(/do\s33\s?450,00\szł/);
  });

  await test.step('Krok 16: the group filter Bez grupy draws the assets without a group', async () => {
    await chooseFilter(page, 'Bez grupy');
    await expect(page.locator('.net-worth-chart__caption')).toContainText('Bez grupy');
    await expect(historyChart(page)).toBeVisible();
  });

  await test.step('Krok 17: moving VWCE.DE from Emerytura to Inwestycje redraws both groups', async () => {
    await openAssignment(page, 'Inwestycje');
    await assetRow(page, 'Vanguard FTSE All-World').getByRole('checkbox').check();
    await saveAssignment(page);
    await expectFilteredChart(page, { group: 'Emerytura' }, /do\s29\s?200,00\szł/);
    await expectFilteredChart(page, { group: 'Inwestycje' }, /do\s52\s?000,00\szł/);
  });

  await test.step('Krok 18: archiving the COI lot removes it from the current split and restoring brings it back', async () => {
    const headers = { Authorization: `Bearer ${accessToken}` };
    const listed = await request.get('/api/portfolio/bonds', { headers });
    expect(listed.ok()).toBeTruthy();
    const coi = ((await listed.json()) as { assetId: string; name: string }[]).find(
      (bond) => bond.name === 'COI0136',
    );
    expect(coi).toBeTruthy();

    const archived = await request.post(`/api/portfolio/assets/${coi?.assetId}/archive`, {
      headers,
    });
    expect(archived.ok()).toBeTruthy();
    await expectDashboard(page, {
      groups: [legendName('Inwestycje', '51 000,00')],
    });

    const restored = await request.post(`/api/portfolio/assets/${coi?.assetId}/restore`, {
      headers,
    });
    expect(restored.ok()).toBeTruthy();
    await expectDashboard(page, {
      netWorth: '114 200,00',
      groups: [legendName('Inwestycje', '52 000,00')],
    });
  });

  await test.step('Krok 19: deleting Inwestycje warns about its 5 assets and sends them to Bez grupy', async () => {
    await chooseFilter(page, 'Inwestycje');
    await openPage(page, 'Grupy');
    await groupRow(page, 'Inwestycje').getByTestId('delete-group').click();
    await expect(dialog).toContainText('ma aktywa: 5');
    await expect(dialog).toContainText('„Bez grupy”');
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('group-row')).toHaveCount(2);

    await expectDashboard(page, {
      netWorth: '114 200,00',
      groups: [
        legendName('Bez grupy', '52 000,00'),
        legendName('Emerytura', '29 200,00'),
        legendName('Poduszka', '33 000,00', { amount: '30 000,00', percent: 110 }),
      ],
    });
    await expect(filterButton(page)).toContainText('Wszystko');

    await openPage(page, 'Rachunki inwestycyjne');
    await page.getByRole('button', { name: 'Akcje dla: Broker', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Edytuj', exact: true }).click();
    await expect(groupCombobox(dialog, 'Domyślna grupa')).toContainText('Bez grupy');
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 20: on a phone width Struktura majątku and Wg grup are stacked', async () => {
    await page.setViewportSize({ width: 390, height: 844 });
    await dashboardRefetch(page);
    const classes = page.getByRole('heading', { name: 'Struktura majątku' });
    const groups = page.getByRole('heading', { name: 'Wg grup' });
    await expect(groups).toBeVisible();
    await expect(async () => {
      const first = await classes.boundingBox();
      const second = await groups.boundingBox();
      expect(first).not.toBeNull();
      expect(second).not.toBeNull();
      expect(second?.y ?? 0).toBeGreaterThan((first?.y ?? 0) + (first?.height ?? 0));
    }).toPass({ timeout: 10_000 });
  });
});

test('asset groups: the twelve group limit refuses a thirteenth', async ({
  signedInPage: page,
  user,
  request,
}) => {
  const accessToken = await loginToken(request, user);
  const headers = { Authorization: `Bearer ${accessToken}` };
  for (const [index, color] of COLORS.entries()) {
    const created = await request.post('/api/portfolio/asset-groups', {
      headers,
      data: { name: `Grupa ${index + 1}`, color },
    });
    expect(created.ok()).toBeTruthy();
  }

  await test.step('Krok 21: with 12 groups adding is blocked with the limit hint and the server refuses a 13th', async () => {
    await openPage(page, 'Grupy');
    await expect(page.getByTestId('group-row')).toHaveCount(12);
    await expect(page.getByTestId('add-group')).toBeDisabled();
    await expect(page.getByTestId('group-limit-hint')).toContainText('Osiągnięto limit 12 grup.');
    const refused = await request.post('/api/portfolio/asset-groups', {
      headers,
      data: { name: 'Grupa 13', color: 'blue' },
    });
    expect(refused.ok()).toBeFalsy();
  });
});
