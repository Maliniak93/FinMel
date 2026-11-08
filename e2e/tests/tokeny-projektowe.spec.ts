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
const MIN_TEXT_PX = 12;
const RAIL_WIDTH = 72;

function at(width: number): { width: number; height: number } {
  return { width, height: 900 };
}

function sidebar(page: Page): Locator {
  return page.locator('mat-sidenav');
}

function heading(page: Page, name: string): Locator {
  return page.getByRole('heading', { name, exact: true });
}

async function openRoute(page: Page, path: string, title: string): Promise<void> {
  await page.goto(path);
  await expect(heading(page, title).first()).toBeVisible();
}

async function px(element: Locator, property: string): Promise<number> {
  await expect(element.first()).toBeVisible();
  return element
    .first()
    .evaluate((node, name) => parseFloat(getComputedStyle(node).getPropertyValue(name)), property);
}

async function textBelowMinimum(page: Page): Promise<string[]> {
  return page.evaluate((minimum) => {
    const offenders: string[] = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const text = (node.textContent ?? '').trim();
      const element = node.parentElement;
      if (!text || !element) {
        continue;
      }
      const style = getComputedStyle(element);
      if (
        style.display === 'none' ||
        style.visibility === 'hidden' ||
        element.getClientRects().length === 0
      ) {
        continue;
      }
      if (['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(element.tagName)) {
        continue;
      }
      const size = parseFloat(style.fontSize);
      if (size < minimum) {
        offenders.push(
          `${element.tagName.toLowerCase()}.${element.className} "${text.slice(0, 30)}" ${size}px`,
        );
      }
    }
    return offenders;
  }, MIN_TEXT_PX);
}

async function expectNoSmallText(page: Page): Promise<void> {
  expect(await textBelowMinimum(page)).toEqual([]);
}

async function overflow(page: Page): Promise<number> {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
}

async function columnCount(element: Locator): Promise<number> {
  return element.evaluate(
    (node) => getComputedStyle(node).gridTemplateColumns.split(' ').filter(Boolean).length,
  );
}

async function expectOnFourGrid(page: Page, selector: string): Promise<void> {
  const offenders = await page.evaluate((scope) => {
    const properties = [
      'paddingTop',
      'paddingRight',
      'paddingBottom',
      'paddingLeft',
      'rowGap',
      'columnGap',
    ] as const;
    const found: string[] = [];
    for (const element of Array.from(document.querySelectorAll(scope))) {
      const style = getComputedStyle(element);
      for (const property of properties) {
        const raw = style[property];
        const value = raw === 'normal' ? 0 : parseFloat(raw);
        if (value > 2 && value % 4 !== 0) {
          found.push(`${element.className} ${property} ${raw}`);
        }
      }
    }
    return found;
  }, selector);
  expect(offenders).toEqual([]);
}

