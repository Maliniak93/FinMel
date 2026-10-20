import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { depositCashThroughApi, expect, test, warsawDate, type Credentials } from './fixtures';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BONDS = 1;
const WRAPPER_REGULAR = 0;
const ASSET_CLASS_ETF = 3;
const TYPE_BUY = 0;
const BOND_TYPE_EDO = 5;
const METAL_GOLD = 0;

const WHOLE = 'Całość';
const RETIREMENT = 'Emerytura';
const INVESTMENTS = 'Inwestycje';
const UNKNOWN_GROUP = '00000000-0000-0000-0000-000000000000';

function pln(amount: string): RegExp {
  const sign = amount.startsWith('+') ? '\\+' : amount.startsWith('-') ? '[-\\u2212]' : '';
  const digits = amount.replace(/^[+-]/, '').replace(/ /g, '\\s?');
  return new RegExp(`${sign}${digits}\\szł`);
}

function exactly(pattern: RegExp): RegExp {
  return new RegExp(`^\\s*${pattern.source}\\s*$`);
}

function percent(value: string): RegExp {
  const pattern = value.endsWith(',0') ? `${value.slice(0, -2)}(?:,0)?` : value;
  return new RegExp(`^\\s*${pattern}\\s%\\s*$`);
}

function navigation(page: Page): Locator {
  return page.locator('mat-sidenav');
}

async function openPage(page: Page, link: string): Promise<void> {
  await navigation(page).getByRole('link', { name: link, exact: true }).click();
}

function scopeSwitcher(page: Page): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres alokacji' });
}

function scopeChip(page: Page, name: string): Locator {
  return scopeSwitcher(page).getByRole('radio', { name, exact: true });
}

async function openAllocation(page: Page, scope = WHOLE): Promise<void> {
  await openPage(page, 'Alokacja');
  await expect(page).toHaveURL(/\/allocation/);
  await selectScope(page, scope);
}

async function selectScope(page: Page, scope: string): Promise<void> {
  const chip = scopeChip(page, scope);
  await expect(chip).toBeVisible();
  if ((await chip.getAttribute('aria-checked')) !== 'true') {
    await chip.click();
  }
  await expect(chip).toHaveAttribute('aria-checked', 'true');
}

function groupIdFromUrl(page: Page): string {
  const id = new URL(page.url()).searchParams.get('group');
  expect(id).toBeTruthy();
  return id as string;
}

function editorRow(page: Page, index: number): Locator {
  return page.getByTestId('editor-row').nth(index);
}

function viewRow(page: Page, name: string): Locator {
  return page.getByTestId('group-row').filter({
    has: page.locator('.allocation-page__group-name').getByText(name, { exact: true }),
  });
}

async function pickClass(page: Page, row: Locator, label: string): Promise<void> {
  await row.getByTestId('group-classes').click();
  await page.getByRole('option', { name: label, exact: true }).click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('option', { name: label, exact: true })).toBeHidden();
}

async function setRow(
  row: Locator,
  values: { name?: string; target?: string; band?: string },
): Promise<void> {
  if (values.name !== undefined) {
    await row.getByTestId('group-name').fill(values.name);
  }
  if (values.target !== undefined) {
    await row.getByTestId('group-target').fill(values.target);
  }
  if (values.band !== undefined) {
    await row.getByTestId('group-band').fill(values.band);
  }
}

interface ViewRow {
  value?: string;
  current?: string;
  target: string;
  band: string;
  deviationPln?: string;
  outOfBand?: boolean;
}

