import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expandWealthSections,
  expect,
  logInThroughApi,
  logInThroughUi,
  openSelect,
  test,
  warsawDate,
  wealthTab,
} from './fixtures';
import { between, fetchHistory, type HistoryPoint } from './cash-class';
import {
  MBANK,
  REVOLUT,
  addIncome,
  choose,
  expectPlan,
  openAddDialog,
  openMonth,
  pln,
  shiftMonth,
} from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const VARIANT_SAVINGS = 1;
const CUSHION_KIND = 1;

const SAVINGS = 'Oszczędności';
const JOINT = 'Konto wspólne';
const DEPOSIT_NAME = 'Lokata 3M';
const GROUP = 'Poduszka';
const LABEL = 'Poza majątkiem';

const CURRENT_MONTH = warsawDate().slice(0, 7);
const NEXT_MONTH = shiftMonth(CURRENT_MONTH, 1);

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

async function createBankAccount(
  request: APIRequestContext,
  token: string,
  account: { name: string; variant: number; groupId: string },
): Promise<{ id: string; cashAssetId: string }> {
  return createAccountThroughApi(request, token, {
    name: account.name,
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: account.variant,
    annualInterestRatePercent: 0,
    groupId: account.groupId,
  } as Parameters<typeof createAccountThroughApi>[2]);
}

async function transferThroughApi(
  request: APIRequestContext,
  token: string,
  transfer: { sourceAssetId: string; targetAssetId: string; amount: number; date: string },
): Promise<void> {
  const response = await request.post('/api/portfolio/transfers', {
    headers: authorization(token),
    data: transfer,
  });
  expect(response.ok()).toBeTruthy();
}

async function openBankList(page: Page): Promise<void> {
  await wealthTab(page, 'Konta bankowe');
  await expect(page).toHaveURL(/\/wealth\/bank$/);
}

function accountRow(page: Page, name: string): Locator {
  return page.getByRole('row').filter({ has: page.getByRole('link', { name, exact: true }) });
}

function chip(page: Page, name: string): Locator {
  return page.getByRole('option', { name, exact: true });
}

function statusOf(page: Page, name: string): Locator {
  return accountRow(page, name).locator('.mat-column-status');
}

async function openRowMenu(page: Page, name: string, item: string): Promise<void> {
  await accountRow(page, name)
    .getByRole('button', { name: `Akcje dla: ${name}`, exact: true })
    .click();
  await page.getByRole('menuitem', { name: item, exact: true }).click();
}

function flagSwitch(dialog: Locator): Locator {
  return dialog.getByRole('switch', { name: /Poza majątkiem/ });
}

function groupSelect(dialog: Locator): Locator {
  return dialog.getByRole('combobox', { name: 'Grupa', exact: true });
}

async function openEditDialog(page: Page, name: string): Promise<Locator> {
  await openBankList(page);
  await openRowMenu(page, name, 'Edytuj');
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Edytuj rachunek' })).toBeVisible();
  return dialog;
}

