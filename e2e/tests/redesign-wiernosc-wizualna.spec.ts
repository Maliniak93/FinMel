import type { Locator, Page } from '@playwright/test';
import {
  createAccountThroughApi,
  depositCashThroughApi,
  expandWealthSections,
  expect,
  openSelect,
  test,
  warsawDate,
} from './fixtures';
import { addExpense, addIncome, choose, openAddDialog, openMonth } from './planning';
import { expectProjected } from './projection';

const ACCOUNT_TYPE_BROKERAGE = 0;
const ACCOUNT_TYPE_BANK = 2;
const WRAPPER_REGULAR = 0;
const WRAPPER_IKE = 1;
const VARIANT_CURRENT = 0;
const KIND_CUSHION = 1;
const ASSET_CLASS_BANK_ACCOUNT = 9;

const MBANK = 'mBank osobiste';
const XTB = 'XTB IKE';
const CUSHION = 'Poduszka finansowa';
const PENSION = 'Emerytura';
const SALARY = 'Wynagrodzenie';
const RENT = 'Czynsz';
const PREMIA = 'Premia kwartalna';

const WIDE = { width: 1440, height: 900 };
const PHONE = { width: 390, height: 844 };

const TODAY = warsawDate();
const MONTH = TODAY.slice(0, 7);
const YEAR = Number(TODAY.slice(0, 4));
const PREMIA_DATE = warsawDate(23);

type Side = 'top' | 'right' | 'bottom' | 'left';

interface Rule {
  selector: string;
  side: Side;
  text?: string | RegExp;
  wideOnly?: boolean;
}

const SIDEBAR_RULES: Rule[] = [
  { selector: 'mat-sidenav.shell__nav', side: 'right' },
  { selector: '.shell__nav-footer', side: 'top' },
];

const PULPIT_RULES: Rule[] = [
  { selector: '.dashboard-page__hero', side: 'bottom' },
  { selector: '.todo-card__list', side: 'top' },
  { selector: '.todo-card__row', side: 'bottom' },
  { selector: '.month-card__cascade', side: 'top' },
  { selector: '.month-card__line', side: 'bottom' },
  { selector: '.dashboard-page__group-row', side: 'bottom' },
  { selector: '.dashboard-page__structure', side: 'top' },
  { selector: '.dashboard-page__structure-row', side: 'bottom' },
  { selector: '.accounts-card__row', side: 'bottom' },
  { selector: '.cash-card__row', side: 'bottom' },
];

const WEALTH_RULES: Rule[] = [
  { selector: '.mat-mdc-tab-link-container', side: 'bottom' },
  { selector: '.wealth-all__table thead th', side: 'bottom' },
  { selector: 'tr.wealth-all__row', side: 'bottom' },
];

const STRIP_RULES: Rule[] = [
  { selector: '.summary-strip__list', side: 'top' },
  { selector: '.summary-strip__list', side: 'bottom' },
  { selector: '.summary-strip__item + .summary-strip__item', side: 'left' },
];

const ACCOUNTS_RULES: Rule[] = [
  ...STRIP_RULES,
  { selector: '.accounts-page__table th.mat-mdc-header-cell', side: 'bottom' },
  { selector: '.accounts-page__table td.mat-mdc-cell', side: 'bottom' },
];

const PLANNING_RULES: Rule[] = [
  { selector: '.mat-mdc-tab-label-container', side: 'bottom' },
  { selector: '[data-testid="balance-difference"]', side: 'top' },
  { selector: '[data-testid="free-funds"] .cascade__row', side: 'bottom' },
  { selector: '.planning-split__shares', side: 'top' },
  { selector: '.planning-split__shares', side: 'bottom' },
  { selector: '.planning-layout__aside', side: 'left', wideOnly: true },
];

const GROUPS_RULES: Rule[] = [...STRIP_RULES, { selector: '.group-card__allocation', side: 'top' }];

function alphaOf(color: string): number {
  if (color === 'transparent') {
    return 0;
  }
  const slash = /\/\s*([\d.]+%?)\s*\)/.exec(color);
  if (slash) {
    const raw = slash[1] as string;
    return raw.endsWith('%') ? parseFloat(raw) / 100 : parseFloat(raw);
  }
  const rgba = /^rgba\(([^)]*)\)$/.exec(color);
  if (rgba) {
    const parts = (rgba[1] as string).split(',');
    return parts.length === 4 ? parseFloat(parts[3] as string) : 1;
  }
  return 1;
}

