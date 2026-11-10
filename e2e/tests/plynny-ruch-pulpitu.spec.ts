import type { APIRequestContext, Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expect,
  test,
  warsawDate,
} from './fixtures';
import { expensePayments, openMonth } from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const VARIANT_CURRENT = 0;
const TRANSACTION_WITHDRAW = 3;

const WIDE = { width: 1440, height: 900 };

const DAYS_14_MONTHS = 426;
const DAYS_5_MONTHS = 152;
const DAYS_3_WEEKS = 21;
const DAYS_5 = 5;
const DAYS_10 = 10;

const TOLERANCE = 1;
const TRANSPARENT = /^(rgba\(0, 0, 0, 0\)|transparent)$/;

interface Sample {
  amount: string | null;
  percent: string | null;
  left: number | null;
  width: number | null;
  topLeft: number | null;
  topRight: number | null;
  swapOut: number;
  fills: number;
}

interface Tracked {
  headers: { Authorization: string };
  accountId: string;
  cashAssetId: string;
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function rangeToggle(page: Page, name: string): Locator {
  return page.getByRole('radiogroup', { name: 'Zakres wykresu' }).getByRole('radio', { name });
}

function allToggles(page: Page): Locator {
  return page.locator('.net-worth-chart__ranges mat-button-toggle');
}

function thumb(page: Page): Locator {
  return page.locator('.net-worth-chart__thumb');
}

function amount(page: Page): Locator {
  return page.getByTestId('change-amount');
}

function percent(page: Page): Locator {
  return page.getByTestId('change-percent');
}

function caption(page: Page): Locator {
  return page.locator('.dashboard-page__change-range:not(.dashboard-page__swap-out)');
}

function pair(page: Page): Locator {
  return page.locator('.dashboard-page__todo-pair');
}

function pairInHero(page: Page): Locator {
  return page.locator('.dashboard-page__hero > .dashboard-page__todo-pair');
}

function todoItem(page: Page, name: string): Locator {
  return page.locator('app-todo-card').getByTestId('todo-card-item').filter({ hasText: name });
}

async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { name: 'Pulpit', exact: true })).toBeVisible();
}

