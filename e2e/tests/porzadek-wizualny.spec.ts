import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  WEALTH_TABS,
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
  wealthTab,
} from './fixtures';
import { expectProjected } from './projection';
import { pln } from './planning';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TYPE_BUY = 0;

const EXTRA_WIDE = { width: 1900, height: 1000 };
const NARROW = { width: 1100, height: 900 };

const DEFAULT_LIMIT = 1200;
const WIDE_LIMIT = 1440;

const ACCOUNT = 'Główne';
const GOLD = 'Złoto 10 g';
const BONUS = 'Premia';
const FUTURE_EXPENSE = 'Przegląd auta';
const LATE_EXPENSE = 'Abonament';

interface ContentMetrics {
  width: number;
  left: number;
  right: number;
  padLeft: number;
  padRight: number;
  mainRight: number;
  overflowY: string;
}

interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

async function box(locator: Locator): Promise<Box> {
  const found = await locator.boundingBox();
  expect(found).not.toBeNull();
  return found as Box;
}

function heading(page: Page, name: string): Locator {
  return page.getByRole('heading', { name, exact: true });
}

async function contentMetrics(page: Page): Promise<ContentMetrics> {
  return page.evaluate(() => {
    const main = document.querySelector('.shell__content') as HTMLElement;
    const shellPage = document.querySelector('.shell__page') as HTMLElement;
    const mainBox = main.getBoundingClientRect();
    const pageBox = shellPage.getBoundingClientRect();
    const style = getComputedStyle(main);
    return {
      width: pageBox.width,
      left: pageBox.left - mainBox.left,
      right: mainBox.left + main.clientWidth - pageBox.right,
      padLeft: parseFloat(style.paddingLeft),
      padRight: parseFloat(style.paddingRight),
      mainRight: mainBox.right,
      overflowY: style.overflowY,
    };
  });
}

async function expectLimit(page: Page, limit: number): Promise<void> {
  if (limit === WIDE_LIMIT) {
    await expect(page.locator('.shell__page')).toHaveAttribute('data-content-width', 'wide');
  } else {
    await expect(page.locator('.shell__page')).not.toHaveAttribute('data-content-width', /.+/);
  }
  const metrics = await contentMetrics(page);
  expect(Math.abs(metrics.width - limit)).toBeLessThanOrEqual(1);
  expect(Math.abs(metrics.left - metrics.right)).toBeLessThanOrEqual(1);
  expect(metrics.left).toBeGreaterThan(metrics.padLeft);
}