test('tokeny projektowe: typography scale, spacing grid and the three breakpoints', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(300_000);
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const accessToken = ((await login.json()) as { accessToken: string }).accessToken;
  const headers = { Authorization: `Bearer ${accessToken}` };

  const account = await createAccountThroughApi(request, accessToken, {
    name: 'Główne',
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
  } as Parameters<typeof createAccountThroughApi>[2]);
  await depositCashThroughApi(request, accessToken, account.cashAssetId, {
    quantity: 12345.67,
    date: warsawDate(3),
  });
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
  const group = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: { name: 'Wakacje', color: 'blue' },
  });
  expect(group.ok()).toBeTruthy();
  const second = await request.post('/api/portfolio/asset-groups', {
    headers,
    data: { name: 'Remont', color: 'green' },
  });
  expect(second.ok()).toBeTruthy();

  async function openDashboard(): Promise<void> {
    await expectProjected(
      () => openRoute(page, '/dashboard', 'Pulpit'),
      async () => {
        await expect(page.getByTestId('net-worth')).toContainText(/12\s345,67\szł/, {
          timeout: 2_000,
        });
      },
    );
  }

  async function openWealthAll(): Promise<void> {
    await expectProjected(
      () => openRoute(page, '/wealth', 'Majątek'),
      async () => {
        await expect(page.getByTestId('total')).toContainText(/12\s345,67\szł/, {
          timeout: 2_000,
        });
      },
    );
  }

  async function openPlanningYear(): Promise<void> {
    await openRoute(page, '/planning', 'Planowanie');
    await page.getByRole('tab', { name: 'Rok', exact: true }).click();
    await expect(page.getByTestId('year-label')).toBeVisible();
  }

  await test.step('Krok 1: at 1440 px the net worth is 40 px and the card text is on the scale', async () => {
    await page.setViewportSize(at(1440));
    await openDashboard();
    expect(await px(page.getByTestId('net-worth'), 'font-size')).toBe(40);
    expect(await px(page.locator('app-month-card h2'), 'font-size')).toBe(24);
    expect(await px(page.locator('.dashboard-page__kpi-label'), 'font-size')).toBe(14);
    expect(await px(page.locator('.dashboard-page__structure-row').first(), 'font-size')).toBe(14);
  });

  await test.step('Krok 2: no Dashboard text, chart labels included, is under 12 px', async () => {
    await expect(page.locator('.net-worth-chart__svg')).toBeVisible();
    await expect(page.locator('.net-worth-chart__y-label').first()).toBeVisible();
    await expectNoSmallText(page);
  });

  await test.step('Krok 3: Majątek → Wszystko summary strip and heading are at least 12 px', async () => {
    await openWealthAll();
    const total = page.getByTestId('total');
    expect(await px(total, 'font-size')).toBeGreaterThanOrEqual(MIN_TEXT_PX);
    await expect(page.locator('.summary-strip__label, .wealth-all__summary').first()).toBeVisible();
    await expect(heading(page, 'Majątek').first()).toBeVisible();
    await expectNoSmallText(page);
  });

  await test.step('Krok 4: Planowanie → Rok has chart labels of at least 12 px', async () => {
    await openPlanningYear();
    await expect(page.locator('.year-chart__label').first()).toBeVisible();
    await expectNoSmallText(page);
  });

  await test.step('Krok 5: at 1440 px the shell is the full sidebar and its text is at least 12 px', async () => {
    await expect(page.locator('.shell__brand').first()).toBeVisible();
    await expect(page.getByTestId('nav-list').getByRole('link', { name: 'Majątek' })).toBeVisible();
    await expect(page.getByTestId('nav-list').locator('.shell__nav-label').first()).toBeVisible();
    await expectNoSmallText(page);
  });

  await test.step('Krok 6: at 1440 px Dashboard paddings and gaps are multiples of 4 px', async () => {
    await openDashboard();
    await expectOnFourGrid(page, '[class*="dashboard-page"], app-month-card, .month-card');
  });

  await test.step('Krok 7: at 1280 px the sidebar is full', async () => {
    await page.setViewportSize(at(1280));
    await expect(page.locator('.shell__brand').first()).toBeVisible();
    await expect
      .poll(async () => (await sidebar(page).boundingBox())?.width)
      .toBeGreaterThan(RAIL_WIDTH);
  });

  await test.step('Krok 8: at 1279 px the sidebar is a 72 px rail without the brand', async () => {
    await page.setViewportSize(at(1279));
    await expect.poll(async () => (await sidebar(page).boundingBox())?.width).toBe(RAIL_WIDTH);
    await expect(page.locator('.shell__brand')).toHaveCount(0);
    await expect(page.getByTestId('bottom-nav')).toHaveCount(0);
    await expect(
      page.getByTestId('nav-list').locator('.shell__nav-link').first().locator('mat-icon'),
    ).toBeVisible();
  });

  await test.step('Krok 9: at 1279 px Planowanie has no two-column layout with an aside', async () => {
    await openRoute(page, '/planning', 'Planowanie');
    const layout = page.locator('.planning-layout');
    await expect(layout).toBeVisible();
    await expect.poll(() => columnCount(layout)).toBe(1);
  });

  await test.step('Krok 10: at 1280 px Planowanie has two columns with the aside', async () => {
    await page.setViewportSize(at(1280));
    const layout = page.locator('.planning-layout');
    await expect.poll(() => columnCount(layout)).toBe(2);
    await expect(page.locator('.planning-layout__aside')).toBeVisible();
  });

  await test.step('Krok 11: at 600 px the shell is a rail and no bottom bar', async () => {
    await page.setViewportSize(at(600));
    await expect.poll(async () => (await sidebar(page).boundingBox())?.width).toBe(RAIL_WIDTH);
    await expect(page.getByTestId('bottom-nav')).toHaveCount(0);
    const content = await page.locator('.shell__content').boundingBox();
    expect(content!.x + content!.width).toBeGreaterThanOrEqual(599);
  });

  await test.step('Krok 12: at 599 px the phone layout has a bottom bar and no rail', async () => {
    await page.setViewportSize(at(599));
    await expect(page.getByTestId('bottom-nav')).toBeVisible();
    await expect(page.getByTestId('top-bar')).toBeVisible();
    await expect(sidebar(page)).toHaveCount(0);
  });

  await test.step('Krok 13: at 650 px every main screen is tablet-sized and nothing overflows', async () => {
    await page.setViewportSize(at(650));
    for (const [path, title] of [
      ['/wealth', 'Majątek'],
      ['/accounts', 'Konta'],
      ['/planning', 'Planowanie'],
      ['/dashboard', 'Pulpit'],
    ] as const) {
      await page.goto(path);
      await expect(page.getByTestId('bottom-nav')).toHaveCount(0);
      await expect(page.locator('main').getByRole('heading').first()).toBeVisible();
      await expect.poll(() => overflow(page), { message: title }).toBeLessThanOrEqual(0);
      const labels = page.getByTestId('nav-list').locator('.shell__nav-label');
      for (const label of await labels.all()) {
        const fits = await label.evaluate((node) => node.scrollWidth <= node.clientWidth + 1);
        expect(fits).toBe(true);
      }
    }
  });

  await test.step('Krok 14: Grupy drawer is beside the list at 960 px and covers it at 959 px', async () => {
    await page.setViewportSize(at(960));
    await openRoute(page, '/groups', 'Grupy');
    await page.getByTestId('ungrouped-assets').click();
    const drawer = page.getByTestId('assets-panel');
    await expect(drawer).toBeVisible();
    await expect(drawer).toHaveClass(/mat-drawer-side/);
    const container = (await page.locator('.groups-page__container').boundingBox())!;
    const side = (await drawer.boundingBox())!;
    expect(side.width).toBeLessThan(container.width - 100);

    await page.setViewportSize(at(959));
    await expect(drawer).toHaveClass(/mat-drawer-over/);
    await expect(async () => {
      const covering = (await drawer.boundingBox())!;
      const current = (await page.locator('.groups-page__container').boundingBox())!;
      expect(Math.abs(covering.width - current.width)).toBeLessThanOrEqual(1);
    }).toPass();
  });

  await test.step('Krok 15: Grupy list has two columns at 600 px and one at 599 px', async () => {
    await page.setViewportSize(at(600));
    await openRoute(page, '/groups', 'Grupy');
    const list = page.locator('.groups-page__list');
    await expect.poll(() => columnCount(list)).toBe(2);
    await page.setViewportSize(at(599));
    await expect.poll(() => columnCount(list)).toBe(1);
    const manage = page.getByTestId('ungrouped-assets');
    await expect(manage).toBeVisible();
    expect((await manage.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  });

  await test.step('Krok 16: Dashboard hero has two columns at 720 px and one at 719 px', async () => {
    await page.setViewportSize(at(720));
    await openDashboard();
    const hero = page.locator('.dashboard-page__hero');
    await expect.poll(() => columnCount(hero)).toBe(2);
    await page.setViewportSize(at(719));
    await expect.poll(() => columnCount(hero)).toBe(1);
  });

  await test.step('Krok 17: in the dark theme the sizes of steps 1 and 3 stay the same', async () => {
    await page.setViewportSize(at(1440));
    await openDashboard();
    const theme = page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
    await theme.click();
    await theme.click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    expect(await px(page.getByTestId('net-worth'), 'font-size')).toBe(40);
    expect(await px(page.locator('app-month-card h2'), 'font-size')).toBe(24);
    await expectNoSmallText(page);
    await openWealthAll();
    await expectNoSmallText(page);
    await expectOnFourGrid(page, '[class*="wealth-all"], [class*="summary-strip"]');
  });
});