async function expectViewRow(page: Page, name: string, expected: ViewRow): Promise<void> {
  const row = viewRow(page, name);
  await expect(row).toHaveCount(1);
  const cells = row.locator('td');
  if (expected.value === undefined) {
    await expect(cells).toHaveCount(3);
    await expect(cells.nth(1)).toHaveText(percent(expected.target));
    await expect(cells.nth(2)).toHaveText(
      new RegExp(`^\\s*${expected.band.replace('–', '[–-]')}\\s%\\s*$`),
    );
    await expect(row.getByTestId('out-of-band')).toHaveCount(0);
    return;
  }
  await expect(cells.nth(1)).toHaveText(exactly(pln(expected.value)));
  if (expected.current !== undefined) {
    await expect(cells.nth(2)).toHaveText(percent(expected.current));
  }
  await expect(cells.nth(3)).toHaveText(percent(expected.target));
  await expect(cells.nth(4)).toHaveText(
    new RegExp(`^\\s*${expected.band.replace('–', '[–-]')}\\s%\\s*$`),
  );
  if (expected.deviationPln !== undefined) {
    await expect(cells.nth(6)).toHaveText(exactly(pln(expected.deviationPln)));
  }
  if (expected.outOfBand !== undefined) {
    await expect(row.getByTestId('out-of-band')).toHaveCount(expected.outOfBand ? 1 : 0);
  }
}

async function expectSummary(page: Page, base: string, total: string): Promise<void> {
  await expect(page.getByTestId('summary')).toContainText(
    new RegExp(`baza\\s${pln(base).source}\\sz\\s${pln(total).source}`),
  );
}

async function expectOutside(page: Page, amount: string): Promise<void> {
  const outside = page.getByTestId('outside-row');
  await expect(outside).toContainText('Poza alokacją');
  await expect(outside).toContainText(pln(amount));
}

async function expectWholeView(page: Page): Promise<void> {
  await expect(page.getByTestId('allocation-view')).toBeVisible();
  await expect(page.getByTestId('group-row')).toHaveCount(3);
  await expectViewRow(page, 'Obligacje', {
    value: '30 000,00',
    target: '50',
    band: '45–55',
  });
  await expectViewRow(page, 'Akcje', { value: '21 250,00', target: '40', band: '35–45' });
  await expectViewRow(page, 'Złoto', { value: '4 800,00', target: '10', band: '7–13' });
  await expectSummary(page, '56 050,00', '61 050,00');
  await expect(page.getByTestId('plan-note')).toHaveCount(0);
  await expect(page.getByTestId('delete-allocation')).toHaveCount(0);
}

async function reloadPage(page: Page): Promise<void> {
  await page.reload();
}

async function loginToken(request: APIRequestContext, user: Credentials): Promise<string> {
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  return ((await login.json()) as { accessToken: string }).accessToken;
}

async function createGroup(page: Page, group: { name: string; color: string }): Promise<void> {
  await page.getByTestId('add-group').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(group.name);
  await dialog.getByRole('radio', { name: group.color }).click();
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

function groupRow(page: Page, name: string): Locator {
  return page
    .getByTestId('group-row')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

function assignmentPanel(page: Page): Locator {
  return page.locator('mat-card.groups-page__assignment');
}

async function openAssignment(page: Page, group: string): Promise<void> {
  await openPage(page, 'Grupy');
  await groupRow(page, group).getByTestId('select-group').click();
  await expect(assignmentPanel(page)).toBeVisible();
}

async function assignAssets(
  page: Page,
  group: string,
  assets: { name: string; checked: boolean }[],
): Promise<void> {
  await openAssignment(page, group);
  for (const asset of assets) {
    const box = assignmentPanel(page)
      .getByTestId('asset-row')
      .filter({ hasText: asset.name })
      .getByRole('checkbox');
    if (asset.checked) {
      await box.check();
    } else {
      await box.uncheck();
    }
  }
  const save = assignmentPanel(page).getByTestId('save-assignment');
  await save.click();
  await expect(save).toBeDisabled();
}

async function createSecurityThroughApi(
  request: APIRequestContext,
  token: string,
  accountId: string,
): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };
  const search = await request.get('/api/marketdata/instruments/search?q=VWCE&assetClass=Etf', {
    headers,
  });
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === 'VWCE.DE');
  expect(found?.id).toBeTruthy();

  // The first buy freezes the FX rate of its day, which MarketData serves only after its first FX sync.
  await expect(async () => {
    const created = await request.post('/api/portfolio/assets', {
      headers,
      data: {
        accountId,
        assetClass: ASSET_CLASS_ETF,
        name: 'VWCE.DE',
        currency: 'EUR',
        instrumentId: found?.id,
        initialTransaction: {
          type: TYPE_BUY,
          quantity: 50,
          unitPrice: 100,
          date: warsawDate(),
        },
      },
    });
    expect(created.ok()).toBeTruthy();
  }).toPass({ timeout: 60_000, intervals: [2_000] });
}

