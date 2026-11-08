import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
} from './fixtures';
import { pln } from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BONDS = 1;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const BOND_TYPE_OTS = 0;
const ASSET_CLASS_DEPOSIT = 1;
const ASSET_CLASS_BANK = 9;

const WIDE = { width: 1440, height: 900 };
const NARROW = { width: 600, height: 900 };
const PHONE = { width: 390, height: 844 };

const LETTERS = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H'];
const COLORS = ['blue', 'green', 'orange', 'purple', 'red', 'teal', 'pink', 'amber'];
const GROUP_AMOUNTS = [1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000];
const DESCENDING = LETTERS.map((letter) => `Grupa ${letter}`).reverse();

const MOTION = /^0\.14s(, 0\.14s)*$/;
const NO_MOTION = /^0s(, 0s)*$/;

interface Seeded {
  headers: { Authorization: string };
  accountId: string;
  cashAssetId: string;
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function heading(page: Page, name: string): Locator {
  return page.getByRole('heading', { name, exact: true });
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

function visibleGroupRows(page: Page): Locator {
  return page.locator('[data-testid="group-row"]:visible');
}

function visibleStructureRows(page: Page): Locator {
  return page.locator('.dashboard-page__structure-row:visible');
}

function groupNames(page: Page): Locator {
  return page.locator('.dashboard-page__group-name');
}

function foldToggles(page: Page): Locator {
  return page.locator('.dashboard-page__fold-toggle');
}

function badge(page: Page): Locator {
  return page.getByTestId('todo-badge');
}

async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(heading(page, 'Pulpit')).toBeVisible();
}

async function seedAccount(
  request: APIRequestContext,
  token: string,
  opening: number,
): Promise<Seeded> {
  const account = await createAccountThroughApi(request, token, {
    name: 'mBank osobiste',
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
  } as Parameters<typeof createAccountThroughApi>[2]);
  await depositCashThroughApi(request, token, account.cashAssetId, {
    quantity: opening,
    date: warsawDate(40),
  });
  return { headers: authorization(token), accountId: account.id, cashAssetId: account.cashAssetId };
}

async function createGroup(
  request: APIRequestContext,
  headers: Seeded['headers'],
  data: Record<string, unknown>,
): Promise<string> {
  const created = await request.post('/api/portfolio/asset-groups', { headers, data });
  expect(created.ok(), await created.text()).toBeTruthy();
  return ((await created.json()) as { id: string }).id;
}

async function assign(
  request: APIRequestContext,
  headers: Seeded['headers'],
  assetId: string,
  groupId: string,
): Promise<void> {
  const assigned = await request.put('/api/portfolio/asset-groups/assignment', {
    headers,
    data: { assetIds: [assetId], groupId },
  });
  expect(assigned.ok()).toBeTruthy();
}

async function createCashPlace(
  request: APIRequestContext,
  token: string,
  name: string,
  amount: number,
): Promise<string> {
  const place = await request.post('/api/portfolio/cash-places', {
    headers: authorization(token),
    data: { name, currencies: ['PLN'] },
  });
  expect(place.ok()).toBeTruthy();
  const assetId = ((await place.json()) as { balances: { cashAssetId: string }[] }).balances[0]
    ?.cashAssetId as string;
  await depositCashThroughApi(request, token, assetId, { quantity: amount, date: warsawDate() });
  return assetId;
}

async function seedEightGroups(
  request: APIRequestContext,
  token: string,
  seeded: Seeded,
): Promise<void> {
  for (const [index, letter] of LETTERS.entries()) {
    const groupId = await createGroup(request, seeded.headers, {
      name: `Grupa ${letter}`,
      color: COLORS[index],
    });
    const assetId =
      index === 0
        ? seeded.cashAssetId
        : await createCashPlace(
            request,
            token,
            `Portfel ${letter}`,
            GROUP_AMOUNTS[index] as number,
          );
    await assign(request, seeded.headers, assetId, groupId);
  }
}

async function createRecurringExpense(
  request: APIRequestContext,
  seeded: Seeded,
  name: string,
  daysAhead: number,
): Promise<string> {
  const date = warsawDate(-daysAhead);
  const created = await request.post('/api/portfolio/recurring-expenses', {
    headers: seeded.headers,
    data: {
      name,
      amount: 100,
      startDate: date,
      endDate: null,
      intervalMonths: 3,
      dayKind: 0,
      dayOfMonth: Number(date.slice(8)),
      shift: 0,
      accountId: seeded.accountId,
    },
  });
  expect(created.ok(), await created.text()).toBeTruthy();
  return ((await created.json()) as { id: string }).id;
}

async function loginToken(
  request: APIRequestContext,
  user: { email: string; password: string },
): Promise<string> {
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  return ((await login.json()) as { accessToken: string }).accessToken;
}

async function resolvedColor(page: Page, variable: string): Promise<string> {
  return page.evaluate((name) => {
    const probe = document.createElement('span');
    probe.style.color = `var(${name})`;
    document.body.appendChild(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color;
  }, variable);
}

async function badgeColor(page: Page): Promise<string> {
  return badge(page).evaluate((element) => getComputedStyle(element).color);
}

async function transitionOf(locator: Locator): Promise<string> {
  return locator.evaluate((element) => getComputedStyle(element).transitionDuration);
}

async function animationOf(locator: Locator): Promise<string> {
  return locator.evaluate((element) => getComputedStyle(element).animationName);
}

async function expectGroupOrder(page: Page, names: string[]): Promise<void> {
  await expect(groupNames(page)).toHaveText(names);
}

async function seedHistory(page: Page): Promise<void> {
  await openDashboard(page);
  await expectProjected(
    () => openDashboard(page),
    async () => {
      await expect(page.locator('.net-worth-chart__svg')).toBeVisible({ timeout: 2_000 });
    },
  );
}

test('szlif ui: dashboard folds on a narrow window and the badge turns red only for overdue', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(600_000);
  const token = await loginToken(request, user);
  await page.setViewportSize(WIDE);
  const seeded = await seedAccount(request, token, 1000);
  await seedEightGroups(request, token, seeded);
  const expenseIds: string[] = [];
  for (const [index, days] of [2, 4, 6].entries()) {
    expenseIds.push(await createRecurringExpense(request, seeded, `Czynsz ${index + 1}`, days));
  }

  await test.step('Krok 1: wide window shows the accent badge 3, eight groups from H to A, the full class list and no fold buttons', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(groupNames(page)).toHaveCount(8, { timeout: 2_000 });
        await expect(badge(page)).toHaveText('3', { timeout: 2_000 });
      },
    );
    await expect(badge(page)).not.toHaveClass(/shell__badge--overdue/);
    expect(await badgeColor(page)).toBe(await resolvedColor(page, '--mat-sys-primary'));
    expect(await badgeColor(page)).not.toBe(await resolvedColor(page, '--sk-negative'));
    await expectGroupOrder(page, DESCENDING);
    const rows = page.getByTestId('group-row');
    await expect(rows.first()).toContainText(pln('8 000,00'));
    await expect(rows.last()).toContainText(pln('1 000,00'));
    await expect(page.locator('.dashboard-page__structure app-split-bar')).toBeVisible();
    await expect(page.locator('.dashboard-page__structure-row')).not.toHaveCount(0);
    await expect(visibleStructureRows(page)).not.toHaveCount(0);
    await expect(foldToggles(page)).toHaveCount(0);
  });

  await test.step('Krok 2: at 600 px five groups and Pokaż wszystkie (8), the class list collapsed under Pokaż szczegóły', async () => {
    await page.setViewportSize(NARROW);
    await expect(page.getByRole('button', { name: 'Pokaż wszystkie (8)' })).toBeVisible();
    await expect(visibleGroupRows(page)).toHaveCount(5);
    await expect(page.locator('.dashboard-page__group-name:visible')).toHaveText(
      DESCENDING.slice(0, 5),
    );
    await expect(page.locator('.dashboard-page__structure app-split-bar')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Pokaż szczegóły' })).toBeVisible();
    await expect(visibleStructureRows(page)).toHaveCount(0);
  });

  await test.step('Krok 3: Pokaż wszystkie reveals C, B, A in order, the button becomes Zwiń and Shift+Tab walks through them', async () => {
    await page.getByRole('button', { name: 'Pokaż wszystkie (8)' }).click();
    const fold = page.getByRole('button', { name: 'Zwiń' });
    await expect(fold).toHaveAttribute('aria-expanded', 'true');
    await expect(visibleGroupRows(page)).toHaveCount(8);
    await expectGroupOrder(page, DESCENDING);
    await fold.focus();
    for (const name of ['Grupa A', 'Grupa B', 'Grupa C']) {
      await page.keyboard.press('Shift+Tab');
      await expect(page.getByRole('link', { name, exact: true })).toBeFocused();
    }
  });

  await test.step('Krok 4: Zwiń hides C, B, A and focus never enters the hidden rows', async () => {
    await page.getByRole('button', { name: 'Zwiń' }).click();
    const more = page.getByRole('button', { name: 'Pokaż wszystkie (8)' });
    await expect(more).toHaveAttribute('aria-expanded', 'false');
    await expect(visibleGroupRows(page)).toHaveCount(5);
    await more.focus();
    await page.keyboard.press('Shift+Tab');
    await expect(page.getByRole('link', { name: 'Grupa D', exact: true })).toBeFocused();
  });

  await test.step('Krok 5: Pokaż szczegóły opens the class list and the button becomes Ukryj szczegóły', async () => {
    await page.getByRole('button', { name: 'Pokaż szczegóły' }).click();
    await expect(page.getByRole('button', { name: 'Ukryj szczegóły' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
    await expect(visibleStructureRows(page)).not.toHaveCount(0);
  });

  await test.step('Krok 6: after a reload both cards start collapsed', async () => {
    await page.reload();
    await expect(page.getByRole('button', { name: 'Pokaż wszystkie (8)' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Pokaż szczegóły' })).toBeVisible();
    await expect(visibleGroupRows(page)).toHaveCount(5);
    await expect(visibleStructureRows(page)).toHaveCount(0);
  });

  await test.step('Krok 7: 9 000 zł without a group is first, five rows and Pokaż wszystkie (9)', async () => {
    await createCashPlace(request, token, 'Skarpeta', 9000);
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(page.getByRole('button', { name: 'Pokaż wszystkie (9)' })).toBeVisible({
          timeout: 2_000,
        });
      },
    );
    await expect(visibleGroupRows(page)).toHaveCount(5);
    await expect(groupNames(page).first()).toHaveText('Bez grupy');
    await expect(page.getByTestId('group-row').first()).toContainText(pln('9 000,00'));
  });

  await test.step('Krok 8: back at 1440 px all nine rows in the same order, class list open, no fold buttons', async () => {
    await page.setViewportSize(WIDE);
    await expectGroupOrder(page, ['Bez grupy', ...DESCENDING]);
    await expect(visibleGroupRows(page)).toHaveCount(9);
    await expect(visibleStructureRows(page)).not.toHaveCount(0);
    await expect(foldToggles(page)).toHaveCount(0);
  });

  await test.step('Krok 13: an overdue expense of 50 zł makes the badge 4 and red', async () => {
    const created = await request.post('/api/portfolio/one-off-expenses', {
      headers: seeded.headers,
      data: { name: 'Zaległy', amount: 50, accountId: seeded.accountId, date: warsawDate(1) },
    });
    expect(created.ok(), await created.text()).toBeTruthy();
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(badge(page)).toHaveText('4', { timeout: 2_000 });
      },
    );
    await expect(badge(page)).toHaveClass(/shell__badge--overdue/);
    expect(await badgeColor(page)).toBe(await resolvedColor(page, '--sk-negative'));
  });

  await test.step('Krok 14: settling it on the Do rozliczenia card brings the badge back to 3 in the accent colour', async () => {
    const row = page
      .locator('app-todo-card')
      .getByTestId('todo-card-item')
      .filter({ hasText: 'Zaległy' });
    await row.getByRole('button', { name: 'Rozlicz', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expect(badge(page)).toHaveText('3');
    await expect(badge(page)).not.toHaveClass(/shell__badge--overdue/);
    expect(await badgeColor(page)).toBe(await resolvedColor(page, '--mat-sys-primary'));
  });

  await test.step('Krok 16: with every to-do item gone the badge disappears', async () => {
    for (const id of expenseIds) {
      const removed = await request.delete(`/api/portfolio/recurring-expenses/${id}`, {
        headers: seeded.headers,
      });
      expect(removed.ok(), await removed.text()).toBeTruthy();
    }
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(badge(page)).toHaveCount(0, { timeout: 2_000 });
      },
    );
  });
});

test('szlif ui: one 140 ms motion for navigation, lists and the range switch', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(600_000);
  const token = await loginToken(request, user);
  await page.setViewportSize(WIDE);
  const seeded = await seedAccount(request, token, 5000);
  await createCashPlace(request, token, 'Portfel', 500);
  for (const [index, name] of ['Grupa A', 'Plan'].entries()) {
    const id = await createGroup(request, seeded.headers, {
      name,
      color: COLORS[index],
      ...(name === 'Plan'
        ? {
            monthlyContributionPln: 500,
            classAllocation: [
              { assetClass: ASSET_CLASS_DEPOSIT, percent: 50 },
              { assetClass: ASSET_CLASS_BANK, percent: 50 },
            ],
          }
        : {}),
    });
    if (name === 'Plan') {
      await assign(request, seeded.headers, seeded.cashAssetId, id);
    }
  }
  const bondAccount = await createAccountThroughApi(request, token, {
    name: 'Obligacje PKO',
    type: ACCOUNT_TYPE_BONDS,
    wrapper: WRAPPER_REGULAR,
  });
  await depositCashThroughApi(request, token, bondAccount.cashAssetId, {
    quantity: 2000,
    date: warsawDate(40),
  });
  const bond = await request.post('/api/portfolio/bonds', {
    headers: seeded.headers,
    data: {
      accountId: bondAccount.id,
      name: 'OTS0127',
      seriesCode: 'OTS0127',
      type: BOND_TYPE_OTS,
      purchaseDate: warsawDate(),
      bondCount: 10,
      purchasePricePerBond: 100,
      firstPeriodRatePercent: 2,
      earlyRedemptionFeePerBond: 0,
    },
  });
  expect(bond.ok(), await bond.text()).toBeTruthy();
  await createRecurringExpense(request, seeded, 'Czynsz', 3);

  await test.step('Krok 9: navigation colours change in 0.14 s in the sidebar and in the bottom navigation', async () => {
    await seedHistory(page);
    const links = page.locator('.shell__nav-link:visible');
    await links.nth(1).hover();
    expect(await transitionOf(links.nth(1))).toMatch(MOTION);
    await page.getByRole('link', { name: /^Majątek/ }).click();
    await expect(page.locator('.shell__nav-link--active:visible')).toHaveCount(1);
    expect(await transitionOf(page.locator('.shell__nav-link--active:visible'))).toMatch(MOTION);
    await page.setViewportSize(PHONE);
    await expect(page.locator('.shell__nav-link:visible')).toHaveCount(4);
    expect(await transitionOf(page.locator('.shell__nav-link:visible').first())).toMatch(MOTION);
    await page.setViewportSize(WIDE);
  });

  await test.step('Krok 10: a Majątek section expands and collapses with the shared 0.14 s', async () => {
    await page.goto('/wealth');
    const toggle = page.locator('.wealth-all__toggle').first();
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    const collapse = page.locator('.wealth-all__collapse-row app-collapse').first();
    expect(await transitionOf(collapse)).toContain('0.14s');
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator('tr.wealth-all__row:visible').first()).toBeVisible();
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator('tr.wealth-all__row:visible')).toHaveCount(0);
  });

  await test.step('Krok 11: at 390 px Plan roczny, the group split and a bond row expand with aria-expanded', async () => {
    await page.setViewportSize(PHONE);
    await page.goto('/planning?tab=year');
    const month = page.getByTestId('year-expand').first();
    await expect(month).toHaveAttribute('aria-expanded', 'false');
    await month.click();
    await expect(month).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByTestId('year-detail').first().locator('dl:visible')).toBeVisible();
    await month.click();
    await expect(month).toHaveAttribute('aria-expanded', 'false');

    await page.goto('/planning');
    const split = page.getByTestId('split-breakdown-toggle').first();
    await expect(split).toHaveAttribute('aria-expanded', 'false');
    await split.click();
    await expect(split).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByTestId('split-breakdown').first()).toBeVisible();
    await split.click();
    await expect(split).toHaveAttribute('aria-expanded', 'false');

    await page.goto('/wealth/bonds');
    const bondRow = page.locator('[data-testid^="expand-"]').first();
    await expect(bondRow).toHaveAttribute('aria-expanded', 'false');
    await bondRow.click();
    await expect(bondRow).toHaveAttribute('aria-expanded', 'true');
    await bondRow.click();
    await expect(bondRow).toHaveAttribute('aria-expanded', 'false');
    await page.setViewportSize(WIDE);
  });

  await test.step('Krok 12: the switch thumb slides, the old chart stays dimmed without a spinner, the first load shows a spinner', async () => {
    await openDashboard(page);
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    await page.route('**/api/reporting/net-worth-history*', async (route) => {
      await gate;
      await route.continue();
    });
    const thumb = page.locator('.net-worth-chart__thumb');
    await thumb.evaluate((element) => {
      const target = window as unknown as Record<string, number>;
      target['__thumbSlides'] = 0;
      element.addEventListener('transitionrun', () => {
        target['__thumbSlides'] = (target['__thumbSlides'] ?? 0) + 1;
      });
    });
    await rangeToggle(page, '1M').click();
    await expect(rangeToggle(page, '1M')).toBeChecked();
    await expect(page.locator('.net-worth-chart__canvas')).toHaveAttribute('aria-busy', 'true');
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await expect(page.locator('.dashboard-page__history mat-spinner')).toHaveCount(0);
    await expect(async () => {
      expect(
        Number(
          await page
            .locator('.net-worth-chart__canvas')
            .evaluate((e) => getComputedStyle(e).opacity),
        ),
      ).toBeLessThan(1);
    }).toPass({ timeout: 5_000 });
    await expect(async () => {
      expect(
        await page.evaluate(() => (window as unknown as Record<string, number>)['__thumbSlides']),
      ).toBeGreaterThan(0);
    }).toPass({ timeout: 5_000 });
    release();
    await expect(page.locator('.net-worth-chart__canvas')).not.toHaveAttribute('aria-busy', 'true');
    await expect(page.locator('.net-worth-chart__layer')).toHaveCount(1);
    await page.unroute('**/api/reporting/net-worth-history*');
    for (const range of ['1Y', 'YTD']) {
      await rangeToggle(page, range).click();
      await expect(rangeToggle(page, range)).toBeChecked();
      await expect(page.locator('.dashboard-page__history mat-spinner')).toHaveCount(0);
    }

    let releaseFirst: () => void = () => undefined;
    const firstGate = new Promise<void>((resolve) => {
      releaseFirst = resolve;
    });
    await page.route('**/api/reporting/net-worth-history*', async (route) => {
      await firstGate;
      await route.continue();
    });
    await page.reload();
    await expect(page.locator('.dashboard-page__history mat-spinner')).toBeVisible();
    await expect(page.locator('.net-worth-chart__layer')).toHaveCount(0);
    releaseFirst();
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await expect(page.locator('.net-worth-chart__layer')).toHaveCount(1);
    expect(await animationOf(page.locator('.net-worth-chart__layer'))).toBe('none');
    await page.unroute('**/api/reporting/net-worth-history*');
  });
});

