import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
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
import { formatDate, formatMoney, formatPercent, formatSignedMoney } from '../../shared/format';
import {
  type ApiRoute,
  type Responder,
  cashPlacesResponse,
  dashboard,
  dashboardWithClasses,
  dashboardWithGroups,
  groups,
  RETIREMENT_GROUP_ID,
  INVESTMENTS_GROUP_ID,
  CUSHION_GROUP_ID,
  jsonResponse,
  lastHistoryParams,
  menuItem,
  openFilterMenu,
  mockApi,
  accountResponse,
  requestUrl,
} from './testing/dashboard-fixtures';

function normalised(text: string): string {
  return text.replace(/\s+/g, ' ');
}

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
        { date: '2026-07-01', netWorthPln: 1000 },
        { date: '2026-08-01', netWorthPln: 1250 },
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
    expect(change.textContent).toContain(formatPercent(25));
    expect(change.classList).toContain('dashboard-page__change--positive');
    expect(change.classList).not.toContain('dashboard-page__change--negative');

    await switchLanguage(fixture, 'pl');

    expect(normalised(change.textContent ?? '')).toContain('zmiana za YTD');
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
    expect(structure.querySelector('app-pie-chart')).toBeNull();
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

  it('stacks the sections on a narrow container', async () => {
    await setup(jsonResponse(dashboardWithGroups()), undefined, { groups: jsonResponse(groups) });
    const element = fixture.nativeElement as HTMLElement;

    const order = [
      '.dashboard-page__hero',
      'app-month-card',
      'app-upcoming-card',
      '.dashboard-page__group-card',
      '.dashboard-page__structure',
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
    const split = css.indexOf('@container (min-width: 720px)');
    expect(split).toBeGreaterThan(-1);
    expect(css.slice(split)).toMatch(/\.dashboard-page__hero[^{]*\{[^}]*5fr/);
    expect(css.slice(0, split)).not.toMatch(/\.dashboard-page__hero[^{]*\{[^}]*5fr/);
  });

  it('styles a negative change as negative', async () => {
    await setup(jsonResponse(dashboard), historyWithChange(-250, -20));

    const change = (fixture.nativeElement as HTMLElement).querySelector(
      '.dashboard-page__change',
    ) as HTMLElement;

    expect(change.classList).toContain('dashboard-page__change--negative');
    expect(change.classList).not.toContain('dashboard-page__change--positive');
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
    expect(value.querySelector('.dashboard-page__as-of')).not.toBeNull();
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
    expect(textOf(value.querySelector('.dashboard-page__change'))).toBe('—');
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

  describe('groups', () => {
    const groupCard = '.dashboard-page__group-card';

    function groupCircles(element: HTMLElement): SVGElement[] {
      return [...element.querySelectorAll<SVGElement>(`${groupCard} app-pie-chart circle`)];
    }

    function groupLegend(element: HTMLElement): HTMLElement[] {
      return [
        ...element.querySelectorAll<HTMLElement>(
          '.dashboard-page__group-legend .dashboard-page__legend-item',
        ),
      ];
    }

    function groupLegendRow(element: HTMLElement, name: string): HTMLElement {
      const row = groupLegend(element).find((button) => textOf(button).includes(name));
      if (!row) {
        throw new Error(`No group legend row for "${name}".`);
      }
      return row;
    }

    function clickSvg(target: SVGElement): void {
      target.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    }

    function progressOf(row: HTMLElement): string | null | undefined {
      return row.parentElement?.querySelector('mat-progress-bar')?.getAttribute('aria-valuenow');
    }

    it('renders the group pie', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      const element = fixture.nativeElement as HTMLElement;

      expect(element.querySelectorAll('app-pie-chart')).toHaveLength(1);
      const strokes = groupCircles(element).map((circle) => circle.getAttribute('style') ?? '');
      expect(strokes).toHaveLength(4);
      ['var(--sk-g-blue)', 'var(--sk-g-green)', 'var(--sk-g-orange)', 'var(--sk-g-none)'].forEach(
        (token, i) => expect(strokes[i]).toContain(token),
      );
      expect(groupLegend(element).map((button) => textOf(button).split(' ')[0])).toEqual([
        'Emerytura',
        'Inwestycje',
        'Poduszka',
        'No',
      ]);
    });

    it('shows the groups hint without groups', async () => {
      await setup(
        jsonResponse(dashboardWithGroups([{ groupId: null, valuePln: 15000, percentage: 100 }])),
      );
      const element = fixture.nativeElement as HTMLElement;

      expect(groupCircles(element)).toHaveLength(0);
      expect(element.querySelector(`${groupCard} a[href="/groups"]`)).not.toBeNull();
    });

    it('hides non-positive groups from the pie only', async () => {
      await setup(
        jsonResponse(
          dashboardWithGroups([
            { groupId: RETIREMENT_GROUP_ID, valuePln: 75000, percentage: 100 },
            { groupId: INVESTMENTS_GROUP_ID, valuePln: 0, percentage: 0 },
            { groupId: CUSHION_GROUP_ID, valuePln: -500, percentage: -0.5 },
          ]),
        ),
        undefined,
        { groups: jsonResponse(groups) },
      );
      const element = fixture.nativeElement as HTMLElement;

      expect(groupCircles(element)).toHaveLength(1);
      const legend = groupLegend(element).map((button) => textOf(button));
      expect(legend.some((text) => text.includes('Inwestycje'))).toBe(true);
      expect(legend.some((text) => text.includes('Poduszka'))).toBe(true);
    });

    it('shows target progress', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      await switchLanguage(fixture, 'pl');
      const element = fixture.nativeElement as HTMLElement;

      const row = groupLegendRow(element, 'Poduszka');
      expect(normalised(textOf(row))).toMatch(
        /28\s000(,00)?\s*\/\s*30\s000(,00)?\s*zł\s*\(93\s?%\)/,
      );
      expect(progressOf(row)).toBe('93');
      expect(progressOf(groupLegendRow(element, 'Emerytura'))).toBeUndefined();
    });

    it('shows class shares against the allocation and the cushion level', async () => {
      const allocatedGroups = [
        {
          ...groups[0],
          kind: 0,
          monthlyContributionPln: 2000,
          classAllocation: [
            { assetClass: 1, percent: 50 },
            { assetClass: 9, percent: 50 },
          ],
        },
        groups[1],
        { ...groups[2], kind: 1, targetPln: 12000, monthlyContributionPln: 0, classAllocation: [] },
      ];
      await setup(
        jsonResponse(
          dashboardWithGroups([
            {
              groupId: RETIREMENT_GROUP_ID,
              valuePln: 10000,
              percentage: 48,
              byAssetClass: [
                { assetClass: 1, valuePln: 9000 },
                { assetClass: 9, valuePln: 1000 },
              ],
            },
            {
              groupId: CUSHION_GROUP_ID,
              valuePln: 10500,
              percentage: 52,
              byAssetClass: [{ assetClass: 9, valuePln: 10500 }],
            },
          ] as DashboardResponse['byGroup']),
        ),
        undefined,
        { groups: jsonResponse(allocatedGroups) },
      );
      const text = normalised(textOf(fixture.nativeElement as HTMLElement));

      expect(text).toMatch(/90\s?%/);
      expect(text).toMatch(/50\s?%/);
      expect(text).toMatch(/10,500/);
      expect(text).toMatch(/12,000/);
      expect(text).toMatch(/1,500/);
    });

    it('caps the bar at 100', async () => {
      await setup(
        jsonResponse(
          dashboardWithGroups([{ groupId: CUSHION_GROUP_ID, valuePln: 33000, percentage: 100 }]),
        ),
        undefined,
        { groups: jsonResponse(groups) },
      );
      await switchLanguage(fixture, 'pl');
      const element = fixture.nativeElement as HTMLElement;

      const row = groupLegendRow(element, 'Poduszka');
      expect(normalised(textOf(row))).toMatch(
        /33\s000(,00)?\s*\/\s*30\s000(,00)?\s*zł\s*\(110\s?%\)/,
      );
      expect(progressOf(row)).toBe('100');
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

      expect(normalised(textOf(groupLegendRow(element, 'Bez grupy')))).toMatch(/3\s?000/);
      expect(
        groupCircles(element).some((circle) =>
          (circle.getAttribute('style') ?? '').includes('var(--sk-g-none)'),
        ),
      ).toBe(true);
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

    it('group rows do not filter the history', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      const element = fixture.nativeElement as HTMLElement;
      const before = fetchSpy.mock.calls.length;

      for (const row of element.querySelectorAll<HTMLElement>(
        '.dashboard-page__group-legend .dashboard-page__legend-item, .dashboard-page__group-legend li',
      )) {
        row.click();
      }
      for (const circle of groupCircles(element)) {
        clickSvg(circle);
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
        'dashboard.groups.progress',
        'dashboard.groups.filter',
      ]),
    );
    expect(keysOf('pl')).toEqual(keysOf('en'));
  });
});