function locate(page: Page, rule: Pick<Rule, 'selector' | 'text'>): Locator {
  const base = page.locator(rule.selector);
  return (rule.text ? base.filter({ hasText: rule.text }) : base).first();
}

async function border(
  element: Locator,
  side: Side,
): Promise<{ width: number; style: string; color: string }> {
  return element.evaluate((node, edge) => {
    const style = getComputedStyle(node);
    return {
      width: parseFloat(style.getPropertyValue(`border-${edge}-width`)),
      style: style.getPropertyValue(`border-${edge}-style`),
      color: style.getPropertyValue(`border-${edge}-color`),
    };
  }, side);
}

async function expectRule(page: Page, rule: Rule): Promise<void> {
  const element = locate(page, rule);
  await expect(element, `${rule.selector} is shown`).toBeVisible();
  const measured = await border(element, rule.side);
  const label = `${rule.selector} border-${rule.side}`;
  expect(Math.abs(measured.width - 1), `${label} width`).toBeLessThanOrEqual(0.5);
  expect(measured.style, `${label} style`).toBe('solid');
  expect(alphaOf(measured.color), `${label} colour ${measured.color}`).toBeGreaterThan(0);
}

async function expectRules(page: Page, rules: Rule[], wide = true): Promise<void> {
  for (const rule of rules) {
    if (rule.wideOnly && !wide) {
      continue;
    }
    await expectRule(page, rule);
  }
}

async function expectNoBorder(element: Locator, side: Side): Promise<void> {
  await expect(element).toBeVisible();
  expect((await border(element, side)).width).toBe(0);
}

async function expectPx(element: Locator, property: string, expected: number): Promise<void> {
  await expect(element).toBeVisible();
  const value = await element.evaluate(
    (node, name) => parseFloat(getComputedStyle(node).getPropertyValue(name)),
    property,
  );
  expect(Math.abs(value - expected), `${property} ${value} vs ${expected}`).toBeLessThanOrEqual(1);
}

async function expectUnderline(page: Page): Promise<void> {
  const underline = page
    .locator('.mdc-tab-indicator--active .mdc-tab-indicator__content--underline')
    .first();
  await expect(underline).toBeVisible();
  const measured = await border(underline, 'top');
  expect(Math.abs(measured.width - 2)).toBeLessThanOrEqual(0.5);
  expect(alphaOf(measured.color)).toBeGreaterThan(0);
}

async function overflow(page: Page): Promise<number> {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
}

async function box(
  element: Locator,
): Promise<{ x: number; y: number; width: number; height: number }> {
  await expect(element).toBeVisible();
  const measured = await element.boundingBox();
  expect(measured).not.toBeNull();
  return measured as { x: number; y: number; width: number; height: number };
}

function authorization(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

function themeButton(page: Page): Locator {
  return page.getByTestId('nav-footer').getByRole('button', { name: /^Motyw:/ });
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

async function openDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { name: 'Pulpit', exact: true })).toBeVisible();
}

async function openPlanning(page: Page): Promise<void> {
  await openMonth(page, MONTH);
  await expect(page.getByTestId('split-section')).toBeVisible();
}

async function openGroups(page: Page): Promise<void> {
  await page.goto('/groups');
  await expect(page.getByRole('heading', { name: /^Grupy/ })).toBeVisible();
  await expect(page.getByTestId('group-card')).toHaveCount(2);
}

async function expectGroupCards(page: Page, wide: boolean): Promise<void> {
  const cards = page.getByTestId('group-card');
  await expect(cards).toHaveCount(2);
  await expect(page.locator('.group-card__bar')).toHaveCount(0);
  for (const card of await cards.all()) {
    const top = await border(card, 'top');
    const right = await border(card, 'right');
    expect(Math.abs(top.width - 3)).toBeLessThanOrEqual(0.5);
    expect(top.style).toBe('solid');
    expect(alphaOf(top.color)).toBeGreaterThan(0);
    expect(top.color).not.toBe(right.color);
  }
  const first = await box(cards.nth(0));
  const second = await box(cards.nth(1));
  if (wide) {
    expect(Math.abs(first.y - second.y)).toBeLessThanOrEqual(1);
    expect(second.x).toBeGreaterThanOrEqual(first.x + first.width - 1);
  } else {
    expect(Math.abs(first.x - second.x)).toBeLessThanOrEqual(1);
    expect(second.y).toBeGreaterThanOrEqual(first.y + first.height - 1);
  }
}