async function setFlag(page: Page, name: string, on: boolean): Promise<void> {
  const dialog = await openEditDialog(page, name);
  const toggle = flagSwitch(dialog);
  if ((await toggle.getAttribute('aria-checked')) !== String(on)) {
    await toggle.click();
  }
  await expect(toggle).toBeChecked({ checked: on });
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function transfer(
  page: Page,
  move: { from: string; to: string; amount: string },
): Promise<void> {
  await openBankList(page);
  await openRowMenu(page, move.from, 'Przelew');
  const dialog = page.getByRole('dialog');
  await openSelect(dialog.getByRole('combobox', { name: 'Rachunek docelowy' }));
  await page.getByRole('option', { name: new RegExp(`^${move.to}`) }).click();
  await dialog.getByLabel('Kwota').fill(move.amount);
  await dialog.getByRole('button', { name: 'Przelej' }).click();
  await expect(dialog).toBeHidden();
}

async function expectBalance(page: Page, name: string, amount: RegExp): Promise<void> {
  await openBankList(page);
  await expect(accountRow(page, name).locator('.mat-column-balance')).toContainText(amount);
}

async function addCurrentAccount(
  page: Page,
  account: { name: string; currency: RegExp; deposit: string },
): Promise<void> {
  await openBankList(page);
  await page.getByRole('button', { name: 'Dodaj konto', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(account.name);
  await choose(page, dialog.getByRole('combobox', { name: 'Waluta' }), account.currency);
  await choose(page, dialog.getByRole('combobox', { name: 'Wariant' }), 'Bieżące');
  await dialog.getByLabel('Roczne oprocentowanie (%)').fill('0');
  await flagSwitch(dialog).click();
  await expect(flagSwitch(dialog)).toBeChecked();
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
  await expect(statusOf(page, account.name)).toContainText(LABEL);

  await openRowMenu(page, account.name, 'Wpłata');
  const deposit = page.getByRole('dialog');
  await deposit.getByLabel('Kwota').fill(account.deposit);
  await deposit.getByRole('button', { name: 'Zapisz' }).click();
  await expect(deposit).toBeHidden();
}

async function expectNetWorth(page: Page, amount: string): Promise<void> {
  await expectProjected(
    () => page.goto('/dashboard'),
    async () => {
      await expect(page.getByTestId('net-worth')).toContainText(pln(amount), { timeout: 2_000 });
    },
    { timeout: 60_000 },
  );
}

function accountsCardRow(page: Page, name: string): Locator {
  return page.locator('.accounts-card__row').filter({ hasText: name });
}

async function historyOf(request: APIRequestContext, token: string): Promise<HistoryPoint[]> {
  return fetchHistory(request, token);
}

function values(points: HistoryPoint[]): number[] {
  return points.map((point) => point.value);
}

test('rachunek poza majątkiem: konto bieżące zostaje poza majątkiem, a przelewy przez granicę zmieniają go w dniu operacji', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  const headers = authorization(token);

  const groupResponse = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: {
      name: GROUP,
      color: 'orange',
      targetPln: 30000,
      kind: CUSHION_KIND,
      contributions: [],
      classAllocation: [],
    },
  });
  expect(groupResponse.ok()).toBeTruthy();
  const groupId = ((await groupResponse.json()) as { id: string }).id;

  const savings = await createBankAccount(request, token, {
    name: SAVINGS,
    variant: VARIANT_SAVINGS,
    groupId,
  });
  const ror = await createBankAccount(request, token, {
    name: MBANK,
    variant: VARIANT_CURRENT,
    groupId,
  });
  await depositCashThroughApi(request, token, savings.cashAssetId, {
    quantity: 39000,
    date: warsawDate(60),
  });
  await depositCashThroughApi(request, token, ror.cashAssetId, {
    quantity: 4200,
    date: warsawDate(60),
  });
  await transferThroughApi(request, token, {
    sourceAssetId: ror.cashAssetId,
    targetAssetId: savings.cashAssetId,
    amount: 1000,
    date: warsawDate(14),
  });

  await page.goto('/login');
  await logInThroughUi(page, user);

  await test.step('Krok 1: pulpit pokazuje wartość netto 43 200 zł, mBank ROR 3 200 zł, Oszczędności 40 000 zł i płaski wykres historii na 43 200 zł', async () => {
    await expectNetWorth(page, '43 200,00');
    await expectProjected(
      () => page.goto('/dashboard'),
      async () => {
        await expect(accountsCardRow(page, MBANK)).toContainText(pln('3 200,00'), {
          timeout: 2_000,
        });
        await expect(accountsCardRow(page, SAVINGS)).toContainText(pln('40 000,00'), {
          timeout: 2_000,
        });
      },
    );
    await expectProjected(
      async () => undefined,
      async () => {
        const points = await historyOf(request, token);
        expect(points.length).toBeGreaterThan(0);
        expect(new Set(values(between(points, warsawDate(60), warsawDate(1))))).toEqual(
          new Set([43200]),
        );
      },
    );
  });

  await test.step('Krok 2: formularz edycji mBank ROR ma wyłączony przełącznik Poza majątkiem (konto bieżące) z wyjaśnieniem i wybór grupy', async () => {
    const dialog = await openEditDialog(page, MBANK);
    await expect(flagSwitch(dialog)).not.toBeChecked();
    await expect(
      dialog.getByText('Konto nie wchodzi do majątku, pulpitu ani list majątku.'),
    ).toBeVisible();
    await expect(groupSelect(dialog)).toBeVisible();

    await test.step('Krok 3: włączenie przełącznika ukrywa wybór grupy', async () => {
      await flagSwitch(dialog).click();
      await expect(flagSwitch(dialog)).toBeChecked();
      await expect(groupSelect(dialog)).toHaveCount(0);
    });

    await test.step('Krok 4: po zapisie mBank ROR stoi w jednej tabeli z etykietą Poza majątkiem i jest widoczny pod chipem Bieżące', async () => {
      await dialog.getByRole('button', { name: 'Zapisz' }).click();
      await expect(dialog).toBeHidden();
      await expect(statusOf(page, MBANK)).toContainText(LABEL);
      await chip(page, 'Bieżące').click();
      await expect(accountRow(page, MBANK)).toBeVisible();
      await chip(page, 'Wszystkie').click();
    });
  });

  await test.step('Krok 5: pulpit pokazuje wartość netto 40 000 zł, bez mBank ROR w rachunkach i w klasie Konta bankowe', async () => {
    await expectNetWorth(page, '40 000,00');
    await expectProjected(
      () => page.goto('/dashboard'),
      async () => {
        await expect(accountsCardRow(page, MBANK)).toHaveCount(0, { timeout: 2_000 });
        await expect(accountsCardRow(page, SAVINGS)).toContainText(pln('40 000,00'), {
          timeout: 2_000,
        });
        await expect(
          page.locator('.dashboard-page__structure li').filter({ hasText: 'Konta bankowe' }),
        ).toContainText(pln('40 000,00'), { timeout: 2_000 });
      },
    );
  });

  await test.step('Krok 6: wykres historii ma 39 000 zł do dnia sprzed 14 dni i 40 000 zł od tego dnia, bez skoku w dniu zmiany flagi', async () => {
    await expectProjected(
      async () => undefined,
      async () => {
        const points = await historyOf(request, token);
        expect(new Set(values(between(points, warsawDate(60), warsawDate(15))))).toEqual(
          new Set([39000]),
        );
        expect(new Set(values(between(points, warsawDate(14), warsawDate(1))))).toEqual(
          new Set([40000]),
        );
      },
    );
  });

  await test.step('Krok 7: Grupy pokazują Poduszkę z sumą 40 000 zł i jednym aktywem, bez mBank ROR', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    const card = page
      .getByTestId('group-card')
      .filter({ has: page.getByTestId('group-name').getByText(GROUP, { exact: true }) });
    await expectProjected(
      () => page.goto('/groups'),
      async () => {
        await expect(card.getByTestId('group-value')).toContainText(pln('40 000,00'), {
          timeout: 2_000,
        });
        await expect(card.getByTestId('group-count')).toContainText('Aktywa: 1', {
          timeout: 2_000,
        });
      },
    );
    await card.getByTestId('select-group').click();
    const panel = page.getByTestId('assets-panel');
    await panel.getByTestId('edit-assignment').click();
    const assetRow = (name: string): Locator =>
      panel
        .getByTestId('tree-row')
        .filter({ has: page.getByTestId('row-name').getByText(name, { exact: true }) });
    await expect(assetRow(SAVINGS).getByRole('checkbox')).toBeChecked();
    await expect(assetRow(MBANK).getByRole('checkbox')).not.toBeChecked();
    await expect(page.getByTestId('ungrouped-card').getByTestId('ungrouped-count')).toContainText(
      '1',
    );
  });

  await test.step('Krok 8: Planowanie liczy poziom poduszki od 40 000 zł', async () => {
    await expectProjected(
      () => openMonth(page, CURRENT_MONTH),
      async () => {
        await expect(page.getByTestId('split-cushion')).toContainText(pln('40 000,00'), {
          timeout: 2_000,
        });
      },
    );
  });

  await test.step('Krok 9: strona mBank ROR pokazuje saldo 3 200 zł i etykietę Poza majątkiem, bez wartości liczonej do majątku', async () => {
    await openBankList(page);
    await accountRow(page, MBANK).getByRole('link', { name: MBANK, exact: true }).click();
    await expect(page.getByRole('heading', { name: MBANK, exact: true })).toBeVisible();
    await expect(page.getByTestId('account-cash')).toContainText(pln('3 200,00'));
    await expect(page.locator('.account-detail__meta')).toContainText(LABEL);
    await expect(page.getByTestId('summary-strip')).not.toContainText('Razem');
  });

  await test.step('Krok 10: wyłączenie flagi zostawia grupę pustą (nie wraca Poduszka), ponowne włączenie chowa wybór grupy', async () => {
    await setFlag(page, MBANK, false);
    await expect(statusOf(page, MBANK)).not.toContainText(LABEL);
    const dialog = await openEditDialog(page, MBANK);
    await expect(groupSelect(dialog)).toBeVisible();
    await expect(groupSelect(dialog)).not.toContainText(GROUP);
    await flagSwitch(dialog).click();
    await expect(flagSwitch(dialog)).toBeChecked();
    await expect(groupSelect(dialog)).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(statusOf(page, MBANK)).toContainText(LABEL);
  });

  await test.step('Krok 11: przelew 1 000 zł z mBank ROR na Oszczędności z dzisiejszą datą: ROR 2 200 zł, wartość netto 41 000 zł, etykieta tylko przy koncie poza majątkiem', async () => {
    await openBankList(page);
    await openRowMenu(page, SAVINGS, 'Przelew');
    const probe = page.getByRole('dialog');
    await openSelect(probe.getByRole('combobox', { name: 'Rachunek docelowy' }));
    await expect(page.getByRole('option', { name: /^mBank ROR/ })).toContainText(LABEL);
    await page.keyboard.press('Escape');
    await probe.getByRole('button', { name: 'Anuluj' }).click();
    await expect(probe).toBeHidden();

    await openRowMenu(page, MBANK, 'Przelew');
    const dialog = page.getByRole('dialog');
    await openSelect(dialog.getByRole('combobox', { name: 'Rachunek docelowy' }));
    await expect(page.getByRole('option', { name: /^Oszczędności/ })).not.toContainText(LABEL);
    await page.getByRole('option', { name: /^Oszczędności/ }).click();
    await dialog.getByLabel('Kwota').fill('1000');
    await dialog.getByRole('button', { name: 'Przelej' }).click();
    await expect(dialog).toBeHidden();
    await expectBalance(page, MBANK, pln('2 200,00'));
    await expectNetWorth(page, '41 000,00');
  });

  await test.step('Krok 12: przelew 500 zł z Oszczędności na mBank ROR: ROR 2 700 zł, wartość netto 40 500 zł', async () => {
    await transfer(page, { from: SAVINGS, to: MBANK, amount: '500' });
    await expectBalance(page, MBANK, pln('2 700,00'));
    await expectNetWorth(page, '40 500,00');
  });

  await test.step('Krok 13: lokata Lokata 3M 1 000 zł z mBank ROR: ROR 1 700 zł, lokata 1 000 zł, wartość netto 41 500 zł', async () => {
    await wealthTab(page, 'Lokaty');
    await page.getByRole('button', { name: 'Nowa lokata', exact: true }).first().click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Nazwa').fill(DEPOSIT_NAME);
    await openSelect(dialog.getByRole('combobox', { name: 'Z konta' }));
    await expect(page.getByRole('option', { name: /^mBank ROR/ })).toContainText(LABEL);
    await page.getByRole('option', { name: /^mBank ROR/ }).click();
    await dialog.getByLabel('Kapitał').fill('1000');
    await dialog.getByLabel('Okres', { exact: true }).fill('3');
    await dialog.getByLabel('Roczne oprocentowanie (%)').fill('6');
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row').filter({ hasText: DEPOSIT_NAME })).toContainText(
      pln('1 000,00'),
    );
    await expectBalance(page, MBANK, pln('1 700,00'));
    await expectNetWorth(page, '41 500,00');
  });

  await test.step('Krok 14: Konto wspólne (PLN, Bieżący) z 500 zł poza majątkiem ma etykietę Poza majątkiem w tabeli, wartość netto 41 500 zł', async () => {
    await addCurrentAccount(page, { name: JOINT, currency: /^PLN/, deposit: '500' });
    await expectBalance(page, JOINT, pln('500,00'));
    await expect(statusOf(page, JOINT)).toContainText(LABEL);
    await expectNetWorth(page, '41 500,00');
  });

  await test.step('Krok 15: Revolut EUR ze 100 EUR poza majątkiem ma etykietę i saldo 100 EUR, wartość netto bez zmian', async () => {
    await addCurrentAccount(page, { name: REVOLUT, currency: /^EUR/, deposit: '100' });
    await openBankList(page);
    await expect(statusOf(page, REVOLUT)).toContainText(LABEL);
    await expect(accountRow(page, REVOLUT).locator('.mat-column-balance')).toContainText(
      /100,00\sEUR/,
    );
    await expectNetWorth(page, '41 500,00');
  });

  await test.step('Krok 16: przelew 200 zł z mBank ROR na Konto wspólne: ROR 1 500 zł, wspólne 700 zł, wartość netto bez zmian', async () => {
    await transfer(page, { from: MBANK, to: JOINT, amount: '200' });
    await expectBalance(page, MBANK, pln('1 500,00'));
    await expectBalance(page, JOINT, pln('700,00'));
    await expect(statusOf(page, MBANK)).toContainText(LABEL);
    await expect(statusOf(page, JOINT)).toContainText(LABEL);
    await expectNetWorth(page, '41 500,00');
  });

  await test.step('Krok 17: cykliczny przychód 8 000 zł na mBank ROR od przyszłego miesiąca ma konto z etykietą Poza majątkiem i daje 8 000 zł w bilansie miesiąca', async () => {
    await openMonth(page, CURRENT_MONTH);
    const dialog = await openAddDialog(page);
    await openSelect(dialog.getByRole('combobox', { name: 'Konto', exact: true }));
    await expect(page.getByRole('option', { name: /^mBank ROR/ })).toContainText(LABEL);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('listbox')).toBeHidden();
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();

    await addIncome(page, {
      name: 'Wypłata',
      amount: '8000',
      account: MBANK,
      start: `${NEXT_MONTH}-01`,
      interval: '1',
      day: 10,
    });
    await openMonth(page, NEXT_MONTH);
    await expectPlan(page, '8 000,00');
  });

  await test.step('Krok 18: jednorazowy przychód 500 zł na Konto wspólne liczy się do bilansu miesiąca, wartość netto bez zmian', async () => {
    await openMonth(page, NEXT_MONTH);
    const dialog = await openAddDialog(page);
    await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
    await dialog.getByLabel('Nazwa').fill('Zwrot');
    await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('500');
    await choose(
      page,
      dialog.getByRole('combobox', { name: 'Konto', exact: true }),
      /^Konto wspólne/,
    );
    await dialog.getByLabel('Data', { exact: true }).fill(`${NEXT_MONTH}-15`);
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expectPlan(page, '8 500,00');
    await expectNetWorth(page, '41 500,00');
  });

  await test.step('Krok 19: lista Majątek i karta rachunków na pulpicie nie pokazują żadnego z trzech kont poza majątkiem', async () => {
    await page.goto('/wealth');
    await expandWealthSections(page);
    await expect(page.locator('.wealth-all__name', { hasText: SAVINGS })).toBeVisible();
    for (const name of [MBANK, JOINT, REVOLUT]) {
      await expect(page.locator('.wealth-all__name', { hasText: name })).toHaveCount(0);
    }
    await page.goto('/dashboard');
    await expect(accountsCardRow(page, SAVINGS)).toBeVisible();
    for (const name of [MBANK, JOINT, REVOLUT]) {
      await expect(accountsCardRow(page, name)).toHaveCount(0);
    }
  });

  await test.step('Krok 20: po wyłączeniu przełącznika mBank ROR traci etykietę Poza majątkiem, a dwa pozostałe konta ją zachowują', async () => {
    await setFlag(page, MBANK, false);
    await expect(statusOf(page, MBANK)).not.toContainText(LABEL);
    await expect(statusOf(page, JOINT)).toContainText(LABEL);
    await expect(statusOf(page, REVOLUT)).toContainText(LABEL);
  });

  await test.step('Krok 21: pulpit pokazuje wartość netto 43 000 zł, a wykres obejmuje ROR od początku bez skoku w dniu wyłączenia', async () => {
    await expectNetWorth(page, '43 000,00');
    await expectProjected(
      () => page.goto('/dashboard'),
      async () => {
        await expect(accountsCardRow(page, SAVINGS)).toContainText(pln('40 500,00'), {
          timeout: 2_000,
        });
        await expect(accountsCardRow(page, MBANK)).toContainText(pln('1 500,00'), {
          timeout: 2_000,
        });
      },
    );
    await expectProjected(
      async () => undefined,
      async () => {
        const points = await historyOf(request, token);
        expect(new Set(values(between(points, warsawDate(60), warsawDate(1))))).toEqual(
          new Set([43200]),
        );
        expect(points.at(-1)?.value).toBe(43000);
      },
    );
  });

  await test.step('Krok 22: po wyłączeniu flagi na Konto wspólne i Revolut EUR żadne konto nie ma etykiety Poza majątkiem', async () => {
    await setFlag(page, JOINT, false);
    await setFlag(page, REVOLUT, false);
    await expect(accountRow(page, JOINT)).toBeVisible();
    await expect(accountRow(page, REVOLUT)).toBeVisible();
    await expect(page.getByText(LABEL, { exact: true })).toHaveCount(0);
  });
});
