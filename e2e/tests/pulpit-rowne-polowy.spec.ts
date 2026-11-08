import type { Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
} from './fixtures';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;

const WIDE = { width: 1440, height: 900 };
const EXTRA_WIDE = { width: 1900, height: 1000 };
const PHONE = { width: 390, height: 844 };

const ACCOUNT = 'mBank osobiste';
const EXTRA_GROUPS = ['Wakacje', 'Remont', 'Samochód', 'Dzieci', 'Podatki'];
const COLORS = ['blue', 'green', 'red', 'teal', 'pink'];
const HALF_TOLERANCE = 2;

interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

function heading(page: Page, name: string): Locator {
  return page.getByRole('heading', { name, exact: true });
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(heading(page, 'Pulpit')).toBeVisible();
}

async function box(locator: Locator): Promise<Box> {
  const found = await locator.boundingBox();
  expect(found).not.toBeNull();
  return found as Box;
}

async function horizontalOverflow(page: Page): Promise<number> {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
}

function rows(page: Page): Locator {
  return page.locator('.dashboard-page__grid > *');
}

function halves(page: Page): { left: Locator; right: Locator }[] {
  return [
    {
      left: page.locator('.dashboard-page__structure'),
      right: page.locator('.dashboard-page__history'),
    },
    { left: page.locator('.dashboard-page__todo'), right: page.locator('app-month-card') },
    {
      left: page.locator('.dashboard-page__group-card'),
      right: page.locator('.dashboard-page__side'),
    },
  ];
}

async function expectEqualHalves(page: Page): Promise<number> {
  const dividers: number[] = [];
  for (const { left, right } of halves(page)) {
    const l = await box(left);
    const r = await box(right);
    expect(Math.abs(l.width - r.width)).toBeLessThanOrEqual(HALF_TOLERANCE);
    dividers.push(r.x);
  }
  for (const divider of dividers) {
    expect(Math.abs(divider - (dividers[0] as number))).toBeLessThanOrEqual(HALF_TOLERANCE);
  }
  return dividers[0] as number;
}

async function rowBorder(row: Locator): Promise<{ width: number; color: string; style: string }> {
  return row.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      width: parseFloat(style.borderBottomWidth),
      color: style.borderBottomColor,
      style: style.borderBottomStyle,
    };
  });
}

async function expectTwoSeparators(page: Page): Promise<void> {
  const first = await rowBorder(rows(page).nth(0));
  const second = await rowBorder(rows(page).nth(1));
  const last = await rowBorder(rows(page).nth(2));
  expect(first.width).toBe(1);
  expect(second.width).toBe(1);
  expect(first.style).toBe('solid');
  expect(second.style).toBe('solid');
  expect(second.color).toBe(first.color);
  expect(first.color).not.toBe('rgba(0, 0, 0, 0)');
  expect(last.width).toBe(0);
  await expect(rows(page)).toHaveCount(3);
}

async function canvasFillsHistory(page: Page): Promise<void> {
  const history = await box(page.locator('.dashboard-page__history'));
  const canvas = await box(page.locator('.net-worth-chart__canvas'));
  const svg = await box(page.locator('.net-worth-chart__svg'));
  expect(Math.abs(history.y + history.height - (canvas.y + canvas.height))).toBeLessThanOrEqual(2);
  expect(Math.abs(svg.height - canvas.height)).toBeLessThanOrEqual(1);
  expect(Math.abs(svg.width - canvas.width)).toBeLessThanOrEqual(1);
  const viewBox = await page.locator('.net-worth-chart__svg').getAttribute('viewBox');
  const [, , width, height] = (viewBox ?? '').split(' ').map(Number) as number[];
  expect(Math.abs((width as number) - svg.width)).toBeLessThanOrEqual(2);
  expect(Math.abs((height as number) - svg.height)).toBeLessThanOrEqual(2);
}

async function setDashboardWidth(page: Page, target: number): Promise<void> {
  const container = page.locator('.dashboard-page');
  let viewport = 1000;
  await expect(async () => {
    await page.setViewportSize({ width: viewport, height: 900 });
    await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(resolve)));
    const width = (await box(container)).width;
    const delta = Math.round(target - width);
    if (Math.abs(delta) > 2) {
      viewport += delta;
    }
    expect(Math.abs(delta)).toBeLessThanOrEqual(2);
  }).toPass({ timeout: 15_000 });
}