function installSampler(): void {
  const target = window as unknown as {
    __samples: unknown[];
    __animated: string[];
  };
  target.__samples = [];
  target.__animated = [];
  const original = HTMLElement.prototype.animate;
  HTMLElement.prototype.animate = function (
    this: HTMLElement,
    ...args: Parameters<Element['animate']>
  ) {
    target.__animated.push(this.className);
    return original.apply(this, args);
  };
  const radius = (value: string): number => Number.parseFloat(value) || 0;
  const tick = (): void => {
    const element = document.querySelector<HTMLElement>('.net-worth-chart__thumb');
    const rect = element?.getBoundingClientRect() ?? null;
    const style = element ? getComputedStyle(element) : null;
    const fills = [
      ...document.querySelectorAll<HTMLElement>(
        '.net-worth-chart__ranges mat-button-toggle, .net-worth-chart__ranges .mat-button-toggle-button',
      ),
    ].filter((toggle) => {
      const color = getComputedStyle(toggle).backgroundColor;
      return color !== 'rgba(0, 0, 0, 0)' && color !== 'transparent';
    }).length;
    target.__samples.push({
      amount: document.querySelector('[data-testid="change-amount"]')?.textContent?.trim() ?? null,
      percent:
        document.querySelector('[data-testid="change-percent"]')?.textContent?.trim() ?? null,
      left: rect ? rect.left : null,
      width: rect ? rect.width : null,
      topLeft: style ? radius(style.borderTopLeftRadius) : null,
      topRight: style ? radius(style.borderTopRightRadius) : null,
      swapOut: document.querySelectorAll('.dashboard-page__swap-out').length,
      fills,
    });
    requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}

async function samples(page: Page): Promise<Sample[]> {
  return page.evaluate(() => (window as unknown as { __samples: Sample[] }).__samples);
}

async function resetSamples(page: Page): Promise<void> {
  await page.evaluate(() => {
    const target = window as unknown as { __samples: unknown[]; __animated: string[] };
    target.__samples.length = 0;
    target.__animated.length = 0;
  });
}

async function pairAnimations(page: Page): Promise<number> {
  return page.evaluate(
    () =>
      (window as unknown as { __animated: string[] }).__animated.filter((name) =>
        name.includes('dashboard-page__todo-pair'),
      ).length,
  );
}

function numberOf(text: string): number {
  return Number(
    text
      .replace('−', '-')
      .replace(/[^\d,+-]/g, '')
      .replace(',', '.'),
  );
}

function amounts(list: Sample[]): number[] {
  return list.flatMap((sample) => (sample.amount === null ? [] : [numberOf(sample.amount)]));
}

function distinct(values: (string | null)[]): string[] {
  return [...new Set(values.filter((value): value is string => value !== null))];
}

async function box(locator: Locator): Promise<{ x: number; y: number; width: number }> {
  const found = await locator.boundingBox();
  expect(found).not.toBeNull();
  return found as { x: number; y: number; width: number };
}

async function toggleWidths(page: Page): Promise<number[]> {
  return allToggles(page).evaluateAll((elements) =>
    elements.map((element) => Math.round(element.getBoundingClientRect().width * 10) / 10),
  );
}

async function expectThumbOn(page: Page, name: string): Promise<void> {
  await expect(rangeToggle(page, name)).toBeChecked();
  await expect(async () => {
    const target = await box(allToggles(page).filter({ hasText: name }));
    const found = await box(thumb(page));
    expect(Math.abs(found.x - target.x)).toBeLessThan(TOLERANCE);
    expect(Math.abs(found.width - target.width)).toBeLessThan(TOLERANCE);
  }).toPass({ timeout: 5_000 });
}

async function expectCorners(
  page: Page,
  corners: { left: boolean; right: boolean },
): Promise<void> {
  await expect(thumb(page)).toHaveCSS(
    'border-top-left-radius',
    corners.left ? /^[1-9]\d*(\.\d+)?px$/ : '0px',
  );
  await expect(thumb(page)).toHaveCSS(
    'border-bottom-left-radius',
    corners.left ? /^[1-9]\d*(\.\d+)?px$/ : '0px',
  );
  await expect(thumb(page)).toHaveCSS(
    'border-top-right-radius',
    corners.right ? /^[1-9]\d*(\.\d+)?px$/ : '0px',
  );
  await expect(thumb(page)).toHaveCSS(
    'border-bottom-right-radius',
    corners.right ? /^[1-9]\d*(\.\d+)?px$/ : '0px',
  );
}

async function expectNoCheckMark(page: Page): Promise<void> {
  await expect(page.locator('.net-worth-chart__ranges mat-pseudo-checkbox')).toHaveCount(0);
  await expect(
    page.locator('.net-worth-chart__ranges .mat-button-toggle-checkbox-wrapper'),
  ).toHaveCount(0);
}

function expectMonotonic(list: Sample[], direction: 'left' | 'right'): void {
  const lefts = list.flatMap((sample) => (sample.left === null ? [] : [sample.left]));
  expect(lefts.length).toBeGreaterThan(1);
  for (let index = 1; index < lefts.length; index++) {
    const step = (lefts[index] as number) - (lefts[index - 1] as number);
    expect(direction === 'right' ? step : -step).toBeGreaterThan(-TOLERANCE);
  }
}

async function seed(
  request: APIRequestContext,
  user: { email: string; password: string },
): Promise<Tracked> {
  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;
  const headers = authorization(token);
  const account = await createAccountThroughApi(request, token, {
    name: 'Gotówka',
    type: ACCOUNT_TYPE_BANK,
    wrapper: WRAPPER_REGULAR,
    currency: 'PLN',
    variant: VARIANT_CURRENT,
    annualInterestRatePercent: 0,
  } as Parameters<typeof createAccountThroughApi>[2]);
  for (const [quantity, days] of [
    [10000, DAYS_14_MONTHS],
    [4000, DAYS_5_MONTHS],
    [1250, DAYS_3_WEEKS],
  ] as const) {
    await depositCashThroughApi(request, token, account.cashAssetId, {
      quantity,
      date: warsawDate(days),
    });
  }
  await withdraw(request, headers, account.cashAssetId, 300, DAYS_5);
  return { headers, accountId: account.id, cashAssetId: account.cashAssetId };
}

async function withdraw(
  request: APIRequestContext,
  headers: Tracked['headers'],
  cashAssetId: string,
  quantity: number,
  daysAgo: number,
): Promise<void> {
  const response = await request.post(`/api/portfolio/assets/${cashAssetId}/transactions`, {
    headers,
    data: {
      type: TRANSACTION_WITHDRAW,
      quantity,
      unitPrice: 1,
      date: warsawDate(daysAgo),
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}

async function addOverdueExpense(
  request: APIRequestContext,
  tracked: Tracked,
  name: string,
): Promise<void> {
  const created = await request.post('/api/portfolio/one-off-expenses', {
    headers: tracked.headers,
    data: { name, amount: 50, accountId: tracked.accountId, date: warsawDate(1) },
  });
  expect(created.ok(), await created.text()).toBeTruthy();
}

async function settleFromCard(page: Page, name: string): Promise<void> {
  await todoItem(page, name).getByRole('button', { name: 'Rozlicz', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('spinbutton', { name: 'Faktyczna kwota' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

function dotted(iso: string): string {
  const [year, month, day] = iso.split('-');
  return `${day}.${month}.${year}`;
}

async function prepare(
  page: Page,
  request: APIRequestContext,
  user: { email: string; password: string },
): Promise<Tracked> {
  await page.addInitScript(installSampler);
  await page.setViewportSize(WIDE);
  return seed(request, user);
}

test('plynny ruch pulpitu: range switch, figures and layout move smoothly', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(900_000);
  const tracked = await prepare(page, request, user);
  await addOverdueExpense(request, tracked, 'Zaległy 1');

  await test.step('Krok 1: 1Y is selected, the change shows +4 950,00 zł and +49,5 % with "zmiana za 1Y", the todo cards stand in the top grid', async () => {
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(amount(page)).toContainText(/\+4\s950,00\szł/, { timeout: 2_000 });
      },
    );
    await expect(rangeToggle(page, '1Y')).toBeChecked();
    await expect(percent(page)).toContainText(/\+49,5/);
    await expect(caption(page)).toContainText('zmiana za 1Y');
    await expect(pairInHero(page)).toHaveCount(1);
    await expect(page.getByRole('heading', { name: 'Do zrobienia' })).toBeVisible();
    await expect(page.getByRole('heading', { name: /^Nadchodzące/ })).toBeVisible();
    const hero = await box(page.getByTestId('net-worth'));
    const cards = await box(pair(page));
    const structure = await box(page.getByRole('heading', { name: 'Struktura majątku' }));
    expect(cards.y).toBeGreaterThan(hero.y);
    expect(cards.y).toBeLessThan(structure.y);
  });

  const widthsBefore = await toggleWidths(page);

  await test.step('Krok 2: 1M moves the selection from right to left with the left corners appearing in the same motion, the buttons keep their widths', async () => {
    await expectThumbOn(page, '1Y');
    await resetSamples(page);
    await rangeToggle(page, '1M').click();
    await expect(amount(page)).toContainText(/\+950,00\szł/);
    await expectThumbOn(page, '1M');
    const list = await samples(page);
    const lefts = list.flatMap((sample) => (sample.left === null ? [] : [sample.left]));
    expect(Math.max(...lefts) - Math.min(...lefts)).toBeGreaterThan(20);
    expectMonotonic(list, 'left');
    const finalCorner = Math.max(...list.map((sample) => sample.topLeft ?? 0));
    expect(finalCorner).toBeGreaterThan(0);
    expect(
      list.some(
        (sample) => (sample.topLeft ?? 0) > 0.5 && (sample.topLeft ?? 0) < finalCorner - 0.5,
      ),
    ).toBeTruthy();
    expect(Math.max(...list.map((sample) => sample.fills))).toBe(0);
    expect(await toggleWidths(page)).toEqual(widthsBefore);
  });

  await test.step('Krok 3: the figures run from +4 950,00 zł to +950,00 zł through intermediate values while the caption crossfades into "zmiana za 1M"', async () => {
    const list = await samples(page);
    const values = amounts(list);
    expect(values.some((value) => value > 950 && value < 4950)).toBeTruthy();
    expect(values.at(-1)).toBe(950);
    expect(list.some((sample) => sample.swapOut > 0)).toBeTruthy();
    await expect(percent(page)).toContainText(/\+6,79/);
    await expect(caption(page)).toContainText('zmiana za 1M');
    await expect(page.locator('.dashboard-page__swap-out')).toHaveCount(0);
  });

  await test.step('Krok 4: 1Y moves the selection from left to right and the figures return to +4 950,00 zł', async () => {
    await resetSamples(page);
    await rangeToggle(page, '1Y').click();
    await expect(amount(page)).toContainText(/\+4\s950,00\szł/);
    await expectThumbOn(page, '1Y');
    const list = await samples(page);
    expectMonotonic(list, 'right');
    expect(Math.max(...list.map((sample) => sample.fills))).toBe(0);
    expect(amounts(list).some((value) => value > 950 && value < 4950)).toBeTruthy();
    await expect(percent(page)).toContainText(/\+49,5/);
  });

  await test.step('Krok 5: 1M has only the left corners, 1Y, YTD and MAX have none, no option shows a check mark', async () => {
    await rangeToggle(page, '1M').click();
    await expectThumbOn(page, '1M');
    await expectCorners(page, { left: true, right: false });
    await expectNoCheckMark(page);
    for (const name of ['1Y', 'YTD', 'MAX']) {
      await rangeToggle(page, name).click();
      await expectThumbOn(page, name);
      await expectCorners(page, { left: false, right: false });
      await expectNoCheckMark(page);
    }
  });

  await test.step('Krok 6: Zakres of the last 3 months has only the right corners, the caption shows the date range, the figures keep their two decimals', async () => {
    await rangeToggle(page, 'MAX').click();
    await expectThumbOn(page, 'MAX');
    await resetSamples(page);
    await rangeToggle(page, 'Zakres').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: 'Zakres wykresu' })).toBeVisible();
    await dialog.getByLabel('Od', { exact: true }).fill(dotted(warsawDate(90)));
    await dialog.getByLabel('Do', { exact: true }).fill(dotted(warsawDate()));
    await dialog.getByRole('button', { name: 'Zastosuj' }).click();
    await expect(dialog).toBeHidden();
    await expect(rangeToggle(page, 'Zakres')).toBeChecked();
    await expectThumbOn(page, 'Zakres');
    await expectCorners(page, { left: false, right: true });
    await expectNoCheckMark(page);
    await expect(caption(page)).toContainText(/\d{2}\.\d{2}\.\d{4}\s?[–-]\s?\d{2}\.\d{2}\.\d{4}/);
    await expect(page.locator('.dashboard-page__swap-out')).toHaveCount(0);
    expect((await samples(page)).some((sample) => sample.swapOut > 0)).toBeTruthy();
    await expect(amount(page)).toHaveText(/^[+−-]?\d{1,3}(\s\d{3})*,\d{2}\szł$/);
    await expect(percent(page)).toHaveText(/^\([+−-]?\d+,\d{2}\s?%\)$/);
  });

  await test.step('Krok 7: four quick clicks 1M, 1Y, YTD, 1M queue nothing and end on 1M with +950,00 zł', async () => {
    await resetSamples(page);
    for (const name of ['1M', '1Y', 'YTD', '1M']) {
      await rangeToggle(page, name).click();
    }
    await expectThumbOn(page, '1M');
    await expect(amount(page)).toContainText(/\+950,00\szł/);
    await expect(percent(page)).toContainText(/\+6,79/);
    const list = await samples(page);
    const zakres = await box(allToggles(page).filter({ hasText: 'Zakres' }));
    const first = await box(allToggles(page).first());
    for (const sample of list) {
      if (sample.left !== null) {
        expect(sample.left).toBeGreaterThan(first.x - TOLERANCE);
        expect(sample.left).toBeLessThan(zakres.x + TOLERANCE);
      }
    }
    expect(Math.max(...list.map((sample) => sample.fills))).toBe(0);
    await expect(page.locator('.dashboard-page__swap-out')).toHaveCount(0);
  });

  await test.step('Krok 8: after a withdrawal of 3 000 zł 10 days ago the figure runs through zero to −2 050,00 zł and −14,6 %', async () => {
    await withdraw(request, tracked.headers, tracked.cashAssetId, 3000, DAYS_10);
    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(amount(page)).toContainText(/\+1\s950,00\szł/, { timeout: 2_000 });
      },
    );
    await resetSamples(page);
    await rangeToggle(page, '1M').click();
    await expect(amount(page)).toContainText(/[−-]2\s050,00\szł/);
    await expect(percent(page)).toContainText(/[−-]14,6/);
    const values = amounts(await samples(page));
    expect(values.some((value) => value > 0 && value < 1950)).toBeTruthy();
    expect(values.some((value) => value < 0 && value > -2050)).toBeTruthy();
  });

  await test.step('Krok 9: after F5 and after Ustawienia and back the figures appear at once, without counting', async () => {
    await page.reload();
    await expect(amount(page)).toContainText(/\+1\s950,00\szł/);
    await page
      .getByRole('link', { name: /^Ustawienia/ })
      .first()
      .click();
    await expect(page).toHaveURL(/\/settings/);
    await page
      .getByRole('link', { name: /^Pulpit/ })
      .first()
      .click();
    await expect(amount(page)).toContainText(/\+1\s950,00\szł/);
    await expect(rangeToggle(page, '1Y')).toBeChecked();
    await expect(page.locator('.dashboard-page__swap-out')).toHaveCount(0);
    const list = await samples(page);
    expect(distinct(list.map((sample) => sample.amount))).toHaveLength(1);
    expect(distinct(list.map((sample) => sample.percent))).toHaveLength(1);
  });

  await test.step('Krok 10: settling the 50 zł expense moves the cards under the grid as the same element, animated, without a flash of the dashboard', async () => {
    await expect(pairInHero(page)).toHaveCount(1);
    await page.evaluate(() => {
      const target = window as unknown as Record<string, unknown>;
      target['__pair'] = document.querySelector('.dashboard-page__todo-pair');
      target['__hero'] = document.querySelector('.dashboard-page__hero');
      target['__netWorth'] = document.querySelector('[data-testid="net-worth"]');
    });
    await resetSamples(page);
    await settleFromCard(page, 'Zaległy 1');
    await expect(pairInHero(page)).toHaveCount(0);
    await expect(pair(page)).toHaveCount(1);
    await expect.poll(() => pairAnimations(page)).toBeGreaterThan(0);
    const identity = await page.evaluate(() => {
      const target = window as unknown as Record<string, unknown>;
      return {
        pair: target['__pair'] === document.querySelector('.dashboard-page__todo-pair'),
        hero: target['__hero'] === document.querySelector('.dashboard-page__hero'),
        netWorth: target['__netWorth'] === document.querySelector('[data-testid="net-worth"]'),
      };
    });
    expect(identity).toEqual({ pair: true, hero: true, netWorth: true });
    await expect(async () => {
      expect(await pair(page).evaluate((element) => element.getAnimations().length)).toBe(0);
    }).toPass({ timeout: 5_000 });
    await expect(pair(page)).toHaveCSS('transform', 'none');
  });

  await test.step('Krok 11: after settling, 1Y and 1M work as before and the figures of the same range appear at once', async () => {
    const list = await samples(page);
    expect(distinct(list.map((sample) => sample.amount)).length).toBeLessThanOrEqual(2);
    await rangeToggle(page, '1Y').click();
    await expectThumbOn(page, '1Y');
    await expect(amount(page)).toContainText(/\+1\s9[05]0,00\szł/);
    await rangeToggle(page, '1M').click();
    await expectThumbOn(page, '1M');
    await expect(amount(page)).toContainText(/[−-]2\s(050|100),00\szł/);
  });

  await test.step('Krok 12: another overdue 50 zł expense puts the cards back in the top grid (the motion itself tylko manualnie)', async () => {
    await addOverdueExpense(request, tracked, 'Zaległy 2');
    await openDashboard(page);
    await expect(todoItem(page, 'Zaległy 2')).toBeVisible();
    await expect(pairInHero(page)).toHaveCount(1);
  });

  await test.step('Krok 13: with two overdue expenses settling one leaves the layout unchanged', async () => {
    await addOverdueExpense(request, tracked, 'Zaległy 3');
    await openDashboard(page);
    await expect(todoItem(page, 'Zaległy 3')).toBeVisible();
    await expect(pairInHero(page)).toHaveCount(1);
    await resetSamples(page);
    await settleFromCard(page, 'Zaległy 2');
    await expect(todoItem(page, 'Zaległy 2')).toHaveCount(0);
    await expect(todoItem(page, 'Zaległy 3')).toBeVisible();
    await expect(pairInHero(page)).toHaveCount(1);
    expect(await pairAnimations(page)).toBe(0);
  });

  await test.step('Krok 14: settling the last one and undoing it in Planowanie ends with the cards in the top grid and no pending shift', async () => {
    await settleFromCard(page, 'Zaległy 3');
    await expect(pairInHero(page)).toHaveCount(0);
    await openMonth(page, warsawDate(1).slice(0, 7));
    const row = expensePayments(page, 'Zaległy 3');
    await row.getByTestId('undo-expense').click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cofnij', exact: true }).click();
    await expect(row.getByTestId('settle-expense')).toBeVisible();
    await openDashboard(page);
    await expect(todoItem(page, 'Zaległy 3')).toBeVisible();
    await expect(pairInHero(page)).toHaveCount(1);
    await expect(async () => {
      expect(await pair(page).evaluate((element) => element.getAnimations().length)).toBe(0);
    }).toPass({ timeout: 5_000 });
    await expect(pair(page)).toHaveCSS('transform', 'none');
  });
});

test('plynny ruch pulpitu: nothing animates under reduced motion', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(900_000);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const tracked = await prepare(page, request, user);
  await addOverdueExpense(request, tracked, 'Zaległy 1');
  await addOverdueExpense(request, tracked, 'Zaległy 2');

  await expectProjected(
    () => openDashboard(page),
    async () => {
      await expect(amount(page)).toContainText(/\+4\s950,00\szł/, { timeout: 2_000 });
    },
  );
  await expect(pairInHero(page)).toHaveCount(1);

  await test.step('Krok 15: 1M, then 1Y change the selection, corners, figures and captions at once', async () => {
    await expect(thumb(page)).toHaveCSS('transition-duration', /^0s(, 0s)*$/);
    for (const [name, text, corners] of [
      ['1M', /\+950,00\szł/, { left: true, right: false }],
      ['1Y', /\+4\s950,00\szł/, { left: false, right: false }],
    ] as const) {
      await resetSamples(page);
      await rangeToggle(page, name).click();
      await expect(amount(page)).toContainText(text);
      await expectThumbOn(page, name);
      await expectCorners(page, corners);
      const list = await samples(page);
      expect(distinct(list.map((sample) => sample.amount)).length).toBeLessThanOrEqual(2);
      expect(Math.max(...list.map((sample) => sample.swapOut))).toBe(0);
      expect(
        new Set(list.flatMap((sample) => (sample.left === null ? [] : [sample.left]))).size,
      ).toBeLessThanOrEqual(2);
      await expect(caption(page)).toContainText(`zmiana za ${name}`);
    }
  });

  await test.step('Krok 15: settling the overdue expenses moves the cards at once', async () => {
    await resetSamples(page);
    await settleFromCard(page, 'Zaległy 1');
    await expect(todoItem(page, 'Zaległy 1')).toHaveCount(0);
    await settleFromCard(page, 'Zaległy 2');
    await expect(pairInHero(page)).toHaveCount(0);
    expect(await pairAnimations(page)).toBe(0);
  });

  await test.step('Krok 15: a new overdue expense puts the cards back at once', async () => {
    await addOverdueExpense(request, tracked, 'Zaległy 3');
    await openDashboard(page);
    await expect(pairInHero(page)).toHaveCount(1);
    expect(await pairAnimations(page)).toBe(0);
  });
});
