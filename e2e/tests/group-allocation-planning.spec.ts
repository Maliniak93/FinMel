import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  logInThroughApi,
  logInThroughUi,
  test,
  warsawDate,
} from './fixtures';
import {
  MBANK,
  addExpense,
  addIncome,
  choose,
  editExpenseFrom,
  exactly,
  expectCost,
  expectFreeFunds,
  monthLabel,
  openMonth,
  pln,
  shiftMonth,
} from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const VARIANT_SAVINGS = 1;
const TERM_UNIT_MONTHS = 1;
const CAPITALIZATION_AT_MATURITY = 0;

const CUSHION = 'Poduszka finansowa';
const CURRENT_EXPENSES = 'Bieżące wydatki';
const TARGETED = 'Oszczędności celowe';
const PENSION = 'Emerytura';
const INVESTMENTS = 'Inwestycje';
const HOLIDAY = 'Wakacje';
const SALARY = 'Wypłata';
const RENT = 'Czynsz';

const CURRENT = warsawDate().slice(0, 7);

function m(offset: number): string {
  return shiftMonth(CURRENT, offset);
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function dialogOf(page: Page): Locator {
  return page.getByRole('dialog');
}

async function openGroupDialog(page: Page): Promise<Locator> {
  await page.getByTestId('add-group').click();
  const dialog = dialogOf(page);
  await expect(dialog.getByRole('heading', { name: 'Dodaj grupę' })).toBeVisible();
  return dialog;
}

async function fillAllocation(
  page: Page,
  dialog: Locator,
  rows: { assetClass: string; percent: string }[],
): Promise<void> {
  for (const [index, row] of rows.entries()) {
    await dialog.getByTestId('add-allocation-row').click();
    await choose(page, dialog.getByRole('combobox', { name: 'Klasa' }).nth(index), row.assetClass);
    await dialog.getByRole('spinbutton', { name: 'Udział (%)' }).nth(index).fill(row.percent);
  }
}

async function createGroup(
  page: Page,
  group: {
    name: string;
    color: string;
    kind?: string;
    target?: string;
    contribution?: string;
    allocation?: { assetClass: string; percent: string }[];
  },
): Promise<void> {
  const dialog = await openGroupDialog(page);
  await dialog.getByLabel('Nazwa').fill(group.name);
  await dialog.getByRole('radio', { name: group.color }).click();
  if (group.kind) {
    await choose(page, dialog.getByRole('combobox', { name: 'Rodzaj' }), group.kind);
  }
  if (group.target) {
    await dialog.getByLabel('Cel (PLN)').fill(group.target);
  }
  if (group.contribution) {
    await dialog.getByLabel('Wpłata miesięczna (PLN)').fill(group.contribution);
  }
  if (group.allocation) {
    await fillAllocation(page, dialog, group.allocation);
  }
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

function groupRow(page: Page, name: string): Locator {
  return page
    .getByTestId('group-row')
    .filter({ has: page.getByTestId('group-name').getByText(name, { exact: true }) });
}

function splitGroup(page: Page, name: string): Locator {
  return page.getByTestId('split-group').filter({
    has: page.locator('.planning-definition__name', { hasText: exactly(name) }),
  });
}

function allocationOf(amount: string): RegExp {
  return new RegExp(`Przydział:\\s${pln(amount).source}`);
}

function shareInput(page: Page, name: string): Locator {
  return page.getByRole('spinbutton', { name: `${name} (%)`, exact: true });
}

function contributionOf(page: Page, name: string): Locator {
  return page.getByTestId('group-contribution').filter({ hasText: name });
}

async function createSavings(
  request: APIRequestContext,
  token: string,
  account: { name: string; groupId?: string },
): Promise<{ id: string; cashAssetId: string }> {
  return createAccountThroughApi(request, token, {
    name: account.name,
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_SAVINGS,
    annualInterestRatePercent: 0,
    groupId: account.groupId,
  } as Parameters<typeof createAccountThroughApi>[2]);
}

async function dashboardRefetch(page: Page): Promise<void> {
  await page.goto('/dashboard');
}

async function expectCushionOnDashboard(page: Page, level: string): Promise<void> {
  await expectProjected(
    () => dashboardRefetch(page),
    async () => {
      const text = page.getByTestId('cushion-level');
      await expect(text).toContainText(pln(level), { timeout: 2_000 });
      await expect(text).toContainText(pln('12 000,00'), { timeout: 2_000 });
      await expect(text).toContainText(/brakuje\s1\s?500,00\szł/, { timeout: 2_000 });
    },
    { timeout: 60_000 },
  );
}

test('group allocation planning: groups with kind, class allocation and contribution split the free funds', async ({
  page,
  request,
  user,
}) => {
  test.setTimeout(900_000);

  const token = await logInThroughApi(request, user);
  const headers = authorization(token);
  const mbank = await request.post('/api/portfolio/accounts', {
    headers,
    data: {
      name: MBANK,
      currency: 'PLN',
      type: ACCOUNT_TYPE_BANK,
      wrapper: WRAPPER_REGULAR,
      variant: VARIANT_CURRENT,
      annualInterestRatePercent: 0,
    },
  });
  expect(mbank.ok()).toBeTruthy();
  const mbankAccount = (await mbank.json()) as { cashAssetId: string };
  await depositCashThroughApi(request, token, mbankAccount.cashAssetId, {
    quantity: 1000,
    date: warsawDate(),
  });

  await page.goto('/login');
  await logInThroughUi(page, user);
  const dialog = dialogOf(page);

  await test.step('Krok 1: Poduszka finansowa (kind Poduszka, target 12 000 zł) needs a target and a second cushion is refused', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    await expect(page.getByTestId('group-empty')).toBeVisible();

    const first = await openGroupDialog(page);
    await first.getByLabel('Nazwa').fill(CUSHION);
    await first.getByRole('radio', { name: 'Pomarańczowy' }).click();
    await choose(page, first.getByRole('combobox', { name: 'Rodzaj' }), 'Poduszka');
    await first.getByRole('button', { name: 'Zapisz' }).click();
    await expect(first.getByText('Poduszka wymaga celu.')).toBeVisible();
    await first.getByLabel('Cel (PLN)').fill('12000');
    await first.getByRole('button', { name: 'Zapisz' }).click();
    await expect(first).toBeHidden();
    await expect(page.getByTestId('group-row')).toHaveCount(1);
    await expect(groupRow(page, CUSHION).getByTestId('group-kind')).toContainText('Poduszka');
    await expect(groupRow(page, CUSHION).getByTestId('group-target')).toContainText(
      pln('12 000,00'),
    );

    const second = await openGroupDialog(page);
    await second.getByLabel('Nazwa').fill('Druga poduszka');
    await second.getByRole('radio', { name: 'Czerwony' }).click();
    await choose(page, second.getByRole('combobox', { name: 'Rodzaj' }), 'Poduszka');
    await second.getByLabel('Cel (PLN)').fill('1000');
    await second.getByRole('button', { name: 'Zapisz' }).click();
    await expect(second.getByText('Możesz mieć tylko jedną poduszkę.')).toBeVisible();
    await second.getByRole('button', { name: 'Anuluj' }).click();
    await expect(second).toBeHidden();
    await expect(page.getByTestId('group-row')).toHaveCount(1);
  });

  await test.step('Krok 2: the regular groups exist, an allocation not summing to 100% is refused and a group without allocation has a single contribution amount', async () => {
    await createGroup(page, { name: CURRENT_EXPENSES, color: 'Niebieski' });
    await createGroup(page, { name: TARGETED, color: 'Zielony' });

    const pension = await openGroupDialog(page);
    await pension.getByLabel('Nazwa').fill(PENSION);
    await pension.getByRole('radio', { name: 'Fioletowy' }).click();
    await pension.getByLabel('Wpłata miesięczna (PLN)').fill('2000');
    await fillAllocation(page, pension, [
      { assetClass: 'Lokata', percent: '50' },
      { assetClass: 'Konta bankowe', percent: '40' },
    ]);
    await expect(pension.getByTestId('allocation-sum')).toContainText('90');
    await pension.getByRole('button', { name: 'Zapisz' }).click();
    await expect(pension).toBeVisible();
    await expect(page.getByTestId('group-row')).toHaveCount(3);
    await pension.getByRole('spinbutton', { name: 'Udział (%)' }).nth(1).fill('50');
    await pension.getByRole('button', { name: 'Zapisz' }).click();
    await expect(pension).toBeHidden();

    await createGroup(page, {
      name: INVESTMENTS,
      color: 'Czerwony',
      contribution: '1000',
      allocation: [
        { assetClass: 'Lokata', percent: '50' },
        { assetClass: 'Konta bankowe', percent: '50' },
      ],
    });

    await expect(page.getByTestId('group-row')).toHaveCount(5);
    await expect(groupRow(page, PENSION).getByTestId('group-contribution')).toContainText(
      pln('2 000,00'),
    );
    await expect(groupRow(page, PENSION).getByTestId('group-allocation')).toContainText(
      /Lokata\s50.*Konta bankowe\s50/,
    );
    await expect(groupRow(page, INVESTMENTS).getByTestId('group-contribution')).toContainText(
      pln('1 000,00'),
    );
    await expect(groupRow(page, CURRENT_EXPENSES).getByTestId('group-contribution')).toHaveCount(0);
    await expect(groupRow(page, CURRENT_EXPENSES).getByTestId('group-allocation')).toHaveCount(0);
  });

  const listed = await request.get('/api/portfolio/asset-groups', { headers });
  expect(listed.ok()).toBeTruthy();
  const groups = (await listed.json()) as { id: string; name: string }[];
  const groupId = (name: string): string => {
    const found = groups.find((group) => group.name === name);
    expect(found).toBeTruthy();
    return found?.id ?? '';
  };

  await test.step('Krok 3: a savings account with 10 500 zł in Poduszka finansowa shows 10 500 of 12 000 zł, 1 500 zł missing on the dashboard', async () => {
    const savings = await createSavings(request, token, {
      name: 'Oszczędnościowe Poduszka',
      groupId: groupId(CUSHION),
    });
    await depositCashThroughApi(request, token, savings.cashAssetId, {
      quantity: 10500,
      date: warsawDate(),
    });
    await expectCushionOnDashboard(page, '10 500,00');
  });

  await test.step('Krok 4: Emerytura with a 9 000 zł deposit and 1 000 zł on a savings account shows Lokata 90% and Konta bankowe 10% against the 50/50 target', async () => {
    const savings = await createSavings(request, token, {
      name: 'Oszczędnościowe Emerytura',
      groupId: groupId(PENSION),
    });
    await depositCashThroughApi(request, token, savings.cashAssetId, {
      quantity: 10000,
      date: warsawDate(30),
    });
    const deposit = await request.post('/api/portfolio/deposits', {
      headers,
      data: {
        name: 'Lokata Emerytura',
        sourceAccountId: savings.id,
        groupId: groupId(PENSION),
        principal: 9000,
        startDate: warsawDate(10),
        termLength: 12,
        termUnit: TERM_UNIT_MONTHS,
        annualInterestRatePercent: 6,
        capitalization: CAPITALIZATION_AT_MATURITY,
      },
    });
    expect(deposit.ok()).toBeTruthy();

    await expectProjected(
      () => dashboardRefetch(page),
      async () => {
        const shares = page.getByTestId('group-class-shares').filter({ hasText: 'Lokata' });
        await expect(shares.first()).toContainText(
          /Lokata:\s90(?:,0+)?\s?%\s\(cel\s50(?:,0+)?\s?%\)/,
          { timeout: 2_000 },
        );
        await expect(shares.first()).toContainText(
          /Konta bankowe:\s10(?:,0+)?\s?%\s\(cel\s50(?:,0+)?\s?%\)/,
          { timeout: 2_000 },
        );
      },
      { timeout: 60_000 },
    );
  });

  await test.step('Krok 5: /allocation does not exist, there is no menu item and the dashboard has no target allocation card', async () => {
    await page.goto('/allocation');
    await expect(page.getByText('Nie znaleziono strony.')).toBeVisible();
    await expect(page.locator('mat-sidenav').getByRole('link', { name: /alokacj/i })).toHaveCount(
      0,
    );
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', { name: 'Wg grup' })).toBeVisible();
    await expect(page.getByRole('heading', { name: /alokacj/i })).toHaveCount(0);
  });

  await test.step('Krok 6: Wypłata 8 000 zł and Czynsz 1 000 zł from last month leave 7 000 zł of free funds in the past month, without contributions or a split', async () => {
    await openMonth(page, m(0));
    await addIncome(page, {
      name: SALARY,
      amount: '8000',
      account: MBANK,
      start: `${m(-1)}-01`,
      interval: '1',
      day: 10,
    });
    await addExpense(page, {
      name: RENT,
      amount: '1000',
      start: `${m(-1)}-01`,
      interval: '1',
      day: 1,
    });
    await openMonth(page, m(-1));
    await expectFreeFunds(page, '7 000,00');
    await expectCost(page, '1 000,00');
    await expect(page.getByTestId('group-contribution')).toHaveCount(0);
    await expect(page.getByTestId('split-group')).toHaveCount(0);
  });

  await test.step('Krok 7: the current month has the contributions of Emerytura 2 000 zł and Inwestycje 1 000 zł among the expenses and 4 000 zł of free funds', async () => {
    await openMonth(page, m(0));
    await expect(contributionOf(page, PENSION)).toContainText(pln('2 000,00'));
    await expect(contributionOf(page, INVESTMENTS)).toContainText(pln('1 000,00'));
    await expectCost(page, '4 000,00');
    await expectFreeFunds(page, '4 000,00');
  });

  await test.step('Krok 8: the split 30/10/10/50 is saved, it must sum to 100% and the cushion has no percentage of its own', async () => {
    await expect(page.getByTestId('split-section')).toBeVisible();
    await expect(page.getByRole('spinbutton', { name: /^Poduszka/ })).toHaveCount(0);
    const save = page.getByTestId('shares-save');
    await shareInput(page, CURRENT_EXPENSES).fill('30');
    await expect(save).toBeDisabled();
    await shareInput(page, PENSION).fill('10');
    await shareInput(page, INVESTMENTS).fill('10');
    await shareInput(page, TARGETED).fill('50');
    await expect(page.getByTestId('share-sum')).toContainText(/Suma:\s*100/);
    await expect(save).toBeEnabled();
    await save.click();
    await expect(save).toBeDisabled();
  });

  await test.step('Krok 9: the split of the current month gives Poduszka 1 500 zł, Bieżące wydatki 750, Emerytura 250, Inwestycje 250 and Oszczędności celowe 1 250 zł', async () => {
    await expectProjected(
      () => openMonth(page, m(0)),
      async () => {
        await expect(page.getByTestId('split-valuation-missing')).toHaveCount(0, {
          timeout: 2_000,
        });
        await expect(page.getByTestId('split-cushion')).toContainText(pln('10 500,00'), {
          timeout: 2_000,
        });
        await expect(page.getByTestId('split-cushion')).toContainText(allocationOf('1 500,00'), {
          timeout: 2_000,
        });
      },
      { timeout: 60_000 },
    );
    await expect(page.getByTestId('split-cushion')).toContainText(/Brakuje:\s1\s?500,00\szł/);
    await expect(splitGroup(page, CURRENT_EXPENSES)).toContainText(allocationOf('750,00'));
    await expect(splitGroup(page, PENSION)).toContainText(allocationOf('250,00'));
    await expect(splitGroup(page, INVESTMENTS)).toContainText(allocationOf('250,00'));
    await expect(splitGroup(page, TARGETED)).toContainText(allocationOf('1 250,00'));
  });

  await test.step('Krok 10: the Emerytura contribution of 2 000 zł goes to Konta bankowe, Lokata gets 0 zł', async () => {
    const pension = splitGroup(page, PENSION);
    await pension.getByTestId('split-breakdown-toggle').click();
    const parts = pension.getByTestId('split-breakdown');
    await expect(parts.filter({ hasText: 'Lokata' })).toContainText(pln('0,00'));
    await expect(parts.filter({ hasText: 'Konta bankowe' })).toContainText(pln('2 000,00'));
  });

  await test.step('Krok 11: the Inwestycje contribution of 1 000 zł is split 500 / 500 zł by the allocation percentages', async () => {
    const investments = splitGroup(page, INVESTMENTS);
    await investments.getByTestId('split-breakdown-toggle').click();
    const parts = investments.getByTestId('split-breakdown');
    await expect(parts.filter({ hasText: 'Lokata' })).toContainText(pln('500,00'));
    await expect(parts.filter({ hasText: 'Konta bankowe' })).toContainText(pln('500,00'));
  });

  await test.step('Krok 12: next month the cushion is at 12 000 zł, missing 0 and the whole 4 000 zł goes by percentages 1 200 / 400 / 400 / 2 000 zł', async () => {
    await page.getByTestId('next-month').click();
    await expect(page.getByTestId('month-label')).toHaveText(monthLabel(m(1)));
    await expect(page.getByTestId('split-cushion')).toContainText(pln('12 000,00'));
    await expect(page.getByTestId('split-cushion')).toContainText(/Brakuje:\s0,00\szł/);
    await expect(splitGroup(page, CURRENT_EXPENSES)).toContainText(allocationOf('1 200,00'));
    await expect(splitGroup(page, PENSION)).toContainText(allocationOf('400,00'));
    await expect(splitGroup(page, INVESTMENTS)).toContainText(allocationOf('400,00'));
    await expect(splitGroup(page, TARGETED)).toContainText(allocationOf('2 000,00'));
  });

  await test.step('Krok 13: Emerytura contribution set to 0 zł raises the free funds of the current month to 6 000 zł and removes the contribution and its breakdown, the past month is unchanged', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    await groupRow(page, PENSION).getByTestId('edit-group').click();
    await expect(dialog.getByRole('heading', { name: 'Edytuj grupę' })).toBeVisible();
    await dialog.getByLabel('Wpłata miesięczna (PLN)').fill('0');
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();

    await openMonth(page, m(0));
    await expectFreeFunds(page, '6 000,00');
    await expect(contributionOf(page, PENSION)).toHaveCount(0);
    await expect(contributionOf(page, INVESTMENTS)).toHaveCount(1);
    await expect(splitGroup(page, PENSION).getByTestId('split-breakdown-toggle')).toHaveCount(0);

    await openMonth(page, m(-1));
    await expectFreeFunds(page, '7 000,00');
  });

  await test.step('Krok 14: a new regular group Wakacje has 0% in the split and the sum stays 100%', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    await createGroup(page, { name: HOLIDAY, color: 'Morski' });
    await openMonth(page, m(0));
    await expect(shareInput(page, HOLIDAY)).toHaveValue('0');
    await expect(page.getByTestId('share-sum')).toContainText(/Suma:\s*100/);
  });

  await test.step('Krok 15: deleting Oszczędności celowe hands its 50% to the other regular groups proportionally and the sum stays 100%', async () => {
    await page.locator('mat-sidenav').getByRole('link', { name: 'Grupy', exact: true }).click();
    await groupRow(page, TARGETED).getByTestId('delete-group').click();
    await dialog.getByRole('button', { name: 'Usuń', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(groupRow(page, TARGETED)).toHaveCount(0);

    await openMonth(page, m(0));
    await expect(shareInput(page, TARGETED)).toHaveCount(0);
    await expect(shareInput(page, CURRENT_EXPENSES)).toHaveValue(/^60(?:\.0)?$/);
    await expect(shareInput(page, PENSION)).toHaveValue(/^20(?:\.0)?$/);
    await expect(shareInput(page, INVESTMENTS)).toHaveValue(/^20(?:\.0)?$/);
    await expect(shareInput(page, HOLIDAY)).toHaveValue(/^0(?:\.0)?$/);
    await expect(page.getByTestId('share-sum')).toContainText(/Suma:\s*100/);
  });

  await test.step('Krok 16: raising Czynsz so that the free funds of the current month are negative shows a deficit and no split amounts', async () => {
    await editExpenseFrom(page, RENT, m(0), { amount: '9000' });
    await expect(page.getByTestId('free-funds-deficit').first()).toBeVisible();
    await expect(page.getByTestId('split-deficit')).toBeVisible();
    await expect(page.getByTestId('split-cushion')).not.toContainText('Przydział');
    await expect(page.getByTestId('split-group').first()).toBeVisible();
    await expect(page.getByTestId('split-section')).not.toContainText('Przydział');
  });

  await test.step('Krok 17: the dashboard net worth includes the assets without a group and Poduszka still shows 10 500 of 12 000 zł, missing 1 500 zł', async () => {
    const loose = await createSavings(request, token, { name: 'Oszczędnościowe bez grupy' });
    await depositCashThroughApi(request, token, loose.cashAssetId, {
      quantity: 2000,
      date: warsawDate(),
    });
    await expectProjected(
      () => dashboardRefetch(page),
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(pln('23 500,00'), {
          timeout: 2_000,
        });
        await expect(page.getByTestId('cushion-level')).toContainText(pln('10 500,00'), {
          timeout: 2_000,
        });
        await expect(page.getByTestId('cushion-level')).toContainText(/brakuje\s1\s?500,00\szł/, {
          timeout: 2_000,
        });
      },
      { timeout: 60_000 },
    );
  });
});