async function expectUngrouped(page: Page): Promise<void> {
  const ungrouped = page.getByTestId('ungrouped-card');
  await expect(ungrouped).toBeVisible();
  const measured = await border(ungrouped, 'top');
  expect(measured.style).toBe('dashed');
  expect(alphaOf(measured.color)).toBeGreaterThan(0);
  const last = await box(page.getByTestId('group-card').last());
  expect((await box(ungrouped)).y).toBeGreaterThanOrEqual(last.y + last.height - 1);
}

async function expectGroupsScreen(page: Page, wide: boolean): Promise<void> {
  await expectRules(page, GROUPS_RULES);
  await expectGroupCards(page, wide);
  await expectUngrouped(page);
}

async function setDarkTheme(page: Page): Promise<void> {
  await themeButton(page).click();
  await themeButton(page).click();
  await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: ciemny/);
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
}

async function setSystemTheme(page: Page): Promise<void> {
  await themeButton(page).click();
  await expect(themeButton(page)).toHaveAccessibleName(/^Motyw: systemowy/);
}

async function oneOffIncome(page: Page, income: { name: string; date: string }): Promise<void> {
  await openMonth(page, income.date.slice(0, 7));
  const dialog = await openAddDialog(page);
  await dialog.getByRole('radio', { name: 'Jednorazowy', exact: true }).click();
  await dialog.getByLabel('Nazwa').fill(income.name);
  await dialog.getByRole('spinbutton', { name: 'Kwota' }).fill('1800');
  await choose(
    page,
    dialog.getByRole('combobox', { name: 'Konto', exact: true }),
    new RegExp(`^${MBANK}`),
  );
  await dialog.getByLabel('Data', { exact: true }).fill(income.date);
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
  await expect(dialog).toBeHidden();
}

async function addStock(page: Page): Promise<void> {
  const dialog = page.getByRole('dialog');
  await page.goto('/wealth');
  await page.getByRole('button', { name: 'Nowe aktywo' }).first().click();
  await page.getByRole('menuitem', { name: 'Akcja', exact: true }).click();
  await selectOption(page, dialog.getByRole('combobox', { name: 'Rachunek' }), new RegExp(XTB));
  await dialog.getByLabel('Instrument finansowy').fill('CDR');
  await page.getByRole('option', { name: /CDR\.WA/ }).click();
  await expect(dialog.getByText('Zweryfikowano: CDR.WA')).toBeVisible();
  await dialog.getByRole('checkbox', { name: 'Dodaj pierwszą transakcję' }).check();
  await dialog.getByLabel(/^Ilość/).fill('30');
  await dialog.getByLabel(/^Cena jednostkowa/).fill('200');
  await dialog.getByRole('button', { name: 'Utwórz' }).click();
  await expect(dialog).toBeHidden();
}