async function expectHoverFollowsCursor(page: Page, fraction: number): Promise<void> {
  const plot = await box(page.locator('.net-worth-chart__plot'));
  const x = plot.x + plot.width * fraction;
  await page.mouse.move(x - 20, plot.y + plot.height / 2);
  await page.mouse.move(x, plot.y + plot.height / 2);
  await expect(page.locator('.net-worth-chart__tooltip')).toBeVisible();
  await expect(async () => {
    const guideX = await page
      .locator('.net-worth-chart__guide')
      .evaluate((element) => element.getBoundingClientRect().x);
    expect(Math.abs(guideX - x)).toBeLessThanOrEqual(30);
  }).toPass({ timeout: 10_000 });
}

test('pulpit: three rows split evenly with two separator lines', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(600_000);
  await page.setViewportSize(WIDE);

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;
  const headers = { Authorization: `Bearer ${token}` };

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
  await depositCashThroughApi(request, token, walletAssetId, { quantity: 500, date: warsawDate() });

  const cushion = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: { name: 'Poduszka', color: 'orange' },
  });
  expect(cushion.ok()).toBeTruthy();
  const cushionId = ((await cushion.json()) as { id: string }).id;
  const assigned = await request.put('/api/portfolio/asset-groups/assignment', {
    headers,
    data: { assetIds: [account.cashAssetId], groupId: cushionId },
  });
  expect(assigned.ok()).toBeTruthy();
  const pension = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: { name: 'Emerytura', color: 'purple' },
  });
  expect(pension.ok()).toBeTruthy();

  const rentDate = warsawDate(-3);
  const rent = await request.post('/api/portfolio/recurring-expenses', {
    headers,
    data: {
      name: 'Czynsz',
      amount: 1450,
      startDate: rentDate,
      endDate: null,
      intervalMonths: 1,
      dayKind: 0,
      dayOfMonth: Number(rentDate.slice(8)),
      shift: 0,
      accountId: account.id,
    },
  });
  expect(rent.ok()).toBeTruthy();
  const bonus = await request.post('/api/portfolio/one-off-incomes', {
    headers,
    data: { name: 'Premia', amount: 1800, accountId: account.id, date: warsawDate(-5) },
  });
  expect(bonus.ok()).toBeTruthy();

  let groupCardHeight = 0;

  await test.step('Krok 1: three rows of cards, one under another', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(/60\s500,00\szł/, {
          timeout: 2_000,
        });
        await expect(page.getByTestId('group-row').filter({ hasText: 'Poduszka' })).toBeVisible({
          timeout: 2_000,
        });
      },
    );
    await expect(heading(page, 'Struktura majątku')).toBeVisible();
    await expect(heading(page, 'Historia wartości majątku')).toBeVisible();
    await expect(heading(page, 'Do zrobienia')).toBeVisible();
    await expect(heading(page, 'Wg grup')).toBeVisible();
    await expect(heading(page, 'Rachunki')).toBeVisible();
    await expect(heading(page, 'Gotówka')).toBeVisible();
    const value = await box(page.getByTestId('net-worth'));
    const structure = await box(heading(page, 'Struktura majątku'));
    const history = await box(heading(page, 'Historia wartości majątku'));
    const todo = await box(heading(page, 'Do zrobienia'));
    const groups = await box(heading(page, 'Wg grup'));
    const accounts = await box(page.locator('app-accounts-card'));
    const cash = await box(page.locator('app-cash-card'));
    expect(structure.y).toBeGreaterThan(value.y);
    expect(history.x).toBeGreaterThan(structure.x + 100);
    expect(todo.y).toBeGreaterThan(structure.y + 50);
    expect(groups.y).toBeGreaterThan(todo.y + 50);
    expect(accounts.x).toBeGreaterThan(groups.x + 200);
    expect(cash.y).toBeGreaterThanOrEqual(accounts.y + accounts.height - 1);
    groupCardHeight = (await box(page.locator('.dashboard-page__group-card'))).height;
  });

  await test.step('Krok 2: row 1 halves are equally wide', async () => {
    const structure = await box(page.locator('.dashboard-page__structure'));
    const history = await box(page.locator('.dashboard-page__history'));
    expect(Math.abs(structure.width - history.width)).toBeLessThanOrEqual(HALF_TOLERANCE);
  });

  await test.step('Krok 3: all rows split 50/50 on the same vertical divide', async () => {
    await expectEqualHalves(page);
  });

  await test.step('Krok 4: the bottom edges of the halves line up in every row', async () => {
    for (const { left, right } of halves(page)) {
      const l = await box(left);
      const r = await box(right);
      expect(Math.abs(l.y + l.height - (r.y + r.height))).toBeLessThanOrEqual(1);
    }
    const cash = await box(page.locator('app-cash-card'));
    const groupCard = await box(page.locator('.dashboard-page__group-card'));
    expect(Math.abs(cash.y + cash.height - (groupCard.y + groupCard.height))).toBeLessThanOrEqual(
      1,
    );
  });

  await test.step('Krok 5: the history chart fills the height of its card', async () => {
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await canvasFillsHistory(page);
    const fontSize = await page
      .locator('.net-worth-chart__y-label')
      .first()
      .evaluate((element) => getComputedStyle(element).fontSize);
    expect(fontSize).toBe('12px');
    const svg = await box(page.locator('.net-worth-chart__svg'));
    const viewBox = await page.locator('.net-worth-chart__svg').getAttribute('viewBox');
    const [, , width] = (viewBox ?? '').split(' ').map(Number) as number[];
    expect(Math.abs((width as number) - svg.width)).toBeLessThanOrEqual(2);
  });

  await test.step('Krok 6: exactly two full-width separators of one colour and 1 px', async () => {
    await expectTwoSeparators(page);
    const grid = await box(page.locator('.dashboard-page__grid'));
    for (const index of [0, 1]) {
      const row = await box(rows(page).nth(index));
      expect(Math.abs(row.width - grid.width)).toBeLessThanOrEqual(1);
      expect(Math.abs(row.x - grid.x)).toBeLessThanOrEqual(1);
    }
  });

  await test.step('Krok 7: equal space above and below each separator', async () => {
    for (const index of [0, 1]) {
      const row = await box(rows(page).nth(index));
      const next = await box(rows(page).nth(index + 1));
      const below = next.y - (row.y + row.height);
      const contents = await rows(page)
        .nth(index)
        .evaluate((element) =>
          Math.max(
            ...Array.from(element.children).map((child) => child.getBoundingClientRect().bottom),
          ),
        );
      const above = row.y + row.height - 1 - contents;
      expect(Math.abs(above - below)).toBeLessThanOrEqual(2);
    }
  });

  await test.step('Krok 8: no line under the last row or under Do zrobienia, Plan and Grupy', async () => {
    const last = await rowBorder(rows(page).nth(2));
    expect(last.width).toBe(0);
    const bottoms = await page.evaluate(() => {
      const widths = (selector: string): number[] =>
        Array.from(document.querySelectorAll(selector)).map((element) =>
          parseFloat(getComputedStyle(element).borderBottomWidth),
        );
      const lastOf = (selector: string): number => {
        const items = widths(selector);
        return items[items.length - 1] ?? 0;
      };
      return {
        groupCard: widths('.dashboard-page__group-card')[0],
        todo: widths('.dashboard-page__todo')[0],
        month: widths('app-month-card')[0],
        side: widths('.dashboard-page__side')[0],
        lastGroupRow: lastOf('.dashboard-page__group-row'),
        lastStructureRow: lastOf('.dashboard-page__structure-row'),
      };
    });
    expect(bottoms).toEqual({
      groupCard: 0,
      todo: 0,
      month: 0,
      side: 0,
      lastGroupRow: 0,
      lastStructureRow: 0,
    });
  });

  await test.step('Krok 9: five more groups stretch the right half of row 3', async () => {
    for (const [index, name] of EXTRA_GROUPS.entries()) {
      const created = await request.post('/api/portfolio/asset-groups', {
        headers,
        data: { name, color: COLORS[index] },
      });
      expect(created.ok()).toBeTruthy();
    }
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(page.getByTestId('group-row').filter({ hasText: 'Podatki' })).toBeVisible({
          timeout: 2_000,
        });
      },
    );
    const groupCard = await box(page.locator('.dashboard-page__group-card'));
    const side = await box(page.locator('.dashboard-page__side'));
    const cash = await box(page.locator('app-cash-card'));
    expect(groupCard.height).toBeGreaterThan(groupCardHeight);
    expect(Math.abs(side.y + side.height - (groupCard.y + groupCard.height))).toBeLessThanOrEqual(
      1,
    );
    expect(Math.abs(cash.y + cash.height - (groupCard.y + groupCard.height))).toBeLessThanOrEqual(
      1,
    );
    const lastRow = await page
      .getByTestId('group-row')
      .last()
      .evaluate((element) => parseFloat(getComputedStyle(element).borderBottomWidth));
    expect(lastRow).toBe(0);
    expect((await rowBorder(rows(page).nth(2))).width).toBe(0);
    await expectEqualHalves(page);
  });

  await test.step('Krok 10: at 1900 px row 1 stays 50/50 and equally tall', async () => {
    await page.setViewportSize(EXTRA_WIDE);
    await expect(async () => {
      const structure = await box(page.locator('.dashboard-page__structure'));
      const history = await box(page.locator('.dashboard-page__history'));
      expect(Math.abs(structure.width - history.width)).toBeLessThanOrEqual(HALF_TOLERANCE);
      expect(
        Math.abs(structure.y + structure.height - (history.y + history.height)),
      ).toBeLessThanOrEqual(1);
    }).toPass({ timeout: 10_000 });
    const hero = await box(rows(page).nth(0));
    const history = await box(page.locator('.dashboard-page__history'));
    expect(history.y + history.height).toBeLessThanOrEqual(hero.y + hero.height);
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await canvasFillsHistory(page);
    await page.setViewportSize(WIDE);
  });

  await test.step('Krok 11: switching the range does not move the layout', async () => {
    await expect(async () => {
      const history = await box(page.locator('.dashboard-page__history'));
      expect(history.width).toBeGreaterThan(500);
    }).toPass({ timeout: 10_000 });
    const before = {
      history: await box(page.locator('.dashboard-page__history')),
      hero: await box(rows(page).nth(0)),
    };
    for (const range of ['1M', '1Y', 'MAX']) {
      await rangeToggle(page, range).click();
      await expect(rangeToggle(page, range)).toBeChecked();
      await expect(page.locator('.net-worth-chart__layer')).toHaveCount(1);
      await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
      await expect(async () => {
        const history = await box(page.locator('.dashboard-page__history'));
        const hero = await box(rows(page).nth(0));
        expect(Math.abs(history.height - before.history.height)).toBeLessThanOrEqual(1);
        expect(Math.abs(hero.height - before.hero.height)).toBeLessThanOrEqual(1);
      }).toPass({ timeout: 10_000 });
      await canvasFillsHistory(page);
    }
    await rangeToggle(page, '1Y').click();
    await expect(rangeToggle(page, '1Y')).toBeChecked();
    await expect(page.locator('.net-worth-chart__layer')).toHaveCount(1);
  });

  await test.step('Krok 12: the tooltip follows the cursor, also after a resize', async () => {
    await expectHoverFollowsCursor(page, 0.6);
    await page.setViewportSize({ width: 1200, height: 900 });
    await expect(async () => {
      await canvasFillsHistory(page);
    }).toPass({ timeout: 10_000 });
    await expectHoverFollowsCursor(page, 0.4);
    await page.setViewportSize(WIDE);
  });

  await test.step('Krok 13: just above 720 px the halves stay 50/50 and no amount is clipped', async () => {
    await setDashboardWidth(page, 735);
    await expectEqualHalves(page);
    await expect(async () => {
      const clipped = await page.evaluate(
        () =>
          Array.from(
            document.querySelectorAll(
              '.dashboard-page__group-value, .dashboard-page__group-share, .dashboard-page__structure-value, .dashboard-page__structure-share',
            ),
          ).filter((element) => {
            const style = getComputedStyle(element);
            return (
              element.scrollWidth > element.clientWidth + 1 ||
              style.textOverflow === 'ellipsis' ||
              element.getBoundingClientRect().right >
                (element.closest('li')?.getBoundingClientRect().right ?? Infinity) + 1
            );
          }).length,
      );
      expect(clipped).toBe(0);
    }).toPass({ timeout: 10_000 });
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 14: below the threshold one column stays with the separators', async () => {
    await setDashboardWidth(page, 570);
    await expect(async () => {
      const structure = await box(page.locator('.dashboard-page__structure'));
      const history = await box(page.locator('.dashboard-page__history'));
      expect(Math.abs(structure.x - history.x)).toBeLessThanOrEqual(1);
      expect(structure.y).toBeGreaterThan(history.y);
      const todo = await box(page.locator('.dashboard-page__todo'));
      const month = await box(page.locator('app-month-card'));
      expect(Math.abs(todo.x - month.x)).toBeLessThanOrEqual(1);
      expect(month.y).toBeGreaterThan(todo.y);
      const groups = await box(page.locator('.dashboard-page__group-card'));
      const side = await box(page.locator('.dashboard-page__side'));
      expect(Math.abs(groups.x - side.x)).toBeLessThanOrEqual(1);
      expect(side.y).toBeGreaterThan(groups.y);
    }).toPass({ timeout: 10_000 });
    await expectTwoSeparators(page);
  });

  await test.step('Krok 15: on a phone one column in order, no sideways scroll, chart keeps its ratio', async () => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    const order = [
      page.getByTestId('net-worth'),
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
    await expect(page.locator('.net-worth-chart__canvas')).toBeVisible();
    const canvas = await box(page.locator('.net-worth-chart__canvas'));
    expect(Math.abs(canvas.width / canvas.height - 640 / 280)).toBeLessThan(0.1);
  });

  await test.step('Krok 16: in the dark theme both separators look the same', async () => {
    await page.setViewportSize(WIDE);
    await openDashboard(page);
    const theme = page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
    await theme.click();
    await theme.click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expectTwoSeparators(page);
    const first = await rowBorder(rows(page).nth(0));
    expect(first.color).not.toBe('rgba(0, 0, 0, 0)');
  });

  await test.step('Krok 17: while the history loads the card has the half height and a centred spinner', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    await page.route('**/api/reporting/net-worth-history*', async (route) => {
      await gate;
      await route.continue();
    });
    await page.reload();
    const spinner = page.locator('.dashboard-page__history mat-spinner');
    await expect(spinner).toBeVisible();
    const structure = await box(page.locator('.dashboard-page__structure'));
    const history = await box(page.locator('.dashboard-page__history'));
    const dot = await box(spinner);
    expect(Math.abs(structure.width - history.width)).toBeLessThanOrEqual(HALF_TOLERANCE);
    expect(
      Math.abs(structure.y + structure.height - (history.y + history.height)),
    ).toBeLessThanOrEqual(1);
    expect(Math.abs(dot.x + dot.width / 2 - (history.x + history.width / 2))).toBeLessThanOrEqual(
      40,
    );
    const centreY = dot.y + dot.height / 2;
    expect(centreY).toBeGreaterThan(history.y + history.height * 0.3);
    expect(centreY).toBeLessThan(history.y + history.height * 0.9);
    release();
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await page.unroute('**/api/reporting/net-worth-history*');
  });

  await test.step('Krok 18: a blocked history request shows a centred error with Ponów, then recovers', async () => {
    await page.route('**/api/reporting/net-worth-history*', (route) => route.abort());
    await page.reload();
    const retry = page.locator('.dashboard-page__history').getByRole('button', { name: 'Ponów' });
    await expect(retry).toBeVisible();
    await expect(heading(page, 'Struktura majątku')).toBeVisible();
    await expect(heading(page, 'Wg grup')).toBeVisible();
    const history = await box(page.locator('.dashboard-page__history'));
    const button = await box(page.locator('.dashboard-page__history app-section-error'));
    expect(
      Math.abs(button.x + button.width / 2 - (history.x + history.width / 2)),
    ).toBeLessThanOrEqual(40);
    await page.unroute('**/api/reporting/net-worth-history*');
    await retry.click();
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await expect(retry).toHaveCount(0);
  });
});
