import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
} from './fixtures';
import { assignAccountToGroup } from './groups';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TRANSACTION_WITHDRAW = 3;

const WIDE = { width: 1440, height: 900 };
const NARROW = { width: 1100, height: 900 };
const MEDIUM = { width: 800, height: 900 };
const PHONE = { width: 390, height: 844 };

const ACCOUNT = 'Konto główne';
const GROUP = 'Plan podstawowy';
const EMPTY_GROUP = 'Pusta grupa';

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function heading(page: Page, name: string | RegExp): Locator {
  return page.getByRole('heading', { name, exact: typeof name === 'string' });
}

function change(page: Page): Locator {
  return page.locator('.dashboard-page__change');
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(heading(page, 'Pulpit')).toBeVisible();
}

async function box(
  locator: Locator,
): Promise<{ x: number; y: number; width: number; height: number }> {
  const found = await locator.boundingBox();
  expect(found).not.toBeNull();
  return found as { x: number; y: number; width: number; height: number };
}

async function horizontalOverflow(page: Page): Promise<number> {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
}

async function expectDirection(page: Page): Promise<void> {
  const element = change(page);
  const text = ((await element.innerText()) ?? '').trim();
  const expected = text.startsWith('+')
    ? 'positive'
    : text.startsWith('−')
      ? 'negative'
      : 'neutral';
  await expect(element).toHaveClass(new RegExp(`dashboard-page__change--${expected}`));
}

async function colorOf(locator: Locator): Promise<string> {
  return locator.evaluate((element) => getComputedStyle(element).color);
}

