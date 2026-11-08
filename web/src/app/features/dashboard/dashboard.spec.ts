import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltip } from '@angular/material/tooltip';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';

import {
  TRANSLATIONS,
  attributesOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
  textsOf,
} from '../../../testing/i18n';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse, NetWorthHistoryResponse } from '../../api/reporting';
import { ASSET_CLASS } from '../assets/asset-class';
import { Dashboard } from './dashboard';
import { provideI18nTesting } from '../../core/i18n/testing';
import { rowLineSkipsLast, ruleBodies } from '../../../testing/stylesheet';
import { formatDate, formatMoney, formatPercent, formatSignedMoney } from '../../shared/format';
import {
  type ApiRoute,
  type Responder,
  cashPlacesResponse,
  dateOnlyFromToday,
  dashboard,
  dashboardWithClasses,
  dashboardWithGroups,
  groups,
  INVESTMENTS_GROUP_ID,
  RETIREMENT_GROUP_ID,
  CUSHION_GROUP_ID,
  jsonResponse,
  lastHistoryParams,
  manyGroups,
  menuItem,
  openFilterMenu,
  resizeDashboardTo,
  stubResizeObserver,
  mockApi,
  accountResponse,
  overdueExpenseItem,
  overdueIncomeItem,
  requestUrl,
  todoResponse,
  upcomingPaymentItem,
} from './testing/dashboard-fixtures';

function normalised(text: string): string {
  return text.replace(/\s+/g, ' ');
}