test('szlif ui: nothing animates under prefers-reduced-motion', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(600_000);
  const token = await loginToken(request, user);
  await page.setViewportSize(NARROW);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const seeded = await seedAccount(request, token, 1000);
  await seedEightGroups(request, token, seeded);

  await test.step('Krok 15: with reduce, the fold, navigation, wealth lists and the range switch change instantly', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(page.getByRole('button', { name: 'Pokaż wszystkie (8)' })).toBeVisible({
          timeout: 2_000,
        });
      },
    );
    const collapse = page.locator('#dashboard-groups-more');
    expect(await transitionOf(collapse)).toMatch(NO_MOTION);
    await page.getByRole('button', { name: 'Pokaż wszystkie (8)' }).click();
    await expect(visibleGroupRows(page)).toHaveCount(8);
    expect(await transitionOf(collapse)).toMatch(NO_MOTION);

    const link = page.locator('.shell__nav-link:visible').first();
    await link.hover();
    expect(await transitionOf(link)).toMatch(NO_MOTION);

    await page.goto('/wealth');
    expect(
      await transitionOf(page.locator('.wealth-all__collapse-row app-collapse').first()),
    ).toMatch(NO_MOTION);

    await seedHistory(page);
    expect(await transitionOf(page.locator('.net-worth-chart__thumb'))).toMatch(NO_MOTION);
    await rangeToggle(page, '1M').click();
    await expect(rangeToggle(page, '1M')).toBeChecked();
    expect(await transitionOf(page.locator('.net-worth-chart__canvas'))).toMatch(NO_MOTION);
    expect(await animationOf(page.locator('.net-worth-chart__layer').first())).toBe('none');
  });
});