test('redesign wierność wizualna: shell, login, Pulpit, Majątek, Planowanie and Grupy measured at 1440 and 390 px and in the dark theme', async ({
  signedInPage: page,
  request,
  user,
}) => {
  test.setTimeout(900_000);
  await page.setViewportSize(WIDE);

  const login = await request.post('/api/identity/login', {
    data: { email: user.email, password: user.password },
  });
  expect(login.ok()).toBeTruthy();
  const token = ((await login.json()) as { accessToken: string }).accessToken;
  const headers = authorization(token);
  const netWorth = page.getByTestId('net-worth');

  await test.step('Przygotowanie: accounts, cash, income, expense, groups and a stock', async () => {
    const mbank = await request.post('/api/portfolio/accounts', {
      headers,
      data: {
        name: MBANK,
        type: ACCOUNT_TYPE_BANK,
        wrapper: WRAPPER_REGULAR,
        currency: 'PLN',
        variant: VARIANT_CURRENT,
        annualInterestRatePercent: 0,
      },
    });
    expect(mbank.ok()).toBeTruthy();
    const mbankCashAssetId = ((await mbank.json()) as { cashAssetId: string }).cashAssetId;
    await depositCashThroughApi(request, token, mbankCashAssetId, {
      quantity: 10000,
      date: warsawDate(100),
    });

    const xtb = await createAccountThroughApi(request, token, {
      name: XTB,
      type: ACCOUNT_TYPE_BROKERAGE,
      wrapper: WRAPPER_IKE,
    });
    await depositCashThroughApi(request, token, xtb.cashAssetId, {
      quantity: 10000,
      date: warsawDate(100),
    });
    const limit = await request.put(
      `/api/portfolio/accounts/${xtb.id}/contribution-limits/${YEAR}`,
      { headers, data: { amount: 20000 } },
    );
    expect(limit.ok()).toBeTruthy();

    const place = await request.post('/api/portfolio/cash-places', {
      headers,
      data: { name: 'Portfel', currencies: ['PLN'] },
    });
    expect(place.ok()).toBeTruthy();
    const balances = ((await place.json()) as { balances: { cashAssetId: string }[] }).balances;
    await depositCashThroughApi(
      request,
      token,
      (balances[0] as { cashAssetId: string }).cashAssetId,
      {
        quantity: 2000,
        date: warsawDate(),
      },
    );

    const cushion = await request.post('/api/portfolio/asset-groups', {
      headers,
      data: {
        name: CUSHION,
        color: 'orange',
        kind: KIND_CUSHION,
        targetPln: 60000,
        monthlyContributionPln: 1000,
        classAllocation: [{ assetClass: ASSET_CLASS_BANK_ACCOUNT, percent: 100 }],
      },
    });
    expect(cushion.ok()).toBeTruthy();
    const pension = await request.post('/api/portfolio/asset-groups', {
      headers,
      data: { name: PENSION, color: 'purple', monthlyContributionPln: 1500 },
    });
    expect(pension.ok()).toBeTruthy();
    for (const [groupResponse, assetId] of [
      [cushion, mbankCashAssetId],
      [pension, xtb.cashAssetId],
    ] as const) {
      const assigned = await request.put('/api/portfolio/asset-groups/assignment', {
        headers,
        data: {
          assetIds: [assetId],
          groupId: ((await groupResponse.json()) as { id: string }).id,
        },
      });
      expect(assigned.ok()).toBeTruthy();
    }

    await openMonth(page, MONTH);
    await addIncome(page, {
      name: SALARY,
      amount: '11400',
      account: MBANK,
      start: `${MONTH}-01`,
      interval: '1',
      day: 9,
    });
    await addExpense(page, {
      name: RENT,
      amount: '1450',
      start: `${MONTH}-01`,
      interval: '1',
      day: 5,
    });
    await oneOffIncome(page, { name: PREMIA, date: PREMIA_DATE });
    await addStock(page);

    await expectProjected(
      () => openDashboard(page),
      async () => {
        await expect(netWorth).toBeVisible({ timeout: 2_000 });
        await expect(page.getByTestId('group-row').first()).toBeVisible({ timeout: 2_000 });
        await expect(page.locator('.dashboard-page__structure-row').first()).toBeVisible({
          timeout: 2_000,
        });
        await expect(page.locator('.cash-card__total')).toBeVisible({ timeout: 2_000 });
      },
    );
  });

  await test.step('Krok 1: logging out shows Logowanie with a 44 px brand and a rule under it, a left Cormorant title, a 380 px column, the language switch 24 px from the top and 32 px from the right, and a wrong password gives one red error line with an icon', async () => {
    await page.getByTestId('nav-footer').getByRole('button', { name: 'Wyloguj' }).click();
    await expect(page).toHaveURL(/\/login$/);

    const brand = page.locator('.auth-shell__brand');
    await expectPx(brand, 'font-size', 44);
    await expectRule(page, { selector: '.auth-shell__brand', side: 'bottom' });
    const brandBox = await box(brand);
    const bottom = (await border(brand, 'bottom')).width;
    expect(bottom).toBeGreaterThan(0);
    expect(brandBox.height).toBeGreaterThan(44);

    const title = page.locator('.auth-shell__title');
    await expect(title).toHaveCSS('text-align', 'left');
    await expect(title).toHaveCSS('font-family', /Cormorant Garamond/);

    const column = await box(page.locator('.auth-shell__column'));
    expect(Math.abs(column.width - 380)).toBeLessThanOrEqual(1);

    const language = await box(page.locator('.auth-shell__language'));
    expect(Math.abs(language.y - 24)).toBeLessThanOrEqual(1);
    expect(Math.abs(WIDE.width - (language.x + language.width) - 32)).toBeLessThanOrEqual(1);

    await page.getByLabel('E-mail').fill(user.email);
    await page.getByLabel('Hasło').fill('zleHaslo1');
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    const error = page.locator('.auth-page__error');
    await expect(error).toHaveCount(1);
    await expect(error).toContainText('Invalid email or password.');
    await expect(error.locator('mat-icon')).toBeVisible();
    await expectPx(error.locator('mat-icon'), 'width', 14);
    const colour = await error.evaluate((node) => getComputedStyle(node).color);
    const [red = 0, green = 0, blue = 0] = (colour.match(/[\d.]+/g) ?? []).map(Number);
    expect(red - Math.max(green, blue)).toBeGreaterThan(60);
  });

  await test.step('Krok 2: after logging in at 1440 px every page has a 1 px rule between the sidebar and the content, 18 px icons, 15 px links, the active link with a left accent rule and a rounded right corner, the name "Skarbiec" in the text colour and a rule above the footer', async () => {
    await page.getByLabel('Hasło').fill(user.password);
    await page.getByRole('button', { name: 'Zaloguj się' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);

    for (const path of ['/dashboard', '/wealth', '/planning', '/groups', '/settings']) {
      await page.goto(path);
      await expect(page.locator('main h1').first()).toBeVisible();
      await expectRules(page, SIDEBAR_RULES);
    }

    await openDashboard(page);
    const links = page.locator('mat-sidenav .shell__nav-link');
    await expect(links).toHaveCount(5);
    for (const link of await links.all()) {
      await expectPx(link, 'font-size', 15);
      await expectPx(link.locator('mat-icon'), 'width', 18);
      await expectPx(link.locator('mat-icon'), 'height', 18);
      await expectPx(link.locator('mat-icon'), 'font-size', 18);
    }

    const active = page.locator('mat-sidenav a.shell__nav-link[aria-current="page"]');
    await expect(active).toHaveCount(1);
    const left = await border(active, 'left');
    expect(Math.abs(left.width - 2)).toBeLessThanOrEqual(0.5);
    expect(alphaOf(left.color)).toBeGreaterThan(0);
    await expectPx(active, 'border-top-right-radius', 4);
    await expectPx(active, 'border-bottom-right-radius', 4);
    await expectPx(active, 'border-top-left-radius', 0);

    const brandColour = await page
      .locator('.shell__brand')
      .evaluate((node) => getComputedStyle(node).color);
    const linkColour = await links
      .filter({ hasText: 'Majątek' })
      .evaluate((node) => getComputedStyle(node).color);
    expect(brandColour).toBe(linkColour);
    expect(Math.abs((await box(page.locator('mat-sidenav'))).width - 232)).toBeLessThanOrEqual(1);
  });

  await test.step('Krok 3: Pulpit has a rule under the header with "Stan na", "Do zrobienia" and "Ten miesiąc" in two columns with rules over the lists and between rows, "Wg grup" and "Struktura majątku" opening with a rule, "Rachunki" and "Gotówka" opening with a rule and "Razem" without a rule above it', async () => {
    await openDashboard(page);
    await expect(page.locator('.dashboard-page__as-of')).toContainText(/Stan na\s.*\d{4}/);
    await expectRules(page, PULPIT_RULES);

    const header = await box(page.locator('.dashboard-page__header'));
    const hero = await box(page.locator('.dashboard-page__hero'));
    expect(hero.y).toBeGreaterThanOrEqual(header.y + header.height - 1);

    await expect(page.getByRole('heading', { name: 'Do zrobienia', exact: true })).toBeVisible();
    await expect(
      page.locator('app-month-card').getByRole('heading', { name: /· plan$/ }),
    ).toBeVisible();
    await expectPx(page.locator('.month-card__title'), 'font-size', 24);
    const todo = await box(page.locator('.dashboard-page__todo'));
    const month = await box(page.locator('app-month-card'));
    expect(Math.abs(todo.y - month.y)).toBeLessThanOrEqual(1);
    expect(month.x).toBeGreaterThanOrEqual(todo.x + todo.width - 1);
    expect(Math.abs(todo.width - month.width)).toBeLessThanOrEqual(1);

    const groups = await box(page.locator('.dashboard-page__group-card'));
    const side = await box(page.locator('.dashboard-page__side'));
    expect(Math.abs(groups.y - side.y)).toBeLessThanOrEqual(1);
    expect(Math.abs(groups.width - side.width)).toBeLessThanOrEqual(2);
    await expectNoBorder(page.locator('.dashboard-page__group-card'), 'top');
    await expect(page.getByRole('heading', { name: 'Wg grup', exact: true })).toBeVisible();
    await expect(
      page.getByRole('heading', { name: 'Struktura majątku', exact: true }),
    ).toBeVisible();

    await expect(page.getByRole('heading', { name: 'Rachunki', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Gotówka', exact: true })).toBeVisible();
    const total = page.locator('.cash-card__total');
    await expect(total).toContainText('Razem');
    await expectNoBorder(total, 'top');
  });

  await test.step('Krok 4: in the dark theme the same Pulpit rules are visible', async () => {
    await setDarkTheme(page);
    await expectRules(page, SIDEBAR_RULES);
    await expectRules(page, PULPIT_RULES);
    await expectNoBorder(page.locator('.cash-card__total'), 'top');
    await setSystemTheme(page);
  });

  await test.step('Krok 5: Majątek has tabs with a 2 px accent underline and a rule under the tab bar, a 36 px Cormorant heading, a table header with a rule under it, rows separated by rules and a rule over "Razem"', async () => {
    await page.goto('/wealth');
    await expandWealthSections(page);
    const heading = page.locator('h1.wealth__title');
    await expect(heading).toHaveText('Majątek');
    await expectPx(heading, 'font-size', 36);
    await expect(heading).toHaveCSS('font-family', /Cormorant Garamond/);
    await expectRules(page, WEALTH_RULES);
    await expectUnderline(page);

    await page.goto('/wealth/securities');
    await expectRule(page, {
      selector: '.securities-table__table th.mat-mdc-header-cell',
      side: 'bottom',
    });
    await expectRule(page, {
      selector: '.securities-table__table td.mat-mdc-cell',
      side: 'bottom',
    });
    await expectRule(page, {
      selector: 'td.mat-mdc-footer-cell',
      side: 'top',
      text: 'Razem',
    });
  });

  await test.step('Krok 6: Majątek "Rachunki" has the "Razem" strip with rules above and below and vertical separators, no frame and no background; "Gotówka" has rules over the balance rows; the row menu icon is 16 px', async () => {
    await page.goto('/wealth/accounts');
    await expectRules(page, ACCOUNTS_RULES);
    const list = page.locator('.summary-strip__list');
    await expect(list).toContainText('Razem');
    await expectNoBorder(list, 'left');
    await expectNoBorder(list, 'right');
    await expect(list).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
    await expectNoBorder(page.locator('.summary-strip__item').first(), 'left');
    await expectPx(page.locator('.accounts-page__table mat-icon').first(), 'width', 16);
    await expectPx(page.locator('.accounts-page__table mat-icon').first(), 'font-size', 16);

    await page.goto('/wealth/cash');
    await expectRule(page, { selector: '.cash-place__balance', side: 'top' });
    await expectRule(page, { selector: '.summary-strip__list', side: 'top' });
    await expectPx(page.locator('.cash-place__name').first(), 'font-size', 22);

    await page.goto('/wealth');
    await expandWealthSections(page);
    await expectPx(page.locator('.wealth-all__actions mat-icon').first(), 'width', 16);
    await expectPx(page.locator('.wealth-all__actions mat-icon').first(), 'font-size', 16);
  });

  await test.step('Krok 7: the details of the XTB IKE account and the transaction list of the stock have a heading and a table in the same scale as the list', async () => {
    await page.goto('/wealth/accounts');
    await page.getByRole('link', { name: XTB, exact: true }).click();
    await expect(page).toHaveURL(/\/wealth\/accounts\/[0-9a-f-]+$/);
    const title = page.locator('.account-detail__title');
    await expect(title).toHaveText(XTB);
    await expectPx(title, 'font-size', 36);
    await expect(title).toHaveCSS('font-family', /Cormorant Garamond/);
    await expectRule(page, {
      selector: '.account-detail__table th.mat-mdc-header-cell',
      side: 'bottom',
    });
    await expectRule(page, { selector: '.account-detail__table td.mat-mdc-cell', side: 'bottom' });
    await expectPx(page.locator('.account-detail__table mat-icon').first(), 'width', 16);

    await page.goto('/wealth/securities');
    const row = page.getByRole('row').filter({ hasText: /CD Projekt/ });
    await row.getByRole('button', { name: /^Akcje dla: / }).click();
    await page.getByRole('menuitem', { name: 'Historia transakcji' }).click();
    await expect(page).toHaveURL(/\/wealth\/assets\/[0-9a-f-]+\/transactions$/);
    await expect(page.locator('.transactions-page__breadcrumb')).toBeVisible();
    const heading = page.locator('.transactions-page h1').first();
    await expect(heading).toContainText('CD Projekt');
    await expectPx(heading, 'font-size', 36);
    await expect(heading).toHaveCSS('font-family', /Cormorant Garamond/);
    await expectRule(page, {
      selector: '.transactions-page__table th.mat-mdc-header-cell',
      side: 'bottom',
    });
    await expectRule(page, {
      selector: '.transactions-page__table td.mat-mdc-cell',
      side: 'bottom',
    });
  });

  await test.step('Krok 8: Planowanie has tabs like Majątek, a Lista / Kalendarz switch with icons, a "Bilans miesiąca" column with a left rule, a rule over "Różnica", rules between the "Wolne środki" rows and rules over and under the "Podział" list', async () => {
    await openPlanning(page);
    await expectUnderline(page);
    await expectRules(page, PLANNING_RULES);
    await expect(page.getByTestId('layout-list').locator('mat-icon')).toHaveText('view_list');
    await expect(page.getByTestId('layout-calendar').locator('mat-icon')).toHaveText(
      'calendar_month',
    );
    await expectPx(
      page.getByRole('heading', { name: 'Bilans miesiąca', exact: true }),
      'font-size',
      22,
    );
    await expectPx(
      page.getByRole('heading', { name: 'Wolne środki', exact: true }),
      'font-size',
      22,
    );
    await expectPx(
      page.getByRole('heading', { name: 'Podział wolnych środków', exact: true }),
      'font-size',
      22,
    );
    await expect(page.locator('h1').first()).toHaveCSS('font-family', /Cormorant Garamond/);
  });

  await test.step('Krok 9: Kalendarz is a grid of thin lines without cell frames or gaps and a ruled weekday row; Rok has ruled table rows', async () => {
    await page.getByTestId('layout-calendar').click();
    await expect(page.getByTestId('planning-calendar')).toBeVisible();
    const weekdays = page.locator('.planning-calendar__weekdays');
    await expectRule(page, { selector: '.planning-calendar__weekdays', side: 'top' });
    await expectRule(page, { selector: '.planning-calendar__weekdays', side: 'left' });
    await expectRule(page, { selector: '.planning-calendar__weekdays span', side: 'right' });
    await expectRule(page, { selector: '.planning-calendar__weekdays span', side: 'bottom' });
    await expectRule(page, { selector: '.planning-calendar__grid', side: 'left' });
    await expect(weekdays).toBeVisible();

    const grid = page.locator('.planning-calendar__grid');
    await expect(grid).toHaveCSS('row-gap', '0px');
    await expect(grid).toHaveCSS('column-gap', '0px');
    const cell = page.locator('[data-testid="calendar-cell"][data-date]').first();
    await expectRule(page, { selector: '[data-testid="calendar-cell"][data-date]', side: 'right' });
    await expectRule(page, {
      selector: '[data-testid="calendar-cell"][data-date]',
      side: 'bottom',
    });
    await expectNoBorder(cell, 'top');
    await expectNoBorder(cell, 'left');
    await expectPx(cell, 'border-top-left-radius', 0);

    await page.locator(`[data-testid="calendar-cell"][data-date="${TODAY}"]`).click();
    await expectPx(page.locator('.planning-calendar__details h3'), 'font-size', 20);

    await page.getByTestId('layout-list').click();
    await page.getByRole('tab', { name: 'Rok', exact: true }).click();
    await expect(page.getByTestId('year-row')).toHaveCount(12);
    await expectRule(page, { selector: '.planning-year__table th', side: 'bottom' });
    await expectRule(page, { selector: '.planning-year__table td', side: 'bottom' });
  });

  await test.step('Krok 10: Grupy has the strip with rules above and below and vertical separators, cards in two columns each with a 3 px top edge in the group colour, a rule over the allocation block and "Bez grupy" as a dashed row under the grid', async () => {
    await openGroups(page);
    await expect(page.locator('.summary-strip__item')).toHaveCount(3);
    await expectGroupsScreen(page, true);
    await expectPx(page.locator('.group-card__allocation'), 'padding-top', 9.2);
  });

  await test.step('Krok 11: the "Dodaj grupę" dialog is 540 px wide with a rule over the allocation, a 10 px live bar and an 18 px section title', async () => {
    await page.getByTestId('add-group').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByTestId('add-allocation-row').click();
    const container = await box(page.locator('mat-dialog-container'));
    expect(Math.abs(container.width - 540)).toBeLessThanOrEqual(1);
    await expectRule(page, { selector: '[data-testid="allocation-editor"]', side: 'top' });
    await expectPx(page.getByTestId('allocation-live-bar'), 'height', 10);
    await expectPx(page.locator('[data-testid="allocation-editor"] legend'), 'font-size', 18);
    await dialog.getByRole('button', { name: 'Anuluj' }).click();
    await expect(dialog).toBeHidden();
  });

  await test.step('Krok 12: at 390 px Pulpit has a 68 px bottom navigation with a rule above, the active entry with a 2 px top rule, 20 px icons and 12 px labels, and the sections one under another, each with a rule', async () => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    const bottom = page.getByTestId('bottom-nav');
    await expect(bottom).toBeVisible();
    await expectPx(bottom, 'height', 68);
    expect(Math.abs((await box(bottom)).height - 68)).toBeLessThanOrEqual(1);
    await expectRule(page, { selector: '.shell__bottom-nav', side: 'top' });

    const active = bottom.locator('a[aria-current="page"]');
    await expect(active).toHaveCount(1);
    const activeTop = await border(active, 'top');
    expect(Math.abs(activeTop.width - 2)).toBeLessThanOrEqual(0.5);
    expect(alphaOf(activeTop.color)).toBeGreaterThan(0);
    for (const link of await bottom.getByRole('link').all()) {
      await expectPx(link.locator('mat-icon'), 'width', 20);
      await expectPx(link.locator('mat-icon'), 'height', 20);
      await expectPx(link.locator('mat-icon'), 'font-size', 20);
      await expectPx(link.locator('.shell__nav-label'), 'font-size', 12);
    }

    await expectRules(page, PULPIT_RULES);
    const stacked = [
      page.locator('.dashboard-page__structure'),
      page.locator('.dashboard-page__todo'),
      page.locator('app-month-card'),
      page.locator('.dashboard-page__group-card'),
      page.locator('app-accounts-card'),
      page.locator('app-cash-card'),
    ];
    let previous = await box(stacked[0] as Locator);
    for (const section of stacked.slice(1)) {
      const current = await box(section);
      expect(current.y).toBeGreaterThanOrEqual(previous.y + previous.height - 1);
      expect(Math.abs(current.x - previous.x)).toBeLessThanOrEqual(1);
      previous = current;
    }
    expect(await overflow(page)).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 13: at 390 px Majątek, Planowanie and Grupy keep the same rules as on the desktop and do not scroll sideways', async () => {
    await page.goto('/wealth');
    await expandWealthSections(page);
    await expect(page.locator('h1.wealth__title')).toBeVisible();
    await expectRules(page, WEALTH_RULES);
    await expectUnderline(page);
    expect(await overflow(page)).toBeLessThanOrEqual(0);

    await page.goto('/wealth/accounts');
    await expectRules(page, ACCOUNTS_RULES);
    expect(await overflow(page)).toBeLessThanOrEqual(0);

    await openPlanning(page);
    await expectUnderline(page);
    await expectRules(page, PLANNING_RULES, false);
    await expectRule(page, { selector: 'app-summary-strip .summary-strip__list', side: 'top' });
    await expectRule(page, { selector: 'app-summary-strip .summary-strip__list', side: 'bottom' });
    expect(await overflow(page)).toBeLessThanOrEqual(0);

    await openGroups(page);
    await expectGroupsScreen(page, false);
    expect(await overflow(page)).toBeLessThanOrEqual(0);
  });

  await test.step('Krok 14: in the dark theme no rule disappears on any screen', async () => {
    await page.setViewportSize(WIDE);
    await openDashboard(page);
    await setDarkTheme(page);

    await expectRules(page, SIDEBAR_RULES);
    await expectRules(page, PULPIT_RULES);

    await page.goto('/wealth');
    await expandWealthSections(page);
    await expect(page.locator('h1.wealth__title')).toBeVisible();
    await expectRules(page, WEALTH_RULES);
    await expectUnderline(page);

    await page.goto('/wealth/accounts');
    await expectRules(page, ACCOUNTS_RULES);

    await page.goto('/wealth/securities');
    await expectRule(page, { selector: 'td.mat-mdc-footer-cell', side: 'top', text: 'Razem' });

    await openPlanning(page);
    await expectUnderline(page);
    await expectRules(page, PLANNING_RULES);

    await openGroups(page);
    await expectGroupsScreen(page, true);
  });
});