test('pulpit: layout and change colours', async ({ signedInPage: page, request, user }) => {
  test.setTimeout(600_000);
  await page.setViewportSize(WIDE);

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;
  const headers = authorization(token);
  const netWorth = page.getByTestId('net-worth');

  const account = await createAccountThroughApi(request, token, {
    name: ACCOUNT,
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
  } as Parameters<typeof createAccountThroughApi>[2]);
  await depositCashThroughApi(request, token, account.cashAssetId, {
    quantity: 60000,
    date: warsawDate(40),
  });
  const place = await request.post('/api/portfolio/cash-places', {
    headers,
    data: { name: 'Portfel', currencies: ['PLN'] },
  });
  expect(place.ok()).toBeTruthy();
  const walletAssetId = ((await place.json()) as { balances: { cashAssetId: string }[] })
    .balances[0]?.cashAssetId as string;
  await depositCashThroughApi(request, token, walletAssetId, {
    quantity: 500,
    date: warsawDate(),
  });

  await test.step('Krok 1: the hero has the value, the change line and "Struktura majątku" in the left column and the history to the right', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(netWorth).toContainText(/60\s500,00\szł/, { timeout: 2_000 });
      },
    );
    await expect(change(page)).toContainText('zmiana za 1Y');
    await expect(heading(page, 'Struktura majątku')).toBeVisible();
    await expect(heading(page, 'Historia wartości majątku')).toBeVisible();
    await expect(
      page.locator('.dashboard-page__structure app-split-bar [role="img"]'),
    ).toBeVisible();
    const value = await box(netWorth);
    const changeBox = await box(change(page));
    const structure = await box(heading(page, 'Struktura majątku'));
    const history = await box(heading(page, 'Historia wartości majątku'));
    expect(changeBox.y).toBeGreaterThan(value.y);
    expect(structure.y).toBeGreaterThan(changeBox.y);
    expect(Math.abs(structure.x - value.x)).toBeLessThan(40);
    expect(history.x).toBeGreaterThan(structure.x + 100);
  });

  await test.step('Krok 2: the change is green with a plus sign, the shade of --sk-positive', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(change(page)).toContainText(/^\s*\+/, { timeout: 2_000 });
      },
    );
    await expect(change(page)).toHaveClass(/dashboard-page__change--positive/);
    const positive = await page.evaluate(() => {
      const probe = document.createElement('span');
      probe.style.color = 'var(--sk-positive)';
      document.body.appendChild(probe);
      const color = getComputedStyle(probe).color;
      probe.remove();
      return color;
    });
    expect(await colorOf(change(page))).toBe(positive);
  });

  await test.step('Krok 3: every range labels the change and the colour follows its sign', async () => {
    for (const range of ['1M', 'YTD', '1Y']) {
      await rangeToggle(page, range).click();
      await expect(rangeToggle(page, range)).toBeChecked();
      await expect(change(page)).toContainText(new RegExp(`zmiana za ${range}|^\\s*—\\s*$`));
      await expectDirection(page);
    }
  });

  await test.step('Krok 4: in the dark theme the sign stays and the colour is the positive token', async () => {
    const theme = page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
    await theme.click();
    await theme.click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect(change(page)).toContainText(/^\s*\+/);
    await expectDirection(page);
    await theme.click();
    await expect(page.locator('html')).not.toHaveAttribute('data-theme', 'dark');
    await expect(change(page)).toContainText(/^\s*\+/);
  });

  await test.step('Krok 5: after a 10 000,00 zł withdrawal a range with a drop is red with a minus', async () => {
    const withdrawn = await request.post(
      `/api/portfolio/assets/${account.cashAssetId}/transactions`,
      {
        headers,
        data: { type: TRANSACTION_WITHDRAW, quantity: 10000, unitPrice: 1, date: warsawDate() },
      },
    );
    expect(withdrawn.ok()).toBeTruthy();
    await rangeToggle(page, '1M').click();
    await expectProjected(
      async () => {
        await openDashboard(page);
        await rangeToggle(page, '1M').click();
      },
      async () => {
        await expect(netWorth).toContainText(/50\s500,00\szł/, { timeout: 2_000 });
        await expect(change(page)).toContainText(/^\s*−\s?\d/, { timeout: 2_000 });
      },
    );
    await expect(change(page)).toHaveClass(/dashboard-page__change--negative/);
    await expect(change(page)).toContainText(/9\s500,00\szł/);
  });

  const created = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: { name: GROUP, color: 'orange' },
  });
  expect(created.ok()).toBeTruthy();
  const groupId = ((await created.json()) as { id: string }).id;
  await assignAccountToGroup(request, headers, groupId, account.id);
  const emptyGroup = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: { name: EMPTY_GROUP, color: 'purple' },
  });
  expect(emptyGroup.ok()).toBeTruthy();

  await test.step('Krok 6: the bottom row has "Wg grup" on the left and Rachunki above Gotówka in one column on the right', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(page.getByTestId('group-row').filter({ hasText: GROUP })).toBeVisible({
          timeout: 2_000,
        });
      },
    );
    const groups = await box(heading(page, 'Wg grup'));
    const accounts = await box(page.locator('app-accounts-card'));
    const cash = await box(page.locator('app-cash-card'));
    expect(accounts.x).toBeGreaterThan(groups.x + 200);
    expect(Math.abs(cash.x - accounts.x)).toBeLessThan(5);
    expect(cash.y).toBeGreaterThanOrEqual(accounts.y + accounts.height - 1);
    expect(Math.abs(accounts.y - groups.y)).toBeLessThan(60);
  });

  await test.step('Krok 7: the Rachunki and Gotówka column is not disproportionately taller than "Wg grup"', async () => {
    const groupCard = await box(page.locator('.dashboard-page__group-card'));
    const side = await box(page.locator('.dashboard-page__side'));
    expect(side.height).toBeLessThanOrEqual(groupCard.height * 2);
  });

  await test.step('Krok 8: at 1100 px the layout stays two-column and at 800 px nothing scrolls sideways', async () => {
    await page.setViewportSize(NARROW);
    await openDashboard(page);
    const groupCard = await box(page.locator('.dashboard-page__group-card'));
    const side = await box(page.locator('.dashboard-page__side'));
    expect(side.x).toBeGreaterThan(groupCard.x + groupCard.width - 5);
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0);
    await page.setViewportSize(MEDIUM);
    await openDashboard(page);
    await expect(heading(page, 'Struktura majątku')).toBeVisible();
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 9: at 390 px the sections follow the phone order without horizontal scrolling', async () => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    await expect(heading(page, 'Wg grup')).toBeVisible();
    const order = [
      netWorth,
      heading(page, 'Historia wartości majątku'),
      heading(page, 'Struktura majątku'),
      heading(page, 'Do zrobienia'),
      page.locator('app-month-card').getByRole('heading', { name: /· plan$/ }),
      heading(page, 'Wg grup'),
      heading(page, 'Rachunki'),
      heading(page, 'Gotówka'),
    ];
    await expect(async () => {
      const tops: number[] = [];
      for (const section of order) {
        tops.push((await box(section)).y);
      }
      expect(tops).toEqual([...tops].sort((a, b) => a - b));
    }).toPass({ timeout: 10_000 });
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 12: filtering by a group without assets makes the change line grey and the layout intact', async () => {
    await page.setViewportSize(WIDE);
    await openDashboard(page);
    await page.locator('.net-worth-chart__filter').click();
    await page.getByRole('menuitem', { name: EMPTY_GROUP, exact: true }).click();
    await expect(page.locator('.net-worth-chart__filter')).toContainText(EMPTY_GROUP);
    await expect(change(page)).toHaveClass(/dashboard-page__change--neutral/);
    await expect(change(page)).toContainText(/—|0,00\szł/);
    await expect(heading(page, 'Struktura majątku')).toBeVisible();
    await expect(heading(page, 'Wg grup')).toBeVisible();
  });
});