function followsInDocument(earlier: Element, later: Element): boolean {
  return (earlier.compareDocumentPosition(later) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
}

function sectionsOf(element: HTMLElement) {
  const query = (selector: string): HTMLElement => {
    const node = element.querySelector<HTMLElement>(selector);
    if (!node) {
      throw new Error(`No ${selector} rendered.`);
    }
    return node;
  };
  return {
    hero: query('.dashboard-page__hero'),
    kpi: query('.dashboard-page__value'),
    history: query('.dashboard-page__history'),
    chart: query('app-net-worth-chart'),
    structure: query('.dashboard-page__structure'),
    todo: query('.dashboard-page__todo'),
    month: query('app-month-card'),
  };
}

function dashboardStylesheet(): string {
  return readFileSync(resolve(process.cwd(), 'src/app/features/dashboard/dashboard.scss'), 'utf8');
}

const HERO_CONTAINER_MIXIN = /@include\s+sk-from\(\s*\$sk-bp-sm\s*,\s*\$container:\s*true\s*\)/;
const COMPILED_HERO_CONTAINER = /@container\s*\((?:width\s*>=\s*600px|min-width:\s*600px)\)/;

describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let component: Dashboard;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    vi.unstubAllGlobals();
    await restoreEnglish();
  });

  async function setup(
    dashboardResponse: Response,
    history: Responder = jsonResponse({ range: '1Y', points: [] }),
    cards: Partial<Record<ApiRoute, Responder>> = {},
  ): Promise<void> {
    fetchSpy = mockApi({ ...cards, dashboard: dashboardResponse, history });

    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(Dashboard);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse(dashboard));
    expect(component).toBeTruthy();
  });

  it('loads the dashboard into its resource', async () => {
    await setup(jsonResponse(dashboard));
    expect(component['dashboardResource'].value()).toEqual(dashboard);
  });

  it('shows the formatted net worth and the as-of date', async () => {
    await setup(jsonResponse(dashboard));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    const expectedNetWorth = new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'PLN',
    }).format(15000);
    expect(text).toContain(expectedNetWorth);
    expect(text).toContain('As of');
  });

  it('shows a stale chip when the dashboard is stale', async () => {
    await setup(jsonResponse({ ...dashboard, isStale: true } satisfies DashboardResponse));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Stale');
  });

  it('gives the BankAccount class a colour and the label "Konta bankowe"', async () => {
    await setup(
      jsonResponse({
        ...dashboard,
        byAssetClass: [
          { assetClass: ASSET_CLASS.Cash, valuePln: 5000, percentage: 50 },
          { assetClass: ASSET_CLASS.BankAccount, valuePln: 5000, percentage: 50 },
        ],
      } satisfies DashboardResponse),
    );

    const segments = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
      '.dashboard-page__structure .split-bar__segment',
    );
    const colors = [...segments].map((segment) => segment.style.backgroundColor);

    expect(segments).toHaveLength(2);
    expect(colors[0]).toBeTruthy();
    expect(colors[1]).toBeTruthy();
    expect(colors[0]).not.toBe(colors[1]);
    expect(component['structureSegments']()[1].label).toBe('Bank accounts');

    await switchLanguage(fixture, 'pl');

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Konta bankowe');
  });

  it('shows an empty state when no snapshot has ever been computed', async () => {
    await setup(
      jsonResponse({
        netWorthPln: 0,
        asOf: null,
        isStale: false,
        byAssetClass: [],
        byAccount: [],
        byGroup: [],
        byAsset: [],
      } satisfies DashboardResponse),
    );
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("You haven't entered any wealth yet.");
  });

  it('surfaces a load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
    expect(component['dashboardResource'].error()?.message).toBe('Service unavailable.');
  });
  function historyWithChange(changePln: number | null, changePercent: number | null) {
    return jsonResponse({
      range: '1Y',
      points: [
        { date: '2026-07-01', netWorthPln: 1000, isIncomplete: false },
        { date: '2026-08-01', netWorthPln: 1250, isIncomplete: false },
      ],
      changePln,
      changePercent,
    } satisfies NetWorthHistoryResponse);
  }

  function changeText(): string {
    return textOf(
      (fixture.nativeElement as HTMLElement).querySelector(
        '.dashboard-page__value .dashboard-page__change',
      ),
    );
  }

  it('shows the change for the selected range', async () => {
    await setup(jsonResponse(dashboard), (url) =>
      url.includes('range=YTD') ? historyWithChange(250, 25) : historyWithChange(40, 4),
    );
    const element = fixture.nativeElement as HTMLElement;
    expect(lastHistoryParams(fetchSpy).get('range')).toBe('1Y');

    const ytdToggle = [...element.querySelectorAll('mat-button-toggle button')].find(
      (button) => button.textContent?.trim() === 'YTD',
    ) as HTMLButtonElement;
    ytdToggle.click();
    await fixture.whenStable();

    const change = element.querySelector('.dashboard-page__change') as HTMLElement;
    expect(lastHistoryParams(fetchSpy).get('range')).toBe('YTD');
    expect(normalised(change.textContent ?? '')).toContain(normalised(formatSignedMoney(250)));
    expect(normalised(change.textContent ?? '')).toContain(normalised(`(+${formatPercent(25)})`));
    expect(change.classList).toContain('dashboard-page__change--positive');
    expect(change.classList).not.toContain('dashboard-page__change--negative');

    await switchLanguage(fixture, 'pl');

    expect(normalised(change.textContent ?? '')).toContain('zmiana za YTD');
  });

  it('passes the incomplete flag to the chart', async () => {
    await setup(
      jsonResponse(dashboard),
      jsonResponse({
        range: '1Y',
        points: [
          { date: '2026-07-01', netWorthPln: 1000, isIncomplete: true },
          { date: '2026-07-02', netWorthPln: 1100, isIncomplete: true },
          { date: '2026-07-03', netWorthPln: 1200, isIncomplete: false },
        ],
        changePln: null,
        changePercent: null,
      } satisfies NetWorthHistoryResponse),
    );
    const element = fixture.nativeElement as HTMLElement;

    expect(component['historyPoints']().map((p) => p.isIncomplete)).toEqual([true, true, false]);
    expect(element.querySelectorAll('app-net-worth-chart .net-worth-chart__band')).toHaveLength(1);
  });

  it('shows how many valuations are stale', async () => {
    const line = (assetId: string, isStale: boolean) => ({
      assetId,
      valuePln: 100,
      priceDate: '2026-07-01',
      isStale,
    });
    await setup(
      jsonResponse({
        ...dashboard,
        isStale: true,
        byAsset: [line('a1', true), line('a2', true), line('a3', false)],
      } satisfies DashboardResponse),
    );
    await switchLanguage(fixture, 'pl');
    const text = normalised((fixture.nativeElement as HTMLElement).textContent ?? '');

    expect(text).toContain('Nieaktualne');
    expect(text).toContain('Wycena 2 pozycji od dawna się nie odświeżyła');
  });

  it('hides the stale note when no valuation is stale', async () => {
    await setup(
      jsonResponse({
        ...dashboard,
        byAsset: [{ assetId: 'a1', valuePln: 100, priceDate: '2026-08-08', isStale: false }],
      } satisfies DashboardResponse),
    );
    await switchLanguage(fixture, 'pl');
    const text = normalised((fixture.nativeElement as HTMLElement).textContent ?? '');

    expect(text).not.toContain('Nieaktualne');
    expect(text).not.toContain('od dawna się nie odświeżyła');
  });

  it('shows one empty state without a valuation', async () => {
    await setup(
      jsonResponse({
        netWorthPln: 0,
        asOf: null,
        isStale: false,
        byAssetClass: [],
        byAccount: [],
        byGroup: [],
        byAsset: [],
      } satisfies DashboardResponse),
    );
    await switchLanguage(fixture, 'pl');
    const element = fixture.nativeElement as HTMLElement;
    const states = element.querySelectorAll('app-empty-state');

    expect(states).toHaveLength(1);
    expect(textOf(states[0].querySelector('a[href="/wealth/accounts"]'))).toBe('Dodaj rachunek');
    for (const selector of [
      '.dashboard-page__value',
      '.dashboard-page__hero',
      'app-net-worth-chart',
      '.dashboard-page__structure',
      '.dashboard-page__group-card',
      'app-accounts-card',
      'app-cash-card',
      'app-month-card',
      'app-upcoming-card',
    ]) {
      expect(element.querySelector(selector), selector).toBeNull();
    }
  });

  it('a failed history does not replace the page', async () => {
    let calls = 0;
    await setup(jsonResponse(dashboardWithClasses), () => {
      calls++;
      return calls === 1
        ? jsonResponse({ detail: 'History unavailable.' }, 503)
        : historyWithChange(250, 25);
    });
    const element = fixture.nativeElement as HTMLElement;
    const slot = element.querySelector('app-net-worth-chart app-section-error') as HTMLElement;

    expect(textOf(slot)).toContain('History unavailable.');
    expect(element.querySelectorAll('app-section-error')).toHaveLength(1);
    expect(element.querySelector('.dashboard-page__value')).not.toBeNull();
    expect(element.querySelector('.dashboard-page__structure')).not.toBeNull();
    expect(element.querySelector('app-accounts-card')).not.toBeNull();
    expect(element.querySelector('app-cash-card')).not.toBeNull();

    (slot.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element.querySelector('app-section-error')).toBeNull();
    expect(changeText()).toContain(normalised(formatMoney(250)));
  });

  it('renders the structure bar and class links', async () => {
    await setup(jsonResponse(dashboardWithClasses));
    await switchLanguage(fixture, 'pl');
    const structure = (fixture.nativeElement as HTMLElement).querySelector(
      '.dashboard-page__structure',
    ) as HTMLElement;

    expect(structure.textContent).toContain('Struktura majątku');
    expect(structure.querySelectorAll('app-split-bar')).toHaveLength(1);
    expect(structure.querySelectorAll('app-split-bar .split-bar__segment')).toHaveLength(3);

    const rows = [...structure.querySelectorAll('li')];
    expect(rows).toHaveLength(3);
    const expected = [
      { href: '/wealth/securities', value: 6000, share: 40 },
      { href: '/wealth/cash', value: 5000, share: 33.33 },
      { href: '/wealth/securities', value: 4000, share: 26.67 },
    ];
    expected.forEach((row, index) => {
      const text = normalised(rows[index].textContent ?? '');
      expect(rows[index].querySelector(`a[href="${row.href}"]`)).not.toBeNull();
      expect(text).toContain(normalised(formatMoney(row.value)));
      expect(text).toContain(normalised(formatPercent(row.share)));
    });
    expect(textsOf(structure, 'li a')).toEqual(['Akcje', 'Gotówka', 'ETF']);
  });

  it('puts the structure in the hero after the chart', async () => {
    await setup(jsonResponse(dashboardWithClasses));
    const element = fixture.nativeElement as HTMLElement;
    const hero = element.querySelector('.dashboard-page__hero') as HTMLElement;

    expect(hero).not.toBeNull();
    expect([...hero.children].map((child) => child.className)).toEqual([
      'dashboard-page__value',
      'dashboard-page__history',
      'dashboard-page__structure',
    ]);
    expect(
      element.querySelector('.dashboard-page__pair--split .dashboard-page__structure'),
    ).toBeNull();
  });

  it('stacks the sections on a narrow container', async () => {
    await setup(jsonResponse(dashboardWithGroups()), undefined, { groups: jsonResponse(groups) });
    const element = fixture.nativeElement as HTMLElement;

    const order = [
      '.dashboard-page__hero',
      '.dashboard-page__hero .dashboard-page__structure',
      '.dashboard-page__todo',
      'app-month-card',
      '.dashboard-page__group-card',
      'app-accounts-card',
      'app-cash-card',
    ];
    const nodes = order.map((selector) => element.querySelector(selector));
    nodes.forEach((node, index) => expect(node, order[index]).not.toBeNull());
    expect([...element.querySelectorAll(order.join(', '))]).toEqual(nodes);

    // jsdom cannot evaluate container queries, so read the component's compiled stylesheet.
    const css =
      [...document.querySelectorAll('style')]
        .map((style) => style.textContent ?? '')
        .find((text) => text.includes('.dashboard-page__hero')) ?? '';
    const split = css.search(COMPILED_HERO_CONTAINER);
    expect(split).toBeGreaterThan(-1);
    expect(css.slice(0, split)).toMatch(/\.dashboard-page__hero[^{]*\{[^}]*border-bottom:/);
    expect(css.slice(0, split)).not.toMatch(/\d+fr\s+\d+fr/);
    expect(css.slice(split)).not.toMatch(/\b[57]fr\b/);
  });

  it('puts accounts and cash in one column beside the groups', async () => {
    await setup(jsonResponse(dashboardWithGroups()), undefined, { groups: jsonResponse(groups) });
    const element = fixture.nativeElement as HTMLElement;
    const split = element.querySelector('.dashboard-page__pair--split') as HTMLElement;

    expect(split).not.toBeNull();
    const children = [...split.children] as HTMLElement[];
    expect(children).toHaveLength(2);
    expect(children[0]).toBe(element.querySelector('.dashboard-page__group-card'));
    expect(children[1].querySelector('app-accounts-card')).not.toBeNull();
    expect(children[1].querySelector('app-cash-card')).not.toBeNull();
    expect([...children[1].children].map((child) => child.tagName.toLowerCase())).toEqual([
      'app-accounts-card',
      'app-contribution-limits-card',
      'app-cash-card',
    ]);
    expect(element.querySelector('.dashboard-page__pair--ruled')).toBeNull();
  });

  it('renders the as-of date in the subtitle of the page header', async () => {
    await setup(jsonResponse(dashboard));
    const element = fixture.nativeElement as HTMLElement;
    const header = element.querySelector('app-page-header');
    const asOf = header?.querySelector('.dashboard-page__as-of');

    expect(element.querySelectorAll('app-page-header h1')).toHaveLength(1);
    expect(header?.querySelector('h1')?.textContent?.trim()).toBe('Dashboard');
    expect(asOf?.textContent).toContain('As of');
    expect(asOf?.closest('.page-header__subtitle')).not.toBeNull();
    expect(asOf?.closest('.dashboard-page__value')).toBeNull();
  });

  it('shows no as-of line when the dashboard has no date', async () => {
    await setup(jsonResponse({ ...dashboard, asOf: null }));
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('app-page-header h1')?.textContent?.trim()).toBe('Dashboard');
    expect(element.querySelector('.dashboard-page__as-of')).toBeNull();
  });

  it('gives every card title the global section title class', async () => {
    await setup(jsonResponse(dashboard));
    const element = fixture.nativeElement as HTMLElement;
    const titles = element.querySelectorAll('.dashboard-page__grid h2, .dashboard-page__grid h3');

    expect(titles.length).toBeGreaterThan(0);
    for (const title of titles) {
      expect(title.classList.contains('sk-section-title'), title.textContent ?? '').toBe(true);
    }
  });

  it('renders groups and structure without a card frame', async () => {
    await setup(jsonResponse(dashboardWithClasses), undefined, { groups: jsonResponse(groups) });
    const element = fixture.nativeElement as HTMLElement;

    for (const selector of ['.dashboard-page__group-card', '.dashboard-page__structure']) {
      const section = element.querySelector(selector);
      expect(section, selector).not.toBeNull();
      expect(section?.tagName.toLowerCase(), selector).not.toBe('mat-card');
      expect(section?.classList.contains('mat-mdc-card'), selector).toBe(false);
      expect(section?.querySelector('mat-card, .mat-mdc-card'), selector).toBeNull();
    }
    expect(element.querySelectorAll('.dashboard-page__group-row').length).toBeGreaterThan(0);
    expect(element.querySelectorAll('.dashboard-page__structure-row').length).toBeGreaterThan(0);

    const scss = dashboardStylesheet();
    expect(ruleBodies(scss, '.dashboard-page__group-card')).not.toMatch(/border-top/);
    expect(ruleBodies(scss, '.dashboard-page__structure')).toMatch(/border-top:/);
  });

  it('styles a negative change as negative', async () => {
    await setup(jsonResponse(dashboard), historyWithChange(-250, -20));

    const change = (fixture.nativeElement as HTMLElement).querySelector(
      '.dashboard-page__change',
    ) as HTMLElement;

    expect(change.classList).toContain('dashboard-page__change--negative');
    expect(change.classList).not.toContain('dashboard-page__change--positive');
    expect(normalised(change.textContent ?? '')).toContain(normalised(formatSignedMoney(-250)));
    expect(change.textContent).toContain('−');
  });

  it('colours a zero or rounded-zero change neutral', async () => {
    await setup(jsonResponse(dashboard), (url) =>
      url.includes('range=YTD') ? historyWithChange(0.004, 25) : historyWithChange(0, 0),
    );
    const element = fixture.nativeElement as HTMLElement;
    const change = () => element.querySelector('.dashboard-page__change') as HTMLElement;

    expect(change().classList).toContain('dashboard-page__change--neutral');
    expect(change().classList).not.toContain('dashboard-page__change--positive');
    expect(change().classList).not.toContain('dashboard-page__change--negative');

    const ytdToggle = [...element.querySelectorAll('mat-button-toggle button')].find(
      (button) => button.textContent?.trim() === 'YTD',
    ) as HTMLButtonElement;
    ytdToggle.click();
    await fixture.whenStable();

    expect(change().classList).toContain('dashboard-page__change--neutral');
    expect(change().classList).not.toContain('dashboard-page__change--positive');
    expect(change().classList).not.toContain('dashboard-page__change--negative');
    expect(normalised(change().textContent ?? '')).not.toContain('+');
  });

  it('switching the range updates the change', async () => {
    await setup(jsonResponse(dashboard), (url) =>
      url.includes('range=1M') ? historyWithChange(40, 4) : historyWithChange(250, 25),
    );
    const element = fixture.nativeElement as HTMLElement;
    expect(changeText()).toContain(normalised(formatMoney(250)));

    const monthToggle = [...element.querySelectorAll('mat-button-toggle button')].find(
      (button) => button.textContent?.trim() === '1M',
    ) as HTMLButtonElement;
    monthToggle.click();
    await fixture.whenStable();

    const historyUrls = fetchSpy.mock.calls
      .map(([input]: [unknown]) => requestUrl(input))
      .filter((url: string) => url.includes('/net-worth-history'));
    expect(historyUrls.at(-1)).toContain('range=1M');
    expect(changeText()).toContain(normalised(formatMoney(40)));
    expect(changeText()).toContain('1M');
  });

  it('renders in Polish', async () => {
    await setup(
      jsonResponse({ ...dashboard, isStale: true } satisfies DashboardResponse),
      historyWithChange(250, 25),
    );
    const element = fixture.nativeElement as HTMLElement;
    const tooltip = () =>
      fixture.debugElement.query(By.css('mat-chip')).injector.get(MatTooltip).message;
    const texts = () => [
      ...textsOf(element, 'h1'),
      ...textsOf(element, '.dashboard-page__kpi-label'),
      textOf(element.querySelector('mat-chip')),
      tooltip(),
      ...attributesOf(element, 'svg.net-worth-chart__svg', 'aria-label'),
    ];
    const asOf = () => textOf(element.querySelector('.dashboard-page__as-of'));

    const english = texts();
    expect(english[0]).toBe('Dashboard');
    expect(english.length).toBeGreaterThanOrEqual(5);
    expect(asOf()).toMatch(/^As of /);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
    expect(asOf()).not.toMatch(/^As of /);
    expect(asOf()).toContain(formatDate('2026-08-09'));
    expect(matchesTranslation('pl', asOf()), `"${asOf()}" is not a pl.json value`).toBe(true);
  });

  it('renders the empty state in Polish', async () => {
    await setup(
      jsonResponse({
        netWorthPln: 0,
        asOf: null,
        isStale: false,
        byAssetClass: [],
        byAccount: [],
        byGroup: [],
        byAsset: [],
      } satisfies DashboardResponse),
    );
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h1')),
      textOf(element.querySelector('app-empty-state p')),
      textOf(element.querySelector('app-empty-state a')),
    ];

    const english = texts();
    expect(english).toEqual(['Dashboard', "You haven't entered any wealth yet.", 'Add account']);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });

  it('renders the load-failure retry button in Polish', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
    const element = fixture.nativeElement as HTMLElement;

    expect(textsOf(element, '.dashboard-page__state button')).toEqual(['Retry']);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(['Retry'], textsOf(element, '.dashboard-page__state button'))).toEqual(
      [],
    );
    expect(textOf(element.querySelector('.dashboard-page__state p'))).toBe('Service unavailable.');
  });

  it('the value card holds net worth and change', async () => {
    await setup(
      jsonResponse({ ...dashboard, isStale: true } satisfies DashboardResponse),
      historyWithChange(250, 25),
    );
    const element = fixture.nativeElement as HTMLElement;
    const value = element.querySelector('.dashboard-page__value') as HTMLElement;

    expect(element.querySelectorAll('.dashboard-page__value')).toHaveLength(1);
    expect(value.textContent).toContain(formatMoney(15000));
    expect(value.querySelector('.dashboard-page__as-of')).toBeNull();
    expect(value.textContent).toContain('Stale');
    expect(changeText()).toContain(normalised(formatMoney(250)));
    expect(changeText()).toContain('1Y');
  });

  it('the value card shows the dash when history fails', async () => {
    await setup(jsonResponse(dashboard), jsonResponse({ detail: 'History unavailable.' }, 503));
    const value = (fixture.nativeElement as HTMLElement).querySelector(
      '.dashboard-page__value',
    ) as HTMLElement;

    expect(value.textContent).toContain(formatMoney(15000));
    const dash = value.querySelector('.dashboard-page__change') as HTMLElement;

    expect(textOf(dash)).toBe('—');
    expect(dash.classList).toContain('dashboard-page__change--neutral');
  });

  it('a failing card does not break the dashboard', async () => {
    await setup(jsonResponse(dashboard), historyWithChange(250, 25), {
      bonds: jsonResponse({ detail: 'Bonds unavailable.' }, 503),
      accounts: jsonResponse([accountResponse({ name: 'Retirement' })]),
      cash: jsonResponse(cashPlacesResponse([{ currency: 'PLN', balance: 1000 }])),
    });
    const element = fixture.nativeElement as HTMLElement;
    const upcoming = element.querySelector('app-upcoming-card') as HTMLElement;

    expect(upcoming.textContent).toContain('Bonds unavailable.');
    expect(upcoming.querySelector('button')).not.toBeNull();
    expect(element.querySelector('.dashboard-page__value')).not.toBeNull();
    expect(element.querySelector('app-split-bar')).not.toBeNull();
    expect(element.querySelector('app-net-worth-chart')).not.toBeNull();
    expect(element.querySelector('app-accounts-card')?.textContent).toContain('Retirement');
    expect(element.querySelector('app-cash-card')?.textContent).toContain(formatMoney(1000, 'PLN'));
  });

  it('the cash and accounts cards link to wealth addresses', async () => {
    await setup(jsonResponse(dashboard), historyWithChange(250, 25), {
      accounts: jsonResponse([accountResponse({ name: 'Retirement' })]),
      cash: jsonResponse(cashPlacesResponse([{ currency: 'PLN', balance: 1000 }])),
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('app-cash-card a[href="/wealth/cash"]')).not.toBeNull();
    const accountHrefs = Array.from(
      element.querySelectorAll('app-accounts-card a'),
      (a) => a.getAttribute('href') ?? '',
    );
    expect(accountHrefs.some((href) => href.startsWith('/wealth/accounts/'))).toBe(true);
    expect(accountHrefs.every((href) => !/^\/(accounts|bank-accounts)\b/.test(href))).toBe(true);
  });

  it('lists overdue income with Rozlicz and upcoming payments', async () => {
    await setup(jsonResponse(dashboard), undefined, {
      todo: jsonResponse({
        paymentLeadDays: 7,
        items: [
          {
            kind: 'OverdueIncome',
            name: 'Premia kwartalna',
            date: dateOnlyFromToday(-23),
            amount: 1800,
            currency: 'PLN',
            daysOverdue: 23,
            daysUntil: null,
          },
          {
            kind: 'UpcomingPayment',
            name: 'Ubezpieczenie OC',
            date: dateOnlyFromToday(5),
            amount: 1200,
            currency: 'PLN',
            daysOverdue: null,
            daysUntil: 5,
          },
        ],
      }),
    });
    await switchLanguage(fixture, 'pl');
    const element = fixture.nativeElement as HTMLElement;
    const rowOf = (name: string): HTMLElement => {
      const row = [...element.querySelectorAll<HTMLElement>('li')]
        .filter((candidate) => candidate.textContent?.includes(name))
        .at(-1);
      if (!row) {
        throw new Error(`No todo row for "${name}".`);
      }
      return row;
    };

    const income = rowOf('Premia kwartalna');
    expect(normalised(textOf(income))).toContain('zaległe: 23 dni');
    const settle = [...income.querySelectorAll<HTMLAnchorElement>('a')].find(
      (link) => textOf(link) === 'Rozlicz',
    );
    expect(settle).toBeDefined();
    expect(settle?.getAttribute('href')).toContain('/planning');
    expect(settle?.getAttribute('href')).toContain('#do-rozliczenia');

    const payment = rowOf('Ubezpieczenie OC');
    expect(normalised(textOf(payment))).toContain('za 5 dni · wydatek');
    expect(normalised(textOf(payment))).not.toContain('Rozlicz');
    expect(payment.querySelector('button')).toBeNull();
  });

  it('puts Do zrobienia under the KPI and above the chart when an income is overdue', async () => {
    await setup(jsonResponse(dashboardWithClasses), undefined, {
      todo: todoResponse([overdueIncomeItem('Premia kwartalna', 8)]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const { kpi, todo, month, chart, structure } = sectionsOf(element);

    expect(element.querySelectorAll('.dashboard-page__todo')).toHaveLength(1);
    expect(followsInDocument(kpi, todo)).toBe(true);
    expect(followsInDocument(todo, month)).toBe(true);
    expect(followsInDocument(month, chart)).toBe(true);
    expect(followsInDocument(month, structure)).toBe(true);
  });

  it('puts Do zrobienia under the KPI when an expense is overdue', async () => {
    await setup(jsonResponse(dashboardWithClasses), undefined, {
      todo: todoResponse([overdueExpenseItem('Abonament', 2)]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const { kpi, todo, month, chart, structure } = sectionsOf(element);

    expect(element.querySelectorAll('.dashboard-page__todo')).toHaveLength(1);
    expect(followsInDocument(kpi, todo)).toBe(true);
    expect(followsInDocument(todo, month)).toBe(true);
    expect(followsInDocument(month, chart)).toBe(true);
    expect(followsInDocument(month, structure)).toBe(true);
  });

  it('keeps Do zrobienia in its place without overdue rows', async () => {
    await setup(jsonResponse(dashboardWithClasses), undefined, {
      todo: todoResponse([upcomingPaymentItem('Ubezpieczenie OC', 5)]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const { hero, structure, todo, month } = sectionsOf(element);

    expect(element.querySelectorAll('.dashboard-page__todo')).toHaveLength(1);
    expect(hero.contains(todo)).toBe(false);
    expect(followsInDocument(structure, todo)).toBe(true);
    expect(followsInDocument(todo, month)).toBe(true);
  });

  it('keeps Do zrobienia in its place while the todo list loads', async () => {
    let answerTodo!: (response: Response) => void;
    const pendingTodo = new Promise<Response>((resolve) => (answerTodo = resolve));
    fetchSpy = mockApi({ dashboard: jsonResponse(dashboardWithClasses), todo: () => pendingTodo });
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(Dashboard);
    const element = fixture.nativeElement as HTMLElement;

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element.querySelector('app-todo-card mat-spinner')).not.toBeNull();
    });
    const { hero, structure, todo } = sectionsOf(element);

    expect(hero.contains(todo)).toBe(false);
    expect(followsInDocument(structure, todo)).toBe(true);
    answerTodo(todoResponse([]));
  });

  it('stacks the overdue section after the KPI on a narrow container', async () => {
    await setup(jsonResponse(dashboardWithClasses), undefined, {
      todo: todoResponse([overdueIncomeItem('Premia kwartalna', 8)]),
    });
    const element = fixture.nativeElement as HTMLElement;
    const { kpi, history, structure, todo } = sectionsOf(element);

    expect(followsInDocument(kpi, todo)).toBe(true);
    expect(followsInDocument(todo, history)).toBe(true);
    expect(followsInDocument(todo, structure)).toBe(true);

    // jsdom cannot evaluate container queries, so read the component's compiled stylesheet.
    const css =
      [...document.querySelectorAll('style')]
        .map((style) => style.textContent ?? '')
        .find((text) => text.includes('.dashboard-page__hero')) ?? '';
    const split = css.search(COMPILED_HERO_CONTAINER);
    expect(split).toBeGreaterThan(-1);
    expect(css.slice(0, split)).not.toMatch(/\d+fr\s+\d+fr/);
  });

  describe('settling an overdue expense', () => {
    const expenseTodo = jsonResponse({
      paymentLeadDays: 7,
      items: [
        {
          kind: 'OverdueExpense',
          name: 'Abonament',
          date: dateOnlyFromToday(-4),
          amount: 120,
          currency: 'PLN',
          daysOverdue: 4,
          daysUntil: null,
          oneOffExpense: {
            id: 'one-off-abonament',
            name: 'Abonament',
            date: dateOnlyFromToday(-4),
            amount: 120,
            inMonth: true,
            status: 'Pending',
          },
        },
      ],
    });

    function requestsTo(path: string): number {
      return fetchSpy.mock.calls.filter(([input]: unknown[]) => requestUrl(input).includes(path))
        .length;
    }

    async function clickSettle(saved: boolean, beforeClick?: () => void): Promise<void> {
      vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
        afterClosed: () => of(saved),
      } as never);
      await switchLanguage(fixture, 'pl');
      const button = [
        ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
          '[data-testid="todo-card-item"] button',
        ),
      ].find((candidate) => textOf(candidate) === 'Rozlicz');
      beforeClick?.();
      button!.click();
      await fixture.whenStable();
    }

    it('reloads the page after settling an expense', async () => {
      await setup(jsonResponse(dashboard), undefined, { todo: expenseTodo });
      const before = {
        todo: requestsTo('/planning/todo'),
        dashboard: requestsTo('/api/reporting/dashboard'),
        history: requestsTo('/net-worth-history'),
      };

      await clickSettle(true);

      await vi.waitFor(() => {
        expect(requestsTo('/planning/todo')).toBeGreaterThan(before.todo);
        expect(requestsTo('/api/reporting/dashboard')).toBeGreaterThan(before.dashboard);
        expect(requestsTo('/net-worth-history')).toBeGreaterThan(before.history);
      });
    });

    it('does not reload when the settle dialog is cancelled', async () => {
      await setup(jsonResponse(dashboard), undefined, { todo: expenseTodo });
      const before = fetchSpy.mock.calls.length;

      await clickSettle(false);
      await fixture.whenStable();

      expect(fetchSpy.mock.calls).toHaveLength(before);
    });

    it('returns Do zrobienia to its place after the last overdue row is settled', async () => {
      let overdueRows = [overdueExpenseItem('Abonament', 4)];
      await setup(jsonResponse(dashboardWithClasses), undefined, {
        todo: () => todoResponse(overdueRows),
      });
      const element = fixture.nativeElement as HTMLElement;
      const before = sectionsOf(element);

      expect(followsInDocument(before.kpi, before.todo)).toBe(true);
      expect(followsInDocument(before.todo, before.chart)).toBe(true);

      await clickSettle(true, () => {
        overdueRows = [];
      });

      await vi.waitFor(() => {
        fixture.detectChanges();
        const after = sectionsOf(element);
        expect(followsInDocument(after.structure, after.todo)).toBe(true);
        expect(element.querySelector('.todo-card')?.classList).not.toContain('todo-card--overdue');
      });
    });
  });

  it('a failed section does not replace the page', async () => {
    await setup(jsonResponse(dashboard), historyWithChange(250, 25), {
      todo: jsonResponse({ detail: 'Todo unavailable.' }, 500),
      planningMonth: jsonResponse({ detail: 'Month unavailable.' }, 500),
      groups: jsonResponse({ detail: 'Groups unavailable.' }, 500),
    });
    const element = fixture.nativeElement as HTMLElement;
    const alerts = [...element.querySelectorAll<HTMLElement>('[role="alert"]')];

    expect(alerts).toHaveLength(3);
    expect(alerts.every((alert) => alert.querySelector('button') !== null)).toBe(true);
    expect(element.textContent).toContain('Todo unavailable.');
    expect(element.textContent).toContain('Month unavailable.');
    expect(element.textContent).toContain('Groups unavailable.');
    expect(element.querySelector('.dashboard-page__value')).not.toBeNull();
    expect(element.querySelector('app-net-worth-chart')).not.toBeNull();
    expect(element.querySelector('app-split-bar')).not.toBeNull();
  });

  describe('groups', () => {
    function groupRows(element: HTMLElement): HTMLElement[] {
      return [...element.querySelectorAll<HTMLElement>('[data-testid="group-row"]')];
    }

    function groupRow(element: HTMLElement, name: string): HTMLElement {
      const row = groupRows(element).find((candidate) => textOf(candidate).includes(name));
      if (!row) {
        throw new Error(`No group row for "${name}".`);
      }
      return row;
    }

    function progressOf(row: HTMLElement): string | null | undefined {
      return row.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow');
    }

    it('renders group rows with progress and the cushion', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      await switchLanguage(fixture, 'pl');
      const element = fixture.nativeElement as HTMLElement;

      expect(groupRows(element).map((row) => textOf(row).split(' ')[0])).toEqual([
        'Emerytura',
        'Inwestycje',
        'Poduszka',
        'Bez',
      ]);

      const retirement = groupRow(element, 'Emerytura');
      expect(
        retirement.querySelector('[data-testid="group-dot"]')?.getAttribute('style') ?? '',
      ).toContain('var(--sk-g-blue)');
      expect(retirement.querySelector('a[href="/groups"]')).not.toBeNull();
      expect(normalised(textOf(retirement))).toContain(normalised(formatMoney(75000, 'PLN')));
      expect(progressOf(retirement)).toBeUndefined();

      const cushion = groupRow(element, 'Poduszka');
      expect(normalised(textOf(cushion))).toMatch(/Poduszka.*Poduszka/);
      expect(progressOf(cushion)).toBe('93');
      expect(normalised(textOf(cushion))).toContain(
        normalised(
          `${formatMoney(28000, 'PLN')} z ${formatMoney(30000, 'PLN')}, brakuje ${formatMoney(2000, 'PLN')}`,
        ),
      );

      const ungrouped = groupRows(element).at(-1) as HTMLElement;
      expect(textOf(ungrouped)).toContain('Bez grupy');
      expect(progressOf(ungrouped)).toBeUndefined();
      expect(normalised(textOf(ungrouped))).toMatch(/%\s*majątku/);
    });

    function visibleRows(element: HTMLElement): HTMLElement[] {
      return groupRows(element).filter((row) => row.closest('[inert]') === null);
    }

    function buttonWith(element: HTMLElement, text: string): HTMLButtonElement | undefined {
      return [...element.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
        textOf(button).includes(text),
      );
    }

    async function click(button: HTMLElement): Promise<void> {
      button.click();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    async function setupMany(width: number | null, count = 8): Promise<HTMLElement> {
      const observers = stubResizeObserver();
      const many = manyGroups(count);
      await setup(jsonResponse(many.dashboard), undefined, { groups: jsonResponse(many.groups) });
      await switchLanguage(fixture, 'pl');
      if (width !== null) {
        resizeDashboardTo(observers, width);
        await fixture.whenStable();
        fixture.detectChanges();
      }
      return fixture.nativeElement as HTMLElement;
    }

    const NAMES_BY_VALUE = [
      'Grupa H',
      'Grupa G',
      'Grupa F',
      'Grupa E',
      'Grupa D',
      'Grupa C',
      'Grupa B',
      'Grupa A',
    ];

    it('orders groups by value', async () => {
      await setup(
        jsonResponse(
          dashboardWithGroups([
            { groupId: RETIREMENT_GROUP_ID, valuePln: 75000, percentage: 40 },
            { groupId: INVESTMENTS_GROUP_ID, valuePln: 28000, percentage: 15 },
            { groupId: CUSHION_GROUP_ID, valuePln: 28000, percentage: 15 },
            { groupId: null, valuePln: 50000, percentage: 30 },
          ]),
        ),
        undefined,
        { groups: jsonResponse(groups) },
      );
      await switchLanguage(fixture, 'pl');

      expect(
        groupRows(fixture.nativeElement as HTMLElement).map((row) => textOf(row).split(' ')[0]),
      ).toEqual(['Emerytura', 'Bez', 'Inwestycje', 'Poduszka']);
    });

    it('narrow groups cap at five and expand', async () => {
      const element = await setupMany(600);

      expect(visibleRows(element).map((row) => textOf(row).slice(0, 7))).toEqual(
        NAMES_BY_VALUE.slice(0, 5),
      );
      const button = buttonWith(element, 'Pokaż wszystkie (8)');
      expect(button).toBeDefined();
      expect(button?.getAttribute('aria-expanded')).toBe('false');

      await click(button as HTMLElement);
      expect(groupRows(element).map((row) => textOf(row).slice(0, 7))).toEqual(NAMES_BY_VALUE);
      expect(visibleRows(element)).toHaveLength(8);
      const folded = buttonWith(element, 'Zwiń');
      expect(folded?.getAttribute('aria-expanded')).toBe('true');

      await click(folded as HTMLElement);
      expect(visibleRows(element)).toHaveLength(5);
      expect(buttonWith(element, 'Pokaż wszystkie (8)')).toBeDefined();
    });

    it('no group button when there is nothing to fold', async () => {
      const fiveNarrow = await setupMany(600, 5);
      expect(visibleRows(fiveNarrow)).toHaveLength(5);
      expect(buttonWith(fiveNarrow, 'Pokaż wszystkie')).toBeUndefined();
      expect(buttonWith(fiveNarrow, 'Zwiń')).toBeUndefined();

      fixture.destroy();
      TestBed.resetTestingModule();
      fetchSpy.mockRestore();
      const eightWide = await setupMany(900, 8);
      expect(visibleRows(eightWide).map((row) => textOf(row).slice(0, 7))).toEqual(NAMES_BY_VALUE);
      expect(buttonWith(eightWide, 'Pokaż wszystkie')).toBeUndefined();
    });

    it('narrow structure folds its class list', async () => {
      const element = await setupMany(600, 3);
      const list = element.querySelector<HTMLElement>('.dashboard-page__structure-list');

      expect(element.querySelector('app-split-bar')).not.toBeNull();
      expect(list?.closest('[inert]')).not.toBeNull();
      const open = buttonWith(element, 'Pokaż szczegóły');
      expect(open).toBeDefined();

      await click(open as HTMLElement);
      expect(list?.closest('[inert]')).toBeNull();
      expect(buttonWith(element, 'Ukryj szczegóły')?.getAttribute('aria-expanded')).toBe('true');

      fixture.destroy();
      TestBed.resetTestingModule();
      fetchSpy.mockRestore();
      const wide = await setupMany(900, 3);
      expect(
        wide.querySelector<HTMLElement>('.dashboard-page__structure-list')?.closest('[inert]'),
      ).toBeNull();
      expect(buttonWith(wide, 'Pokaż szczegóły')).toBeUndefined();
      expect(buttonWith(wide, 'Ukryj szczegóły')).toBeUndefined();
    });

    it('folded dashboard content is out of the tab order', async () => {
      const element = await setupMany(600);

      const folded = groupRows(element).filter((row) => row.closest('[inert]') !== null);
      expect(folded).toHaveLength(3);
      const structureList = element.querySelector<HTMLElement>('.dashboard-page__structure-list');
      expect(structureList?.closest('[inert]')).not.toBeNull();

      for (const text of ['Pokaż wszystkie (8)', 'Pokaż szczegóły']) {
        const button = buttonWith(element, text);
        expect(button?.getAttribute('aria-expanded')).toBe('false');
        const controlled = button?.getAttribute('aria-controls');
        expect(controlled).toBeTruthy();
        expect(element.querySelector(`#${controlled}`)).not.toBeNull();
      }
    });

    it('expand state is not remembered', async () => {
      const observers = stubResizeObserver();
      const many = manyGroups(8);
      await setup(jsonResponse(many.dashboard), undefined, { groups: jsonResponse(many.groups) });
      await switchLanguage(fixture, 'pl');
      resizeDashboardTo(observers, 600);
      await fixture.whenStable();
      fixture.detectChanges();
      const first = fixture.nativeElement as HTMLElement;
      await click(buttonWith(first, 'Pokaż wszystkie (8)') as HTMLElement);
      await click(buttonWith(first, 'Pokaż szczegóły') as HTMLElement);
      expect(visibleRows(first)).toHaveLength(8);

      fixture.destroy();
      fixture = TestBed.createComponent(Dashboard);
      await fixture.whenStable();
      resizeDashboardTo(observers, 600);
      await fixture.whenStable();
      fixture.detectChanges();
      const second = fixture.nativeElement as HTMLElement;

      expect(visibleRows(second)).toHaveLength(5);
      expect(buttonWith(second, 'Pokaż wszystkie (8)')).toBeDefined();
      expect(buttonWith(second, 'Pokaż szczegóły')).toBeDefined();
    });

    it('flags class shares that drift from the allocation', async () => {
      const allocatedGroups = [
        {
          ...groups[0],
          monthlyContributionPln: 2000,
          classAllocation: [
            { assetClass: 3, percent: 70 },
            { assetClass: 2, percent: 20 },
            { assetClass: 4, percent: 10 },
          ],
        },
        groups[1],
        groups[2],
      ];
      await setup(
        jsonResponse(
          dashboardWithGroups([
            {
              groupId: RETIREMENT_GROUP_ID,
              valuePln: 10000,
              percentage: 100,
              byAssetClass: [
                { assetClass: 3, valuePln: 6200 },
                { assetClass: 2, valuePln: 2400 },
                { assetClass: 4, valuePln: 1400 },
              ],
            },
          ] as DashboardResponse['byGroup']),
        ),
        undefined,
        { groups: jsonResponse(allocatedGroups) },
      );
      await switchLanguage(fixture, 'pl');
      const text = normalised(textOf(groupRow(fixture.nativeElement as HTMLElement, 'Emerytura')));

      expect(text).toMatch(/ETF: 62\s?% \(cel 70\s?%\)/);
      expect(text).not.toMatch(/\(cel 20\s?%\)/);
      expect(text).not.toMatch(/\(cel 10\s?%\)/);
    });

    it('shows the groups hint without groups', async () => {
      await setup(
        jsonResponse(dashboardWithGroups([{ groupId: null, valuePln: 15000, percentage: 100 }])),
      );
      const element = fixture.nativeElement as HTMLElement;

      expect(groupRows(element).filter((row) => textOf(row).includes('Emerytura'))).toHaveLength(0);
      expect(element.querySelector('.dashboard-page__group-card a[href="/groups"]')).not.toBeNull();
    });

    it('caps the bar at 100', async () => {
      await setup(
        jsonResponse(
          dashboardWithGroups([{ groupId: CUSHION_GROUP_ID, valuePln: 33000, percentage: 100 }]),
        ),
        undefined,
        { groups: jsonResponse(groups) },
      );
      const element = fixture.nativeElement as HTMLElement;

      expect(progressOf(groupRow(element, 'Poduszka'))).toBe('100');
    });

    it('treats an unknown group as ungrouped', async () => {
      await setup(
        jsonResponse(
          dashboardWithGroups([
            { groupId: RETIREMENT_GROUP_ID, valuePln: 75000, percentage: 96.2 },
            { groupId: 'deadbeef-dead-beef-dead-beefdeadbeef', valuePln: 3000, percentage: 3.8 },
          ]),
        ),
        undefined,
        { groups: jsonResponse(groups) },
      );
      await switchLanguage(fixture, 'pl');
      const element = fixture.nativeElement as HTMLElement;

      expect(normalised(textOf(groupRow(element, 'Bez grupy')))).toMatch(/3\s?000/);
    });

    it('sends the filter from the menu', async () => {
      await setup(
        jsonResponse({
          ...dashboardWithGroups(),
          byAssetClass: dashboardWithClasses.byAssetClass,
        }),
        undefined,
        { groups: jsonResponse(groups) },
      );
      const element = fixture.nativeElement as HTMLElement;
      const filterButton = () => textOf(element.querySelector('.net-worth-chart__filter'));
      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);
      expect(lastHistoryParams(fetchSpy).has('assetClass')).toBe(false);

      menuItem(await openFilterMenu(fixture), 'ETF').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('assetClass')).toBe('3');
      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);
      expect(filterButton()).toContain('ETF');

      menuItem(await openFilterMenu(fixture), 'Emerytura').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('groupId')).toBe(RETIREMENT_GROUP_ID);
      expect(lastHistoryParams(fetchSpy).has('assetClass')).toBe(false);
      expect(filterButton()).toContain('Emerytura');

      menuItem(await openFilterMenu(fixture), 'No group').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('ungrouped')).toBe('true');
      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);

      menuItem(await openFilterMenu(fixture), 'All').click();
      await fixture.whenStable();
      const cleared = lastHistoryParams(fetchSpy);
      expect(cleared.has('assetClass')).toBe(false);
      expect(cleared.has('groupId')).toBe(false);
      expect(cleared.has('ungrouped')).toBe(false);
      expect(filterButton()).toContain('All');
    });

    it('group rows are links and do not filter', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      const element = fixture.nativeElement as HTMLElement;
      const before = fetchSpy.mock.calls.length;

      expect(groupRows(element).length).toBeGreaterThan(0);
      for (const row of groupRows(element)) {
        row.click();
      }
      await fixture.whenStable();

      const params = lastHistoryParams(fetchSpy);
      expect(params.has('groupId')).toBe(false);
      expect(params.has('ungrouped')).toBe(false);
      expect(
        fetchSpy.mock.calls
          .slice(before)
          .some(([input]: [unknown]) => requestUrl(input).includes('/net-worth-history')),
      ).toBe(false);
      expect(textOf(element.querySelector('.net-worth-chart__filter'))).toContain('All');
    });

    it('resets a deleted group filter', async () => {
      let current = groups;
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: () => jsonResponse(current),
      });
      const element = fixture.nativeElement as HTMLElement;

      menuItem(await openFilterMenu(fixture), 'Emerytura').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('groupId')).toBe(RETIREMENT_GROUP_ID);

      current = groups.filter((group) => group.id !== RETIREMENT_GROUP_ID);
      component['groupsResource'].reload();
      await fixture.whenStable();

      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);
      expect(textOf(element.querySelector('.net-worth-chart__filter'))).toContain('All');
    });
  });
});