async function openDashboard(page: Page): Promise<void> {
  const todo = page.waitForResponse(
    (response) => response.url().includes('/api/portfolio/planning/todo') && response.ok(),
  );
  await page.goto('/dashboard');
  await todo;
  await expect(heading(page, 'Pulpit')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Do rozliczenia' })).toBeVisible();
}

function todoCardBackground(page: Page): Promise<string> {
  return page
    .locator('app-todo-card mat-card')
    .evaluate((element) => getComputedStyle(element).backgroundColor);
}

function cashCardBackground(page: Page): Promise<string> {
  return page
    .locator('app-cash-card mat-card')
    .evaluate((element) => getComputedStyle(element).backgroundColor);
}

async function expectNormalOrder(page: Page): Promise<void> {
  const kpi = await box(page.getByTestId('net-worth'));
  const structure = await box(page.locator('.dashboard-page__structure'));
  const history = await box(page.locator('.dashboard-page__history'));
  const todo = await box(heading(page, 'Do zrobienia'));
  const month = await box(page.locator('app-month-card'));
  expect(structure.y).toBeGreaterThan(kpi.y);
  expect(history.x).toBeGreaterThan(structure.x + 100);
  expect(history.y).toBeLessThan(structure.y + structure.height);
  expect(todo.y).toBeGreaterThan(structure.y + 50);
  expect(month.x).toBeGreaterThan(todo.x + 100);
  expect(await todoCardBackground(page)).toBe(await cashCardBackground(page));
}

async function expectOverdueOrder(page: Page): Promise<void> {
  const kpi = await box(page.getByTestId('net-worth'));
  const structure = await box(page.locator('.dashboard-page__structure'));
  const history = await box(heading(page, 'Historia wartości majątku'));
  const todo = await box(heading(page, 'Do zrobienia'));
  const month = await box(page.locator('app-month-card'));
  expect(todo.y).toBeGreaterThan(kpi.y);
  expect(todo.y).toBeLessThan(structure.y);
  expect(todo.y).toBeLessThan(history.y);
  expect(month.x).toBeGreaterThan(todo.x + 100);
  expect(month.y).toBeLessThan(structure.y);
  expect(history.x).toBeGreaterThan(structure.x + 100);
  expect(await todoCardBackground(page)).not.toBe(await cashCardBackground(page));
}

function parseColor(value: string): [number, number, number] {
  const numbers = (value.match(/-?\d*\.?\d+/g) ?? []).map(Number);
  if (value.startsWith('color(srgb')) {
    return [(numbers[0] ?? 0) * 255, (numbers[1] ?? 0) * 255, (numbers[2] ?? 0) * 255];
  }
  if (value.startsWith('rgb')) {
    return [numbers[0], numbers[1], numbers[2]] as [number, number, number];
  }
  throw new Error(`Unsupported colour: ${value}`);
}

function luminance([r, g, b]: [number, number, number]): number {
  const channel = (c: number): number => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function contrast(foreground: string, background: string): number {
  const a = luminance(parseColor(foreground));
  const b = luminance(parseColor(background));
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

async function colors(locator: Locator, backgroundFrom: Locator): Promise<[string, string]> {
  const foreground = await locator.evaluate((element) => getComputedStyle(element).color);
  const background = await backgroundFrom.evaluate(
    (element) => getComputedStyle(element).backgroundColor,
  );
  return [foreground, background];
}

async function setTheme(page: Page, theme: 'dark' | 'light'): Promise<void> {
  const toggle = page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
  for (let attempt = 0; attempt < 4; attempt++) {
    if ((await page.locator('html').getAttribute('data-theme')) === theme) {
      return;
    }
    await toggle.click();
  }
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
}

async function createBankAccount(
  request: APIRequestContext,
  token: string,
): Promise<{
  id: string;
  cashAssetId: string;
}> {
  const account = await createAccountThroughApi(request, token, {
    name: ACCOUNT,
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
  } as Parameters<typeof createAccountThroughApi>[2]);
  await depositCashThroughApi(request, token, account.cashAssetId, {
    quantity: 40000,
    date: warsawDate(130),
  });
  return account;
}

async function buyGold(
  request: APIRequestContext,
  token: string,
  cashAssetId: string,
): Promise<string> {
  const metal = await request.post('/api/portfolio/metals', {
    headers: bearer(token),
    data: { name: GOLD, metal: 0, fineWeight: 10, weightUnit: 'Gram' },
  });
  expect(metal.ok()).toBeTruthy();
  const assetId = ((await metal.json()) as { assetId: string }).assetId;
  const buy = await request.post(`/api/portfolio/assets/${assetId}/transactions`, {
    headers: bearer(token),
    data: {
      type: TYPE_BUY,
      quantity: 1,
      totalAmount: 3000,
      date: warsawDate(90),
      cashAssetId,
    },
  });
  expect(buy.ok()).toBeTruthy();
  return assetId;
}

async function login(
  request: APIRequestContext,
  user: { email: string; password: string },
): Promise<string> {
  const response = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(response.ok()).toBeTruthy();
  return ((await response.json()) as { accessToken: string }).accessToken;
}

test('porządek wizualny: content width limit of 1200 px and 1440 px', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(300_000);
  await page.setViewportSize(EXTRA_WIDE);
  const token = await login(request, user);
  const account = await createBankAccount(request, token);
  const goldId = await buyGold(request, token, account.cashAssetId);

  await test.step('Krok 1: Pulpit at 1900 px is centred, at most 1200 px wide, with the scrollbar at the window edge', async () => {
    await page.goto('/dashboard');
    await expect(heading(page, 'Pulpit')).toBeVisible();
    await expectLimit(page, DEFAULT_LIMIT);
    const metrics = await contentMetrics(page);
    expect(metrics.overflowY).toBe('auto');
    expect(Math.abs(metrics.mainRight - EXTRA_WIDE.width)).toBeLessThanOrEqual(1);
  });

  await test.step('Krok 2: at 1100 px the content fills the available width with no empty margins', async () => {
    await page.setViewportSize(NARROW);
    await expect(async () => {
      const metrics = await contentMetrics(page);
      expect(metrics.width).toBeLessThan(DEFAULT_LIMIT);
      expect(Math.abs(metrics.left - metrics.padLeft)).toBeLessThanOrEqual(1);
      expect(Math.abs(metrics.right - metrics.padRight)).toBeLessThanOrEqual(1);
    }).toPass({ timeout: 10_000 });
  });

  await test.step('Krok 3: Grupy and Ustawienia share the 1200 px limit at 1900 px', async () => {
    await page.setViewportSize(EXTRA_WIDE);
    await page.goto('/groups');
    await expect(page).toHaveURL(/\/groups$/);
    await expectLimit(page, DEFAULT_LIMIT);
    await page.goto('/settings');
    await expect(page).toHaveURL(/\/settings$/);
    await expectLimit(page, DEFAULT_LIMIT);
  });

  await test.step('Krok 4: Lokaty are centred and at most 1440 px wide, wider than Pulpit', async () => {
    await page.goto('/wealth/deposits');
    await expect(page).toHaveURL(/\/wealth\/deposits$/);
    await expectLimit(page, WIDE_LIMIT);
  });

  await test.step('Krok 5: switching the Majątek tabs does not change the width', async () => {
    for (const name of WEALTH_TABS) {
      await wealthTab(page, name);
      await expect(page.getByRole('tab', { name: new RegExp(`^${name}(\\s|$)`) })).toHaveAttribute(
        'aria-selected',
        'true',
      );
      await expectLimit(page, WIDE_LIMIT);
    }
  });

  await test.step('Krok 6: the transactions page of the gold asset has the 1440 px limit', async () => {
    await page.goto(`/wealth/assets/${goldId}/transactions`);
    await expect(page).toHaveURL(/\/transactions$/);
    await expectLimit(page, WIDE_LIMIT);
  });

  await test.step('Krok 7: Planowanie, annual tab, has 1440 px and its table needs no horizontal scroll', async () => {
    const rentDate = warsawDate(3);
    const rent = await request.post('/api/portfolio/recurring-expenses', {
      headers: bearer(token),
      data: {
        name: 'Czynsz',
        amount: 1450,
        startDate: rentDate,
        endDate: null,
        intervalMonths: 1,
        dayKind: 0,
        dayOfMonth: Math.min(Number(rentDate.slice(8)), 28),
        shift: 0,
        accountId: account.id,
      },
    });
    expect(rent.ok()).toBeTruthy();
    await page.goto('/planning?tab=year');
    await expect(page.getByRole('tab', { name: 'Rok', exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await expectLimit(page, WIDE_LIMIT);
    await expect(page.locator('table').first()).toBeVisible();
    const overflow = await page.evaluate(() =>
      Array.from(document.querySelectorAll('table')).map((table) => {
        let node: HTMLElement | null = table.parentElement;
        let worst =
          table.getBoundingClientRect().right - (node?.getBoundingClientRect().right ?? 0);
        while (node && node !== document.body) {
          worst = Math.max(worst, node.scrollWidth - node.clientWidth);
          node = node.parentElement;
        }
        return worst;
      }),
    );
    for (const value of overflow) {
      expect(value).toBeLessThanOrEqual(1);
    }
  });
});

test('porządek wizualny: Pulpit puts what is overdue under the KPI', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(300_000);
  await page.setViewportSize(EXTRA_WIDE);
  const token = await login(request, user);
  const account = await createBankAccount(request, token);
  const headers = bearer(token);
  let bonusId = '';

  await test.step('Krok 8: without overdue rows the order is KPI, Struktura beside the chart, Do zrobienia beside Ten miesiąc, nothing tinted', async () => {
    await openDashboard(page);
    await expect(page.getByTestId('todo-card-empty')).toBeVisible();
    await expectNormalOrder(page);
  });

  await test.step('Krok 9: an overdue Premia moves Do zrobienia under the KPI and tints Do rozliczenia', async () => {
    const created = await request.post('/api/portfolio/one-off-incomes', {
      headers,
      data: { name: BONUS, amount: 8000, accountId: account.id, date: warsawDate(5) },
    });
    expect(created.ok()).toBeTruthy();
    bonusId = ((await created.json()) as { id: string }).id;
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(page.getByTestId('todo-card-item').filter({ hasText: BONUS })).toBeVisible({
          timeout: 2_000,
        });
        await expectOverdueOrder(page);
      },
    );
    await expect(page.getByTestId('todo-card-item')).toContainText(pln('8 000,00'));
  });

  await test.step('Krok 10: the tinted block is legible in the dark and the light theme', async () => {
    for (const theme of ['dark', 'light'] as const) {
      await setTheme(page, theme);
      const card = page.locator('app-todo-card mat-card');
      const row = page.getByTestId('todo-card-item').filter({ hasText: BONUS });
      const [nameColor, background] = await colors(row.locator('.todo-card__name'), card);
      expect(contrast(nameColor, background)).toBeGreaterThanOrEqual(4.5);
      const link = row.getByRole('link', { name: 'Rozlicz', exact: true });
      await expect(link).toBeVisible();
      const [linkColor, linkBackground] = await colors(link, link);
      expect(contrast(linkColor, linkBackground)).toBeGreaterThanOrEqual(3);
    }
  });

  await test.step('Krok 11: settling Premia puts Do zrobienia back under the chart and drops the tint', async () => {
    const settled = await request.put(`/api/portfolio/one-off-incomes/${bonusId}/settlement`, {
      headers,
      data: {
        actualAmount: 8000,
        actualDate: warsawDate(5),
        accountId: account.id,
        createDeposit: true,
      },
    });
    expect(settled.ok()).toBeTruthy();
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expectNormalOrder(page);
      },
    );
  });

  await test.step('Krok 12: an expense due in 7 days is not overdue and the order stays', async () => {
    const created = await request.post('/api/portfolio/one-off-expenses', {
      headers,
      data: { name: FUTURE_EXPENSE, amount: 300, accountId: account.id, date: warsawDate(-7) },
    });
    expect(created.ok()).toBeTruthy();
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expectNormalOrder(page);
      },
    );
  });

  await test.step('Krok 13: an expense of 120 zł dated 2 days ago moves the section under the KPI again', async () => {
    const created = await request.post('/api/portfolio/one-off-expenses', {
      headers,
      data: { name: LATE_EXPENSE, amount: 120, accountId: account.id, date: warsawDate(2) },
    });
    expect(created.ok()).toBeTruthy();
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(
          page.getByTestId('todo-card-item').filter({ hasText: LATE_EXPENSE }),
        ).toBeVisible({
          timeout: 2_000,
        });
        await expectOverdueOrder(page);
      },
    );
  });

  await test.step('Krok 14: settling the expense restores the layout of Krok 8', async () => {
    const row = page.getByTestId('todo-card-item').filter({ hasText: LATE_EXPENSE });
    await row.getByRole('button', { name: 'Rozlicz', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    await expect(dialog).toBeHidden();
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(
          page.getByTestId('todo-card-item').filter({ hasText: LATE_EXPENSE }),
        ).toHaveCount(0, {
          timeout: 2_000,
        });
        await expectNormalOrder(page);
      },
    );
  });
});

