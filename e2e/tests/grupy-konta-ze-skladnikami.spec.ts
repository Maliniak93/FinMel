import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  openSelect,
  test,
  wealthTab,
  warsawDate,
  type Credentials,
} from './fixtures';
import { expectProjected } from './projection';

const BOND_TYPE_COI = 4;
const BOND_TYPE_EDO = 5;
const ASSET_CLASS_ETF = 3;
const TYPE_BUY = 0;
const PKO_NAME = 'Powszechna Kasa Oszczednosci Bank Polski';

function pln(amount: string): RegExp {
  return new RegExp(`${amount.replace(/ /g, '\\s?')}\\szł`);
}

function exactText(text: string): RegExp {
  const normalized = text.replace(/\s+/g, ' ').trim();
  return new RegExp(
    normalized
      .split(' ')
      .map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
      .join('\\s'),
  );
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

async function loginToken(request: APIRequestContext, user: Credentials): Promise<string> {
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  return ((await login.json()) as { accessToken: string }).accessToken;
}

function groupCard(page: Page, name: string): Locator {
  return page
    .getByTestId('group-card')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

function panelOf(page: Page): Locator {
  return page.getByTestId('assets-panel');
}

function treeRows(page: Page): Locator {
  return panelOf(page).getByTestId('tree-row');
}

function treeRow(page: Page, name: string): Locator {
  return treeRows(page).filter({
    has: page.getByTestId('row-name').getByText(name, { exact: true }),
  });
}

async function rowNames(page: Page): Promise<string[]> {
  return (await panelOf(page).getByTestId('row-name').allInnerTexts()).map((text) => text.trim());
}

async function createGroup(page: Page, group: { name: string; color: string }): Promise<void> {
  await page.getByTestId('add-group').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nazwa').fill(group.name);
  await dialog.getByRole('radio', { name: group.color }).click();
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function openGroupsPage(page: Page): Promise<void> {
  await page.goto('/groups');
  await expect(page.getByTestId('group-card').first()).toBeVisible();
}

async function openMembers(page: Page, group: string): Promise<void> {
  await openGroupsPage(page);
  await groupCard(page, group).getByTestId('select-group').click();
  await expect(panelOf(page).getByTestId('assets-panel-title')).toContainText(group);
}

async function openUngrouped(page: Page): Promise<void> {
  await openGroupsPage(page);
  await page.getByTestId('ungrouped-assets').click();
  await expect(panelOf(page).getByTestId('ungrouped-hint')).toBeVisible();
  await expect(panelOf(page).getByTestId('members-view')).toBeVisible();
}

async function openEdit(page: Page, group: string): Promise<void> {
  await openMembers(page, group);
  await panelOf(page).getByTestId('edit-assignment').click();
  await expect(panelOf(page).getByTestId('edit-view')).toBeVisible();
}

async function expandRow(page: Page, name: string): Promise<void> {
  const toggle = treeRow(page, name).getByTestId('expand-toggle');
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') {
    await toggle.click();
  }
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
}

async function setChecked(page: Page, name: string, checked: boolean): Promise<void> {
  await treeRow(page, name).getByRole('checkbox').setChecked(checked);
}

async function save(page: Page): Promise<void> {
  await panelOf(page).getByTestId('save-assignment').click();
  await expect(panelOf(page).getByTestId('edit-view')).toHaveCount(0);
}

async function expectMembers(page: Page, names: string[]): Promise<void> {
  await expect(async () => {
    expect(await rowNames(page)).toEqual(names);
  }).toPass({ timeout: 10_000 });
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

async function nextPage(page: Page): Promise<void> {
  await panelOf(page)
    .getByRole('button', { name: /^(Next page|Następna strona)$/ })
    .click();
}

async function previousPage(page: Page): Promise<void> {
  await panelOf(page)
    .getByRole('button', { name: /^(Previous page|Poprzednia strona)$/ })
    .click();
}

async function valueText(locator: Locator): Promise<string> {
  return ((await locator.innerText()) ?? '').replace(/\s+/g, ' ').trim();
}

async function createSecurity(
  request: APIRequestContext,
  token: string,
  security: {
    accountId: string;
    query: string;
    ticker: string;
    assetClass: number;
    currency: string;
  },
  quantity: number,
): Promise<void> {
  const className = security.assetClass === ASSET_CLASS_ETF ? 'Etf' : 'Stock';
  const search = await request.get(
    `/api/marketdata/instruments/search?q=${security.query}&assetClass=${className}`,
    { headers: bearer(token) },
  );
  expect(search.ok()).toBeTruthy();
  const found = (
    (await search.json()) as { results: { id: string | null; ticker: string }[] }
  ).results.find((result) => result.ticker === security.ticker);
  expect(found?.id).toBeTruthy();
  // The first FX sync of the stack may not have finished yet, so a rejection is retried for a while.
  await expect(async () => {
    const created = await request.post('/api/portfolio/assets', {
      headers: bearer(token),
      data: {
        accountId: security.accountId,
        assetClass: security.assetClass,
        name: security.ticker,
        currency: security.currency,
        instrumentId: found?.id,
        initialTransaction: {
          type: TYPE_BUY,
          quantity,
          unitPrice: 100,
          date: warsawDate(3),
        },
      },
    });
    expect(created.ok(), await created.text()).toBeTruthy();
  }).toPass({ timeout: 120_000, intervals: [2_000] });
}

async function addBond(
  request: APIRequestContext,
  token: string,
  bond: { accountId: string; name: string; type: number; bondCount: number },
): Promise<void> {
  const response = await request.post('/api/portfolio/bonds', {
    headers: bearer(token),
    data: {
      accountId: bond.accountId,
      name: bond.name,
      seriesCode: bond.name,
      type: bond.type,
      purchaseDate: warsawDate(3),
      bondCount: bond.bondCount,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 5,
      marginPercent: 1.5,
      earlyRedemptionFeePerBond: 0,
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}

test('account groups: whole accounts with components, per-component exceptions and the paged tree', async ({
  signedInPage: page,
  user,
  request,
}) => {
  test.setTimeout(900_000);
  const token = await loginToken(request, user);
  const dialog = page.getByRole('dialog');

  await test.step('Przygotowanie: 18 PLN cash places Skarbonka 1 to 18', async () => {
    for (let index = 1; index <= 18; index += 1) {
      const place = await request.post('/api/portfolio/cash-places', {
        headers: bearer(token),
        data: { name: `Skarbonka ${index}`, currencies: ['PLN'] },
      });
      expect(place.ok(), await place.text()).toBeTruthy();
    }
  });

  await test.step('Krok 1: groups A and B are two cards of value 0 zł', async () => {
    await page.getByTestId('nav-list').getByRole('link', { name: 'Grupy', exact: true }).click();
    await createGroup(page, { name: 'A', color: 'Niebieski' });
    await createGroup(page, { name: 'B', color: 'Zielony' });
    await expect(page.getByTestId('group-card')).toHaveCount(2);
    for (const name of ['A', 'B']) {
      await expect(groupCard(page, name).getByTestId('group-value')).toContainText(pln('0,00'));
    }
  });

  await test.step('Krok 2: the brokerage IKE eMakler has two components and free cash', async () => {
    const ike = await createAccountThroughApi(request, token, {
      name: 'IKE eMakler',
      type: 0,
      wrapper: 1,
    });
    await depositCashThroughApi(request, token, ike.cashAssetId, {
      quantity: 20000,
      date: warsawDate(7),
    });
    await createSecurity(
      request,
      token,
      {
        accountId: ike.id,
        query: 'VWCE',
        ticker: 'VWCE.DE',
        assetClass: ASSET_CLASS_ETF,
        currency: 'EUR',
      },
      10,
    );
    await createSecurity(
      request,
      token,
      { accountId: ike.id, query: 'CDR', ticker: 'CDR.WA', assetClass: 2, currency: 'PLN' },
      5,
    );
    await wealthTab(page, 'Rachunki inwestycyjne');
    await expect(
      page.getByRole('table').getByRole('link', { name: 'IKE eMakler', exact: true }),
    ).toBeVisible();
  });

  await test.step('Krok 3: the bond account IKE Obligacje has three bonds', async () => {
    const bonds = await createAccountThroughApi(request, token, {
      name: 'IKE Obligacje',
      type: 1,
      wrapper: 1,
    });
    await depositCashThroughApi(request, token, bonds.cashAssetId, {
      quantity: 10000,
      date: warsawDate(7),
    });
    await addBond(request, token, {
      accountId: bonds.id,
      name: 'EDO0136',
      type: BOND_TYPE_EDO,
      bondCount: 10,
    });
    await addBond(request, token, {
      accountId: bonds.id,
      name: 'COI0136',
      type: BOND_TYPE_COI,
      bondCount: 10,
    });
    await addBond(request, token, {
      accountId: bonds.id,
      name: 'EDO0236',
      type: BOND_TYPE_EDO,
      bondCount: 5,
    });
    await wealthTab(page, 'Obligacje');
    for (const series of ['EDO0136', 'COI0136', 'EDO0236']) {
      await expect(page.getByRole('row').filter({ hasText: series })).toBeVisible();
    }
  });

  await test.step('Krok 4: the bank account mBank with 5 000 zł is on the accounts list', async () => {
    const mbank = await createAccountThroughApi(request, token, {
      name: 'mBank',
      type: 2,
      wrapper: 0,
      currency: 'PLN',
      variant: 0,
      annualInterestRatePercent: 0,
    } as Parameters<typeof createAccountThroughApi>[2]);
    await depositCashThroughApi(request, token, mbank.cashAssetId, {
      quantity: 5000,
      date: warsawDate(7),
    });
    await wealthTab(page, 'Konta bankowe');
    await expect(page.getByRole('link', { name: 'mBank', exact: true })).toBeVisible();
  });

  await test.step('Krok 5: group B edit view has 20 rows on page 1 and 1 on page 2, free cash is not a row', async () => {
    await openEdit(page, 'B');
    await expect(treeRows(page)).toHaveCount(20);
    const names = await rowNames(page);
    expect(names.slice(0, 3)).toEqual(['IKE eMakler', 'IKE Obligacje', 'mBank']);
    expect(names.filter((name) => name.startsWith('Skarbonka'))).toHaveLength(17);
    await expect(
      panelOf(page)
        .getByTestId('row-name')
        .getByText(/wolne środki/i),
    ).toHaveCount(0);
    await nextPage(page);
    await expect(treeRows(page)).toHaveCount(1);
    await expect(panelOf(page).locator('.mat-mdc-paginator-range-label')).toContainText('21');
  });

  await test.step('Krok 6: expanding IKE Obligacje shows its three bonds, stays on page 1 and makes 24 rows', async () => {
    await previousPage(page);
    await expandRow(page, 'IKE Obligacje');
    for (const series of ['COI0136', 'EDO0136', 'EDO0236']) {
      await expect(treeRow(page, series)).toBeVisible();
    }
    await expect(treeRows(page)).toHaveCount(20);
    await expect(panelOf(page).locator('.mat-mdc-paginator-range-label')).toContainText('24');
    await nextPage(page);
    await expect(treeRows(page)).toHaveCount(4);
  });

  await test.step('Krok 7: after page 2 and back IKE Obligacje is still expanded', async () => {
    await previousPage(page);
    await expect(treeRow(page, 'IKE Obligacje').getByTestId('expand-toggle')).toHaveAttribute(
      'aria-expanded',
      'true',
    );
    await expect(treeRow(page, 'EDO0136')).toBeVisible();
  });

  await test.step('Krok 8: checking IKE eMakler puts it in B with its cash, VWCE.DE and CDR.WA, and card B shows the same sum', async () => {
    await setChecked(page, 'IKE eMakler', true);
    await save(page);
    await expectMembers(page, ['IKE eMakler', 'CDR.WA', 'VWCE.DE']);
    const card = groupCard(page, 'B');
    await expectProjected(
      () => openMembers(page, 'B'),
      async () => {
        const sum = await valueText(treeRow(page, 'IKE eMakler').getByTestId('row-value'));
        expect(sum).not.toMatch(/^0,00/);
        expect(sum).toMatch(/\d/);
        await expect(card.getByTestId('group-value')).toHaveText(exactText(sum), {
          timeout: 2_000,
        });
        await expect(treeRow(page, 'VWCE.DE').getByTestId('row-value')).toContainText(/[1-9]/, {
          timeout: 2_000,
        });
        await expect(treeRow(page, 'CDR.WA').getByTestId('row-value')).toContainText(/[1-9]/, {
          timeout: 2_000,
        });
      },
    );
  });

  await test.step('Krok 9: in A the account IKE Obligacje has only the first EDO, summed to its value', async () => {
    await openEdit(page, 'A');
    await expandRow(page, 'IKE Obligacje');
    await setChecked(page, 'EDO0136', true);
    await save(page);
    await expectMembers(page, ['IKE Obligacje', 'EDO0136']);
    await expectProjected(
      () => openMembers(page, 'A'),
      async () => {
        const edo = await valueText(treeRow(page, 'EDO0136').getByTestId('row-value'));
        expect(edo).toMatch(/[1-9]/);
        await expect(treeRow(page, 'IKE Obligacje').getByTestId('row-value')).toHaveText(
          exactText(edo),
          { timeout: 2_000 },
        );
      },
    );
  });

  await test.step('Krok 10: in B the first EDO is greyed with the chip A, the account and the other bonds are unchecked', async () => {
    await openEdit(page, 'B');
    await expandRow(page, 'IKE Obligacje');
    const first = treeRow(page, 'EDO0136');
    await expect(first).toHaveAttribute('data-greyed', 'true');
    await expect(first.getByTestId('asset-group-chip')).toHaveText('A');
    await expect(treeRow(page, 'IKE Obligacje').getByRole('checkbox')).not.toBeChecked();
    await expect(treeRow(page, 'COI0136').getByRole('checkbox')).not.toBeChecked();
    await expect(treeRow(page, 'EDO0236').getByRole('checkbox')).not.toBeChecked();
  });

  await test.step('Krok 11: checking the account IKE Obligacje brings its cash, COI and the second EDO to B, A keeps the first EDO', async () => {
    await setChecked(page, 'IKE Obligacje', true);
    await save(page);
    await expectMembers(page, [
      'IKE eMakler',
      'CDR.WA',
      'VWCE.DE',
      'IKE Obligacje',
      'COI0136',
      'EDO0236',
    ]);
    await expectProjected(
      () => openMembers(page, 'B'),
      async () => {
        await expect(treeRow(page, 'IKE Obligacje').getByTestId('row-value')).toContainText(
          /[1-9]/,
          { timeout: 2_000 },
        );
      },
    );
    await openMembers(page, 'A');
    await expectMembers(page, ['IKE Obligacje', 'EDO0136']);
  });

  await test.step('Krok 12: checking the greyed first EDO in B moves it to B and empties A', async () => {
    await openEdit(page, 'B');
    await expandRow(page, 'IKE Obligacje');
    await setChecked(page, 'EDO0136', true);
    await save(page);
    await expectMembers(page, [
      'IKE eMakler',
      'CDR.WA',
      'VWCE.DE',
      'IKE Obligacje',
      'COI0136',
      'EDO0136',
      'EDO0236',
    ]);
    await openMembers(page, 'A');
    await expect(panelOf(page).getByText('W tej grupie nic jeszcze nie ma.')).toBeVisible();
  });

  await test.step('Krok 13: unchecking the first EDO in A returns it to B, which it inherits from the account', async () => {
    await openEdit(page, 'A');
    await expandRow(page, 'IKE Obligacje');
    await setChecked(page, 'EDO0136', true);
    await save(page);
    await expectMembers(page, ['IKE Obligacje', 'EDO0136']);
    await panelOf(page).getByTestId('edit-assignment').click();
    await expect(panelOf(page).getByTestId('edit-view')).toBeVisible();
    await expandRow(page, 'IKE Obligacje');
    await setChecked(page, 'EDO0136', false);
    await save(page);
    await expect(panelOf(page).getByText('W tej grupie nic jeszcze nie ma.')).toBeVisible();
    await openMembers(page, 'B');
    await expectMembers(page, [
      'IKE eMakler',
      'CDR.WA',
      'VWCE.DE',
      'IKE Obligacje',
      'COI0136',
      'EDO0136',
      'EDO0236',
    ]);
  });

  await test.step('Krok 14: unchecking CDR.WA in B leaves it without a group, IKE eMakler keeps VWCE.DE in B', async () => {
    await openEdit(page, 'B');
    await expandRow(page, 'IKE eMakler');
    await setChecked(page, 'CDR.WA', false);
    await save(page);
    await expect(treeRow(page, 'VWCE.DE')).toBeVisible();
    await expect(treeRow(page, 'CDR.WA')).toHaveCount(0);
    await openUngrouped(page);
    await expect(treeRow(page, 'CDR.WA')).toBeVisible();
    await openMembers(page, 'B');
    await expect(treeRow(page, 'IKE eMakler')).toBeVisible();
    await expect(treeRow(page, 'VWCE.DE')).toBeVisible();
  });

  await test.step('Krok 15: 3 units of a new instrument bought on IKE eMakler without a group field land in B', async () => {
    await wealthTab(page, 'Akcje i ETF');
    await page.getByRole('button', { name: 'Dodaj', exact: true }).first().click();
    await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), /^IKE eMakler/);
    await expect(dialog.getByRole('combobox', { name: 'Grupa', exact: true })).toHaveCount(0);
    await expect(dialog.getByRole('combobox', { name: 'Domyślna grupa' })).toHaveCount(0);
    await dialog.getByLabel('Instrument finansowy').fill('PKO');
    await page.getByRole('option', { name: /PKO\.WA/ }).click();
    await expect(dialog.getByText('Zweryfikowano: PKO.WA')).toBeVisible();
    await dialog.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
    await dialog.getByLabel(/^Ilość/).fill('3');
    await dialog.getByLabel(/^Cena jednostkowa/).fill('50');
    await dialog.getByLabel('Data', { exact: true }).fill(warsawDate(1));
    await dialog.getByRole('button', { name: 'Utwórz' }).click();
    await expect(dialog).toBeHidden();
    await openMembers(page, 'B');
    await expect(treeRow(page, PKO_NAME)).toBeVisible();
    await expect(treeRow(page, 'VWCE.DE')).toBeVisible();
  });

  await test.step('Krok 16: unchecking the account IKE Obligacje in B leaves B with only IKE eMakler', async () => {
    await openEdit(page, 'B');
    await setChecked(page, 'IKE Obligacje', false);
    await save(page);
    await expectMembers(page, ['IKE eMakler', PKO_NAME, 'VWCE.DE']);
    await openUngrouped(page);
    for (const name of [
      'IKE eMakler',
      'CDR.WA',
      'IKE Obligacje',
      'COI0136',
      'EDO0136',
      'EDO0236',
    ]) {
      await expect(treeRow(page, name)).toBeVisible();
    }
    await expect(treeRow(page, 'VWCE.DE')).toHaveCount(0);
  });

  await test.step('Krok 17: the account form group B brings IKE Obligacje back to B while the first EDO stays in A', async () => {
    await openEdit(page, 'A');
    await expandRow(page, 'IKE Obligacje');
    await setChecked(page, 'EDO0136', true);
    await save(page);
    await expectMembers(page, ['IKE Obligacje', 'EDO0136']);

    await wealthTab(page, 'Rachunki inwestycyjne');
    await page.getByRole('button', { name: 'Akcje dla: IKE Obligacje', exact: true }).click();
    await page.getByRole('menuitem', { name: 'Edytuj', exact: true }).click();
    await selectOption(page, dialog.getByRole('combobox', { name: 'Grupa konta' }), 'B');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await openEdit(page, 'B');
    await expect(treeRow(page, 'IKE Obligacje').getByRole('checkbox')).toBeChecked();
    await expandRow(page, 'IKE Obligacje');
    await expect(treeRow(page, 'COI0136').getByRole('checkbox')).toBeChecked();
    await expect(treeRow(page, 'EDO0236').getByRole('checkbox')).toBeChecked();
    await expect(treeRow(page, 'EDO0136')).toHaveAttribute('data-greyed', 'true');
    await expect(treeRow(page, 'EDO0136').getByTestId('asset-group-chip')).toHaveText('A');
    await panelOf(page).getByTestId('members-assignment').click();
    await expect(treeRow(page, 'COI0136')).toBeVisible();
    await expect(treeRow(page, 'EDO0236')).toBeVisible();
    await expect(treeRow(page, 'EDO0136')).toHaveCount(0);
  });

  await test.step('Krok 18: filtering group B by the series COI shows only IKE Obligacje, expanded, with COI0136', async () => {
    await panelOf(page).getByTestId('edit-assignment').click();
    await panelOf(page).getByTestId('asset-filter').fill('COI0136');
    await expect(treeRows(page)).toHaveCount(2);
    await expectMembers(page, ['IKE Obligacje', 'COI0136']);
  });

  await test.step('Krok 19: with the filter cleared the class Akcje shows IKE eMakler expanded to its shares', async () => {
    await panelOf(page).getByTestId('asset-filter').fill('');
    await selectOption(page, panelOf(page).getByRole('combobox', { name: 'Klasa' }), 'Akcje');
    await expectMembers(page, ['IKE eMakler', 'CDR.WA', PKO_NAME]);
  });

  await test.step('Krok 20: in A the greyed IKE eMakler (chip B) moves whole to A, CDR.WA stays without a group', async () => {
    await openEdit(page, 'A');
    const account = treeRow(page, 'IKE eMakler');
    await expect(account).toHaveAttribute('data-greyed', 'true');
    await expect(account.getByTestId('asset-group-chip')).toHaveText('B');
    await setChecked(page, 'IKE eMakler', true);
    await save(page);
    await expect(treeRow(page, 'IKE eMakler')).toBeVisible();
    await expect(treeRow(page, 'VWCE.DE')).toBeVisible();
    await expect(treeRow(page, PKO_NAME)).toBeVisible();
    await expect(treeRow(page, 'CDR.WA')).toHaveCount(0);
    await openUngrouped(page);
    await expect(treeRow(page, 'CDR.WA')).toBeVisible();
  });

  await test.step('Krok 21: deleting A leaves IKE eMakler and the first EDO without a group, COI and the second EDO stay in B', async () => {
    await openGroupsPage(page);
    await groupCard(page, 'A').getByTestId('group-menu').click();
    await page.getByTestId('delete-group').click();
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId('group-card')).toHaveCount(1);

    await openUngrouped(page);
    await expect(treeRow(page, 'IKE eMakler')).toBeVisible();
    await expect(treeRow(page, 'VWCE.DE')).toBeVisible();
    await expect(treeRow(page, PKO_NAME)).toBeVisible();
    await expect(treeRow(page, 'CDR.WA')).toBeVisible();
    await expect(treeRow(page, 'EDO0136')).toBeVisible();
    await expect(treeRow(page, 'COI0136')).toHaveCount(0);
    await expect(treeRow(page, 'EDO0236')).toHaveCount(0);

    await openMembers(page, 'B');
    await expectMembers(page, ['IKE Obligacje', 'COI0136', 'EDO0236']);
  });

  await test.step('Krok 22: the dashboard group split shows B equal to its members view sum', async () => {
    let sum = '';
    await expectProjected(
      () => openMembers(page, 'B'),
      async () => {
        sum = await valueText(treeRow(page, 'IKE Obligacje').getByTestId('row-value'));
        expect(sum).toMatch(/[1-9]/);
      },
    );
    await expectProjected(
      () => page.goto('/dashboard'),
      async () => {
        await expect(
          page
            .locator('.dashboard-page__group-rows')
            .getByTestId('group-row')
            .filter({ hasText: /^\s*B\s/ }),
        ).toContainText(exactText(sum), { timeout: 2_000 });
      },
      { timeout: 60_000 },
    );
  });
});