describe('dashboard stylesheet', () => {
  it('hero switches on the container breakpoint', () => {
    const scss = dashboardStylesheet();

    expect(scss).toMatch(HERO_CONTAINER_MIXIN);
    expect(scss).not.toMatch(/@container[^{]*\d+px/);
    expect(ruleBodies(scss, '.dashboard-page')).toMatch(/container-type:\s*inline-size/);
  });

  it('rules rows 1 and 2 only', () => {
    const scss = dashboardStylesheet();
    const separator = (body: string) =>
      /border-bottom:\s*1px solid var\(--mat-sys-outline-variant\)/.test(body) &&
      /padding-bottom:\s*var\(--sk-space-9\)/.test(body);
    const pairRules = [
      ruleBodies(scss, '.dashboard-page__pair'),
      ruleBodies(scss, '.dashboard-page__pair:not(:last-child)'),
    ].join('\n');

    expect(separator(ruleBodies(scss, '.dashboard-page__hero'))).toBe(true);
    expect(separator(pairRules)).toBe(true);
    expect(
      scss.includes('.dashboard-page__pair:not(:last-child)') ||
        /border-bottom:\s*(none|0)\b/.test(ruleBodies(scss, '.dashboard-page__pair--split')),
    ).toBe(true);
    expect(ruleBodies(scss, '.dashboard-page__structure')).toMatch(/border-top:/);
  });

  it('colours the change with the sign tokens', () => {
    const scss = dashboardStylesheet();
    const ruleOf = (selector: string): string =>
      scss.match(new RegExp(`\\.${selector}\\s*\\{([^}]*)\\}`))?.[1] ?? '';
    const selectors = [
      'dashboard-page__change--positive',
      'dashboard-page__change--negative',
      'dashboard-page__change--neutral',
    ];

    expect(ruleOf('dashboard-page__change--positive')).toMatch(/color:\s*var\(--sk-positive\)/);
    expect(ruleOf('dashboard-page__change--negative')).toMatch(/color:\s*var\(--sk-negative\)/);
    expect(ruleOf('dashboard-page__change--neutral')).toMatch(
      /color:\s*var\(--mat-sys-on-surface-variant\)/,
    );
    for (const selector of selectors) {
      expect(ruleOf(selector)).not.toMatch(/#[0-9a-fA-F]{3,6}\b|rgb\(/);
    }
  });

  it('leaves the "Do zrobienia" heading size to the section title class', () => {
    const html = readFileSync(
      resolve(process.cwd(), 'src/app/features/dashboard/dashboard.html'),
      'utf8',
    );

    expect(html).toMatch(/<h2 class="sk-section-title">\{\{ 'dashboard\.todo\.heading'/);
    expect(dashboardStylesheet()).not.toMatch(/\.dashboard-page__todo-top\s*\{[^}]*font-size/);
  });

  it('splits every row 50 50 on a wide container', () => {
    const scss = dashboardStylesheet();
    const split = scss.search(HERO_CONTAINER_MIXIN);
    const base = scss.slice(0, split);
    const wide = scss.slice(split);
    const equalHalves = /repeat\(2,\s*minmax\(0,\s*1fr\)\)|minmax\(0,\s*1fr\)\s+minmax\(0,\s*1fr\)/;

    expect(split).toBeGreaterThan(-1);
    expect(ruleBodies(wide, '.dashboard-page__hero')).toMatch(equalHalves);
    expect(ruleBodies(wide, '.dashboard-page__pair')).toMatch(equalHalves);
    const splitRow = ruleBodies(wide, '.dashboard-page__pair--split');
    expect(splitRow === '' || equalHalves.test(splitRow)).toBe(true);
    expect(scss).not.toMatch(/\b[57]fr\b/);
    expect(wide).toMatch(
      /\.dashboard-page__hero\s*\{[^}]*grid-template-areas:\s*["']value history["']\s*["']structure history["']/,
    );
    expect(wide).toMatch(/\.dashboard-page__hero\s*\{[^}]*row-gap:\s*var\(--sk-space-9\)/);
    expect(scss).toMatch(/\.dashboard-page__value\s*\{[^}]*grid-area:\s*value/);
    expect(scss).toMatch(/\.dashboard-page__structure\s*\{[^}]*grid-area:\s*structure/);
    expect(scss).toMatch(/\.dashboard-page__history\s*\{[^}]*grid-area:\s*history/);
    expect(base).not.toMatch(/grid-template-areas:\s*"[^"]*"\s*"/);
  });

  it('stretches the halves to equal height', () => {
    const scss = dashboardStylesheet();
    const split = scss.search(HERO_CONTAINER_MIXIN);
    const wide = scss.slice(split);

    expect(ruleBodies(scss, '.dashboard-page__history')).not.toMatch(/align-self:\s*start/);
    expect(ruleBodies(scss, '.dashboard-page__side')).not.toMatch(/align-self:\s*start/);
    expect(ruleBodies(wide, '.dashboard-page__hero')).toMatch(/grid-template-rows:\s*auto\s+1fr/);
    expect(ruleBodies(scss, '.dashboard-page__side')).toMatch(/grid-template-rows:\s*auto\s+1fr/);
  });

  it('lets the history section grow', () => {
    const rule = [...dashboardStylesheet().matchAll(/\.dashboard-page__history\s*\{([^}]*)\}/g)]
      .map((match) => match[1])
      .join(' ');

    expect(rule).toMatch(/display:\s*flex/);
    expect(rule).toMatch(/flex-direction:\s*column/);
  });

  it('lets the structure rows wrap', () => {
    const structureRules = [
      ...dashboardStylesheet().matchAll(/\.dashboard-page__structure[^{]*\{([^}]*)\}/g),
    ]
      .map((match) => match[1])
      .join('\n');

    expect(structureRules).toMatch(/min-width:\s*0/);
    expect(structureRules).toMatch(/overflow-wrap:\s*anywhere/);
  });

  it('drops the last row line of Grupy and Struktura', () => {
    const scss = dashboardStylesheet();

    expect(rowLineSkipsLast(scss, '.dashboard-page__group-row')).toBe(true);
    expect(rowLineSkipsLast(scss, '.dashboard-page__structure-row')).toBe(true);
    expect(ruleBodies(scss, '.dashboard-page__group-card')).not.toMatch(/padding-top/);
  });

  it('never clips a number in Grupy or Struktura', () => {
    const scss = dashboardStylesheet();

    expect(scss).not.toMatch(/text-overflow:\s*ellipsis/);
    expect(scss).not.toMatch(/overflow(-x|-y)?:\s*(hidden|clip)\b/);
    for (const row of ['.dashboard-page__group-row', '.dashboard-page__structure-row']) {
      expect(ruleBodies(scss, row), row).toMatch(
        /grid-template-columns:\s*auto\s+minmax\(0,\s*1fr\)\s+(auto|max-content)\s+(auto|max-content)/,
      );
    }
    for (const cell of [
      '.dashboard-page__group-value',
      '.dashboard-page__group-share',
      '.dashboard-page__structure-value',
      '.dashboard-page__structure-share',
    ]) {
      expect(ruleBodies(scss, cell), cell).not.toMatch(/max-width|min-width:\s*0\b|overflow/);
    }
  });

  it('styles the dashboard only with tokens', () => {
    const dir = resolve(process.cwd(), 'src/app/features/dashboard');
    const styles = readdirSync(dir, { recursive: true })
      .map(String)
      .filter((file) => file.endsWith('.scss'))
      .map((file) => readFileSync(resolve(dir, file), 'utf8'))
      .join('\n');
    const scss = dashboardStylesheet();

    expect(styles).not.toMatch(/#[0-9a-fA-F]{3,6}\b|rgb\(/);
    expect(scss).toMatch(/var\(--mat-sys-outline-variant\)/);
    expect(scss).toMatch(/var\(--sk-space-9\)/);
  });
});

describe('dashboard.groups translations', () => {
  it('has every dashboard.groups key in both languages', () => {
    const keysOf = (lang: 'en' | 'pl') =>
      Object.keys(TRANSLATIONS[lang])
        .filter((key) => key.startsWith('dashboard.groups.'))
        .sort();

    expect(keysOf('en')).toEqual(
      expect.arrayContaining([
        'dashboard.groups.title',
        'dashboard.groups.ungrouped',
        'dashboard.groups.hint',
        'dashboard.groups.link',
        'dashboard.groups.cushionTag',
        'dashboard.groups.classShare',
      ]),
    );
    expect(keysOf('pl')).toEqual(keysOf('en'));
  });
});