async function syncPrices(page: Page): Promise<void> {
  await openPage(page, 'Ustawienia');
  await expect(page.getByRole('heading', { name: 'Ustawienia', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Synchronizuj teraz' }).click();
  await expect(page.getByText(/Rozpoczęto/).first()).toBeVisible({ timeout: 30_000 });
}

async function expectNetWorthProjected(
  request: APIRequestContext,
  token: string,
  netWorthPln: number,
): Promise<void> {
  let current: number | undefined;
  await expectProjected(
    async () => {
      const response = await request.get('/api/reporting/dashboard', {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(response.ok()).toBeTruthy();
      current = ((await response.json()) as { netWorthPln: number }).netWorthPln;
    },
    async () => {
      expect(current).toBe(netWorthPln);
    },
    { timeout: 60_000 },
  );
}

async function expectProjectedView(
  page: Page,
  assertion: () => Promise<void>,
  options: { reloadTimeout?: number } = {},
): Promise<void> {
  await expectProjected(() => reloadPage(page), assertion, {
    timeout: options.reloadTimeout ?? 60_000,
  });
}

test('alokacja grup: osobna alokacja docelowa dla każdej grupy aktywów', async ({
  signedInPage: page,
  user,
  request,
}) => {
  test.setTimeout(900_000);
  const token = await loginToken(request, user);
  const headers = { Authorization: `Bearer ${token}` };
  const dialog = page.getByRole('dialog');
  let investmentsId = '';

  await test.step('Przygotowanie 4: grupy Emerytura (niebieski) i Inwestycje (zielony)', async () => {
    await openPage(page, 'Grupy');
    await createGroup(page, { name: RETIREMENT, color: 'Niebieski' });
    await createGroup(page, { name: INVESTMENTS, color: 'Zielony' });
    await expect(page.getByTestId('group-row')).toHaveCount(2);
  });

  await test.step('Przygotowanie 5-6: Obligacje (30 000 zł w EDO), Broker (50 szt. VWCE.DE), sztabka 4 g i Sejf 5 000 zł', async () => {
    const groupsResponse = await request.get('/api/portfolio/asset-groups', { headers });
    expect(groupsResponse.ok()).toBeTruthy();
    const groups = (await groupsResponse.json()) as { id: string; name: string }[];
    const retirement = groups.find((group) => group.name === RETIREMENT)?.id;
    const investments = groups.find((group) => group.name === INVESTMENTS)?.id;
    expect(retirement).toBeTruthy();
    expect(investments).toBeTruthy();

    const bonds = await request.post('/api/portfolio/accounts', {
      headers,
      data: {
        name: 'Obligacje',
        type: ACCOUNT_TYPE_BONDS,
        wrapper: WRAPPER_REGULAR,
        groupId: retirement,
      },
    });
    expect(bonds.ok()).toBeTruthy();
    const bondsAccount = (await bonds.json()) as { id: string; cashAssetId: string };
    const broker = await request.post('/api/portfolio/accounts', {
      headers,
      data: {
        name: 'Broker',
        type: ACCOUNT_TYPE_BROKERAGE,
        wrapper: WRAPPER_REGULAR,
        groupId: investments,
      },
    });
    expect(broker.ok()).toBeTruthy();
    const brokerAccount = (await broker.json()) as { id: string; cashAssetId: string };

    await depositCashThroughApi(request, token, bondsAccount.cashAssetId, {
      quantity: 30000,
      date: warsawDate(),
    });
    await depositCashThroughApi(request, token, brokerAccount.cashAssetId, {
      quantity: 21250,
      date: warsawDate(),
    });

    const bond = await request.post('/api/portfolio/bonds', {
      headers,
      data: {
        accountId: bondsAccount.id,
        groupId: retirement,
        name: 'EDO0136',
        seriesCode: 'EDO0136',
        type: BOND_TYPE_EDO,
        purchaseDate: warsawDate(),
        bondCount: 300,
        purchasePricePerBond: 100,
        firstPeriodRatePercent: 2,
        marginPercent: 1.5,
        earlyRedemptionFeePerBond: 0,
      },
    });
    expect(bond.ok()).toBeTruthy();

    await createSecurityThroughApi(request, token, brokerAccount.id);

    const place = await request.post('/api/portfolio/cash-places', {
      headers,
      data: { name: 'Sejf', currencies: ['PLN'] },
    });
    expect(place.ok()).toBeTruthy();
    const cashAssetId = ((await place.json()) as { balances: { cashAssetId: string }[] })
      .balances[0]?.cashAssetId as string;
    await depositCashThroughApi(request, token, cashAssetId, {
      quantity: 9800,
      date: warsawDate(),
    });

    const metal = await request.post('/api/portfolio/metals', {
      headers,
      data: { name: 'Sztabka 4 g', metal: METAL_GOLD, fineWeight: 4, weightUnit: 'Gram' },
    });
    expect(metal.ok()).toBeTruthy();
    const metalId = ((await metal.json()) as { assetId: string }).assetId;
    const bought = await request.post(`/api/portfolio/assets/${metalId}/transactions`, {
      headers,
      data: {
        type: TYPE_BUY,
        quantity: 1,
        totalAmount: 4800,
        date: warsawDate(),
        cashAssetId,
      },
    });
    expect(bought.ok()).toBeTruthy();

    await syncPrices(page);
    await expectNetWorthProjected(request, token, 61050);

    await assignAssets(page, RETIREMENT, [
      { name: 'VWCE.DE', checked: true },
      { name: 'Sztabka 4 g', checked: true },
      { name: 'Sejf', checked: true },
    ]);
  });

  await test.step('Krok 1: przełącznik zakresu z Całość (wybrana), Emerytura i Inwestycje w swoich kolorach', async () => {
    await openPage(page, 'Alokacja');
    await expect(page).toHaveURL(/\/allocation$/);
    await expect(scopeSwitcher(page)).toBeVisible();
    await expect(scopeChip(page, WHOLE)).toHaveAttribute('aria-checked', 'true');
    await expect(scopeChip(page, RETIREMENT)).toHaveAttribute('aria-checked', 'false');
    await expect(scopeChip(page, INVESTMENTS)).toHaveAttribute('aria-checked', 'false');
    await expect(scopeChip(page, RETIREMENT).getByTestId('scope-swatch')).toHaveCSS(
      'background-color',
      'rgb(76, 110, 245)',
    );
    await expect(scopeChip(page, INVESTMENTS).getByTestId('scope-swatch')).toHaveCSS(
      'background-color',
      'rgb(18, 184, 134)',
    );
    await expect(page.getByRole('heading', { name: 'Alokacja docelowa', level: 1 })).toBeVisible();
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(page.getByTestId('plan-note')).toHaveCount(0);
  });

  await test.step('Krok 2: w Całość zapis Obligacje 50% ±5, Akcje 40% ±5, Złoto 10% ±3 liczony z całego majątku', async () => {
    await expect(page.getByTestId('editor-row')).toHaveCount(3);
    await setRow(editorRow(page, 0), { name: 'Akcje', target: '40', band: '5' });
    await setRow(editorRow(page, 1), { name: 'Obligacje', target: '50', band: '5' });
    await pickClass(page, editorRow(page, 1), 'Lokata');
    await setRow(editorRow(page, 2), { name: 'Złoto', target: '10', band: '3' });
    await expect(page.getByTestId('sum')).toContainText(/100\s%/);
    await page.getByTestId('save').click();
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('allocation-view')).toBeVisible({ timeout: 2_000 });
      await expect(page.getByTestId('group-row')).toHaveCount(3, { timeout: 2_000 });
      await expectViewRow(page, 'Obligacje', { value: '30 000,00', target: '50', band: '45–55' });
      await expectViewRow(page, 'Akcje', { value: '21 250,00', target: '40', band: '35–45' });
      await expectViewRow(page, 'Złoto', { value: '4 800,00', target: '10', band: '7–13' });
      await expectSummary(page, '56 050,00', '61 050,00');
    });
  });

  await test.step('Krok 3: wybór Emerytura daje ?group=, pusty stan z dwiema akcjami i linię o planie przelewów', async () => {
    await selectScope(page, RETIREMENT);
    await expect(page).toHaveURL(/[?&]group=[0-9a-f-]{36}/);
    await expect(page.getByTestId('group-empty')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Utwórz alokację' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Skopiuj z całości' })).toBeVisible();
    await expect(page.getByTestId('plan-note')).toContainText(
      'Plan przelewów jest liczony z alokacji „Całość”',
    );
  });

  await test.step('Krok 4: Skopiuj z całości wypełnia edytor, po odświeżeniu znów pusty stan', async () => {
    await page.getByTestId('copy-from-whole').click();
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(page.getByTestId('editor-row')).toHaveCount(3);
    const expected = [
      { name: 'Akcje', target: '40', band: '5' },
      { name: 'Obligacje', target: '50', band: '5' },
      { name: 'Złoto', target: '10', band: '3' },
    ];
    for (const [index, row] of expected.entries()) {
      await expect(editorRow(page, index).getByTestId('group-name')).toHaveValue(row.name);
      await expect(editorRow(page, index).getByTestId('group-target')).toHaveValue(row.target);
      await expect(editorRow(page, index).getByTestId('group-band')).toHaveValue(row.band);
    }
    await reloadPage(page);
    await expect(scopeChip(page, RETIREMENT)).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('group-empty')).toBeVisible();
    await expect(page.getByTestId('allocation-editor')).toHaveCount(0);
  });

  await test.step('Krok 5: Obligacje 60% ±5, Akcje 30% ±5, Złoto 10% ±3 porównane z bazą ok. 56 050 zł', async () => {
    await page.getByTestId('copy-from-whole').click();
    await expect(page.getByTestId('editor-row')).toHaveCount(3);
    await setRow(editorRow(page, 0), { target: '30' });
    await setRow(editorRow(page, 1), { target: '60' });
    await setRow(editorRow(page, 2), { target: '10' });
    await expect(page.getByTestId('sum')).toContainText(/100\s%/);
    await page.getByTestId('save').click();
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('allocation-view')).toBeVisible({ timeout: 2_000 });
      await expectViewRow(page, 'Obligacje', {
        value: '30 000,00',
        current: '53,5',
        target: '60',
        band: '55–65',
        deviationPln: '-3 630,00',
        outOfBand: true,
      });
      await expectViewRow(page, 'Akcje', {
        value: '21 250,00',
        current: '37,9',
        target: '30',
        band: '25–35',
        deviationPln: '+4 435,00',
        outOfBand: true,
      });
      await expectViewRow(page, 'Złoto', {
        value: '4 800,00',
        current: '8,6',
        target: '10',
        band: '7–13',
        outOfBand: false,
      });
      await expectSummary(page, '56 050,00', '61 050,00');
      await expectOutside(page, '5 000,00');
    });
  });

  await test.step('Krok 6: powrót do Całość pokazuje dotychczasowy wynik bez zmian', async () => {
    await selectScope(page, WHOLE);
    await expect(page).not.toHaveURL(/[?&]group=/);
    await expectWholeView(page);
  });

  await test.step('Krok 8: po przeniesieniu VWCE.DE do Inwestycje alokacja Emerytury liczy bazę 34 800 zł', async () => {
    await assignAssets(page, INVESTMENTS, [{ name: 'VWCE.DE', checked: true }]);
    await openAllocation(page, RETIREMENT);
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('allocation-view')).toBeVisible({ timeout: 2_000 });
      await expectViewRow(page, 'Obligacje', {
        value: '30 000,00',
        current: '86,2',
        target: '60',
        band: '55–65',
      });
      await expectViewRow(page, 'Akcje', {
        value: '0,00',
        current: '0,0',
        target: '30',
        band: '25–35',
      });
      await expectViewRow(page, 'Złoto', {
        value: '4 800,00',
        current: '13,8',
        target: '10',
        band: '7–13',
      });
      await expectSummary(page, '34 800,00', '39 800,00');
      await expectOutside(page, '5 000,00');
    });
  });

  await test.step('Krok 9: Inwestycje z grupą klas Akcje 100% ±5 pokazują ETF 21 250 zł jako 100,0% w paśmie', async () => {
    await selectScope(page, INVESTMENTS);
    await expect(page).toHaveURL(/[?&]group=[0-9a-f-]{36}/);
    investmentsId = groupIdFromUrl(page);
    await expect(page.getByTestId('group-empty')).toBeVisible();
    await page.getByTestId('create-allocation').click();
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(page.getByTestId('editor-row')).toHaveCount(0);
    await page.getByRole('button', { name: 'Dodaj grupę klas' }).click();
    await setRow(editorRow(page, 0), { name: 'Akcje', target: '100', band: '5' });
    await pickClass(page, editorRow(page, 0), 'Akcje');
    await pickClass(page, editorRow(page, 0), 'ETF');
    await page.getByTestId('save').click();
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('allocation-view')).toBeVisible({ timeout: 2_000 });
      await expectViewRow(page, 'Akcje', {
        value: '21 250,00',
        current: '100,0',
        target: '100',
        band: '95–100',
        outOfBand: false,
      });
    });
  });

  await test.step('Krok 10: po przeniesieniu VWCE.DE do Bez grupy Inwestycje pokazują cele bez kwot', async () => {
    await assignAssets(page, INVESTMENTS, [{ name: 'VWCE.DE', checked: false }]);
    await openAllocation(page, INVESTMENTS);
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('group-no-value')).toBeVisible({ timeout: 2_000 });
      await expect(page.getByTestId('group-no-value')).toContainText(
        'Ta grupa aktywów nie ma jeszcze wartości',
      );
      await expectViewRow(page, 'Akcje', { target: '100', band: '95–100' });
      await expect(page.getByRole('columnheader', { name: 'Wartość' })).toHaveCount(0);
      await expect(page.getByTestId('summary')).toHaveCount(0);
    });

    await selectScope(page, RETIREMENT);
    await expectProjectedView(page, async () => {
      await expectViewRow(page, 'Obligacje', {
        value: '30 000,00',
        current: '86,2',
        target: '60',
        band: '55–65',
      });
      await expectSummary(page, '34 800,00', '39 800,00');
    });

    await selectScope(page, WHOLE);
    await expectProjectedView(page, async () => {
      await expectViewRow(page, 'Akcje', { value: '21 250,00', target: '40', band: '35–45' });
    });
  });

  await test.step('Krok 11: odświeżenie przy ?group=<id Inwestycji> zostawia zakres Inwestycje', async () => {
    await page.goto(`/allocation?group=${investmentsId}`);
    await expect(scopeChip(page, INVESTMENTS)).toHaveAttribute('aria-checked', 'true');
    await reloadPage(page);
    await expect(scopeChip(page, INVESTMENTS)).toHaveAttribute('aria-checked', 'true');
    await expect(page).toHaveURL(new RegExp(`[?&]group=${investmentsId}`));
  });

  await test.step('Krok 12: nieznany ?group= wraca do Całość bez błędu', async () => {
    await page.goto(`/allocation?group=${UNKNOWN_GROUP}`);
    await expect(scopeChip(page, WHOLE)).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  let retirementAssets = '';

  await test.step('Krok 13: Usuń alokację w Emerytura po potwierdzeniu daje pusty stan, aktywa i Całość bez zmian', async () => {
    await openPage(page, 'Grupy');
    retirementAssets = (await groupRow(page, RETIREMENT).getByTestId('group-count').innerText())
      .replace(/\s+/g, ' ')
      .trim();
    await openAllocation(page, RETIREMENT);
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await page.getByTestId('delete-allocation').click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('group-empty')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Utwórz alokację' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Skopiuj z całości' })).toBeVisible();
    await expect(page.getByTestId('plan-note')).toBeVisible();

    await openPage(page, 'Grupy');
    await expect(groupRow(page, RETIREMENT).getByTestId('group-count')).toHaveText(
      new RegExp(retirementAssets.replace(/\s/g, '\\s')),
    );

    await openAllocation(page, WHOLE);
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('allocation-view')).toBeVisible({ timeout: 2_000 });
      await expectViewRow(page, 'Obligacje', { value: '30 000,00', target: '50', band: '45–55' });
      await expectViewRow(page, 'Akcje', { value: '21 250,00', target: '40', band: '35–45' });
      await expectViewRow(page, 'Złoto', { value: '4 800,00', target: '10', band: '7–13' });
    });
  });

  await test.step('Krok 14: po powrocie VWCE.DE do Inwestycje zapisana alokacja nadal jest, ETF 100,0% w paśmie', async () => {
    await assignAssets(page, INVESTMENTS, [{ name: 'VWCE.DE', checked: true }]);
    await openAllocation(page, INVESTMENTS);
    await expectProjectedView(page, async () => {
      await expect(page.getByTestId('allocation-view')).toBeVisible({ timeout: 2_000 });
      await expectViewRow(page, 'Akcje', {
        value: '21 250,00',
        current: '100,0',
        target: '100',
        band: '95–100',
        outOfBand: false,
      });
    });
  });

  await test.step('Krok 15: po usunięciu grupy Inwestycje chip znika, a stary adres otwiera Całość', async () => {
    await openPage(page, 'Grupy');
    await groupRow(page, INVESTMENTS).getByTestId('delete-group').click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('group-row')).toHaveCount(1);

    await openPage(page, 'Alokacja');
    await expect(scopeChip(page, RETIREMENT)).toBeVisible();
    await expect(scopeChip(page, INVESTMENTS)).toHaveCount(0);
    await page.goto(`/allocation?group=${investmentsId}`);
    await expect(scopeChip(page, WHOLE)).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  await test.step('Krok 16: ponownie założona grupa Inwestycje jest w przełączniku bez alokacji', async () => {
    await openPage(page, 'Grupy');
    await createGroup(page, { name: INVESTMENTS, color: 'Zielony' });
    await openAllocation(page, INVESTMENTS);
    await expect(page).toHaveURL(/[?&]group=[0-9a-f-]{36}/);
    expect(groupIdFromUrl(page)).not.toBe(investmentsId);
    await expect(page.getByTestId('group-empty')).toBeVisible();
    await expect(page.getByTestId('allocation-view')).toHaveCount(0);
  });

  await test.step('Krok 17: na szerokości telefonu chipy się zawijają, a edytor i porównanie mieszczą się bez przewijania poziomego', async () => {
    await page.setViewportSize({ width: 390, height: 844 });
    const fits = async () =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      );
    await selectScope(page, WHOLE);
    await expect(page.getByTestId('allocation-view')).toBeVisible();
    await expect(scopeSwitcher(page)).toBeVisible();
    await expect(async () => expect(await fits()).toBe(true)).toPass({ timeout: 10_000 });
    const switcher = await scopeSwitcher(page).boundingBox();
    expect(switcher).not.toBeNull();
    expect((switcher?.x ?? 0) + (switcher?.width ?? 0)).toBeLessThanOrEqual(390);

    await page.getByTestId('edit').click();
    await expect(page.getByTestId('allocation-editor')).toBeVisible();
    await expect(async () => expect(await fits()).toBe(true)).toPass({ timeout: 10_000 });
  });
});