test('porządek wizualny: the net-worth chart is a bare line that can be read from the keyboard', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(300_000);
  await page.setViewportSize(EXTRA_WIDE);
  const token = await login(request, user);
  const account = await createBankAccount(request, token);
  await buyGold(request, token, account.cashAssetId);

  const chart = page.locator('.net-worth-chart__svg');
  const tooltip = page.locator('.net-worth-chart__tooltip');
  const live = page.locator('.net-worth-chart__live');

  async function tooltipText(): Promise<string> {
    const date = await page.locator('.net-worth-chart__tooltip-date').innerText();
    const value = await page.locator('.net-worth-chart__tooltip-value').innerText();
    return `${date.trim()}: ${value.trim()}`;
  }

  async function hoverPlot(fraction: number): Promise<void> {
    const plot = await box(page.locator('.net-worth-chart__plot'));
    const x = plot.x + plot.width * fraction;
    await page.mouse.move(x - 20, plot.y + plot.height / 2);
    await page.mouse.move(x, plot.y + plot.height / 2);
  }

  await test.step('Krok 15: the 1R chart draws only the line, with no fill under it', async () => {
    await expectProjected(
      () => page.goto('/dashboard'),
      async () => {
        await page
          .getByRole('radiogroup', { name: 'Zakres wykresu' })
          .getByRole('radio', { name: '1Y' })
          .click({ timeout: 2_000 });
        await expect(chart).toBeVisible({ timeout: 2_000 });
      },
    );
    await expect(chart.locator('path')).toHaveCount(1);
    await expect(page.locator('.net-worth-chart__line')).toHaveCSS('fill', 'none');
    await expect(page.locator('.net-worth-chart__y-label').first()).toBeVisible();
  });

  await test.step('Krok 16: hovering the chart shows a tooltip with the date and the amount', async () => {
    await hoverPlot(0.6);
    await expect(tooltip).toBeVisible();
    await expect(page.locator('.net-worth-chart__tooltip-date')).not.toBeEmpty();
    await expect(page.locator('.net-worth-chart__tooltip-value')).toContainText(/zł/);
    await page.mouse.move(2, 2);
    await expect(tooltip).toBeHidden();
  });

  let lastText = '';
  let firstText = '';

  await test.step('Krok 17: the chart takes focus and arrows, Home, End and Esc move the point', async () => {
    await chart.focus();
    await expect(chart).toBeFocused();
    await page.keyboard.press('ArrowLeft');
    await expect(tooltip).toBeVisible();
    await expect(chart).toHaveCSS('outline-style', 'solid');
    lastText = await tooltipText();
    await page.keyboard.press('ArrowLeft');
    await expect.poll(tooltipText).not.toBe(lastText);
    await page.keyboard.press('ArrowRight');
    await expect.poll(tooltipText).toBe(lastText);
    await page.keyboard.press('ArrowRight');
    await expect.poll(tooltipText).toBe(lastText);
    await page.keyboard.press('Home');
    await expect.poll(tooltipText).not.toBe(lastText);
    firstText = await tooltipText();
    await page.keyboard.press('ArrowLeft');
    await expect.poll(tooltipText).toBe(firstText);
    await page.keyboard.press('End');
    await expect.poll(tooltipText).toBe(lastText);
    await page.keyboard.press('Escape');
    await expect(tooltip).toBeHidden();
  });

  await test.step('Krok 18: the aria-live region holds the date and amount after an arrow and ignores the mouse', async () => {
    await page.keyboard.press('Home');
    await expect(tooltip).toBeVisible();
    const announced = (await tooltipText()).replace(/\s+/g, ' ');
    await expect
      .poll(async () => ((await live.innerText()) || '').replace(/\s+/g, ' ').trim())
      .toContain(announced);
    await hoverPlot(0.5);
    await expect.poll(tooltipText).not.toBe(firstText);
    await expect
      .poll(async () => ((await live.innerText()) || '').replace(/\s+/g, ' ').trim())
      .toContain(announced);
  });
});
