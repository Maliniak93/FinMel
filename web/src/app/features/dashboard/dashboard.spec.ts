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
import { ASSET_CLASS, assetClassLabel } from '../assets/asset-class';
import { Dashboard } from './dashboard';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatDate, formatMoney, formatPercent } from '../../shared/format';
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

  it('builds pie segments from the asset-class breakdown', async () => {
    await setup(jsonResponse(dashboard));
    expect(component['pieSegments']()).toEqual([
      {
        key: String(ASSET_CLASS.Cash),
        label: assetClassLabel(ASSET_CLASS.Cash),
        percentage: 33.33,
        color: 'var(--sk-c-cash)',
      },
      {
        key: String(ASSET_CLASS.Stock),
        label: assetClassLabel(ASSET_CLASS.Stock),
        percentage: 66.67,
        color: 'var(--sk-c-stock)',
      },
    ]);
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

    const [cash, bank] = component['pieSegments']();

    expect(bank.label).toBe(assetClassLabel(ASSET_CLASS.BankAccount));
    expect(bank.color).toBeTruthy();
    expect(bank.color).not.toBe(cash.color);

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
    await setup(jsonResponse(dashboard), historyWithChange(250, 25));

    const change = (fixture.nativeElement as HTMLElement).querySelector(
      '.dashboard-page__change',
    ) as HTMLElement;

    expect(change.textContent).toContain(formatMoney(250));
    expect(change.textContent).toContain(formatPercent(25));
    expect(change.textContent).toContain('1Y');
    expect(change.classList).toContain('dashboard-page__change--positive');
    expect(change.classList).not.toContain('dashboard-page__change--negative');
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

  it('surfaces a history load failure in the chart and retries it', async () => {
    let calls = 0;
    await setup(jsonResponse(dashboard), () => {
      calls++;
      return calls === 1
        ? jsonResponse({ detail: 'History unavailable.' }, 503)
        : historyWithChange(250, 25);
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(textOf(element.querySelector('.net-worth-chart__state p'))).toBe('History unavailable.');

    (element.querySelector('.net-worth-chart__state button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element.querySelector('.net-worth-chart__state')).toBeNull();
    expect(changeText()).toContain(normalised(formatMoney(250)));
  });

  function chipOption(element: HTMLElement, label: string): HTMLButtonElement {
    const option = [
      ...element.querySelectorAll<HTMLButtonElement>('mat-chip-option button[role="option"]'),
    ].find((button) => textOf(button) === label);
    if (!option) {
      throw new Error(`No class chip labelled "${label}".`);
    }
    return option;
  }

  function selectedChips(element: HTMLElement): string[] {
    return textsOf(element, 'mat-chip-option button[aria-selected="true"]');
  }

  function legendButtons(element: HTMLElement): HTMLButtonElement[] {
    return [...element.querySelectorAll<HTMLButtonElement>('.dashboard-page__legend button')];
  }

  function legendPressed(element: HTMLElement): boolean[] {
    return legendButtons(element).map((button) => button.getAttribute('aria-pressed') === 'true');
  }

  function legendDimmed(element: HTMLElement): boolean[] {
    return legendButtons(element).map((button) =>
      button.classList.contains('dashboard-page__legend-item--dimmed'),
    );
  }

  function pieCircles(element: HTMLElement): SVGElement[] {
    return [...element.querySelectorAll<SVGElement>('app-pie-chart circle')];
  }

  function clickSvg(element: SVGElement): void {
    element.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  }

  it('renders class chips in legend order with All selected', async () => {
    await setup(jsonResponse(dashboardWithClasses));
    const element = fixture.nativeElement as HTMLElement;

    expect(textsOf(element, 'mat-chip-option')).toEqual(['All', 'ETF', 'Stock', 'Cash']);
    expect(selectedChips(element)).toEqual(['All']);
    expect(lastHistoryParams(fetchSpy).has('assetClass')).toBe(false);
  });

  it('filtering by a class requests its series and labels the change', async () => {
    await setup(jsonResponse(dashboardWithClasses), (url) =>
      url.includes('assetClass=3') ? historyWithChange(40, 4) : historyWithChange(250, 25),
    );
    const element = fixture.nativeElement as HTMLElement;
    const segmentsBefore = component['pieSegments']();

    chipOption(element, 'ETF').click();
    await fixture.whenStable();

    expect(lastHistoryParams(fetchSpy).get('assetClass')).toBe('3');
    expect(changeText()).toContain(normalised(formatMoney(40)));

    const changeElement = element.querySelector('.dashboard-page__value .dashboard-page__change');
    const classLabel = changeElement?.querySelector('.dashboard-page__change-class');
    const rangeLabel = changeElement?.querySelector('.dashboard-page__change-range');
    expect(classLabel).toBeTruthy();
    expect(rangeLabel).toBeTruthy();
    expect(textOf(classLabel)).toBe('ETF');
    expect(textOf(rangeLabel)).toBe('1Y');
    expect(
      (classLabel as Element).compareDocumentPosition(rangeLabel as Element) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    expect(textOf(element.querySelector('.dashboard-page__net-worth'))).toBe(
      normalised(formatMoney(15000)),
    );
    expect(component['pieSegments']()).toEqual(segmentsBefore);
  });

  it('switching the range keeps the class filter', async () => {
    await setup(jsonResponse(dashboardWithClasses), historyWithChange(250, 25));
    const element = fixture.nativeElement as HTMLElement;

    chipOption(element, 'ETF').click();
    await fixture.whenStable();

    const monthToggle = [...element.querySelectorAll('mat-button-toggle button')].find(
      (button) => button.textContent?.trim() === '1M',
    ) as HTMLButtonElement;
    monthToggle.click();
    await fixture.whenStable();

    const params = lastHistoryParams(fetchSpy);
    expect(params.get('range')).toBe('1M');
    expect(params.get('assetClass')).toBe('3');
    expect(selectedChips(element)).toEqual(['ETF']);
  });

  it('the legend toggles the class filter', async () => {
    await setup(jsonResponse(dashboardWithClasses));
    const element = fixture.nativeElement as HTMLElement;

    expect(legendButtons(element).map((button) => button.getAttribute('type'))).toEqual([
      'button',
      'button',
      'button',
    ]);
    expect(legendPressed(element)).toEqual([false, false, false]);

    legendButtons(element)[0].click();
    await fixture.whenStable();

    expect(selectedChips(element)).toEqual(['ETF']);
    expect(legendPressed(element)).toEqual([true, false, false]);
    expect(legendDimmed(element)).toEqual([false, true, true]);

    legendButtons(element)[0].click();
    await fixture.whenStable();

    expect(selectedChips(element)).toEqual(['All']);
    expect(legendPressed(element)).toEqual([false, false, false]);
    expect(legendDimmed(element)).toEqual([false, false, false]);
  });

  it('deselecting the class chip returns to All', async () => {
    await setup(jsonResponse(dashboardWithClasses), historyWithChange(250, 25));
    const element = fixture.nativeElement as HTMLElement;

    chipOption(element, 'ETF').click();
    await fixture.whenStable();
    chipOption(element, 'ETF').click();
    await fixture.whenStable();

    expect(selectedChips(element)).toEqual(['All']);
    expect(lastHistoryParams(fetchSpy).has('assetClass')).toBe(false);
    expect(legendPressed(element)).toEqual([false, false, false]);
  });

  it('a donut segment click toggles the class filter', async () => {
    await setup(jsonResponse(dashboardWithClasses), historyWithChange(250, 25));
    const element = fixture.nativeElement as HTMLElement;

    clickSvg(pieCircles(element)[0]);
    await fixture.whenStable();

    expect(selectedChips(element)).toEqual(['ETF']);
    expect(lastHistoryParams(fetchSpy).get('assetClass')).toBe('3');

    clickSvg(pieCircles(element)[0]);
    await fixture.whenStable();

    expect(selectedChips(element)).toEqual(['All']);
    expect(lastHistoryParams(fetchSpy).has('assetClass')).toBe(false);
  });

  it('renders the class filter in Polish', async () => {
    await setup(jsonResponse(dashboardWithClasses), historyWithChange(250, 25));
    const element = fixture.nativeElement as HTMLElement;
    chipOption(element, 'ETF').click();
    await fixture.whenStable();

    const english = textsOf(element, 'mat-chip-option');
    expect(english).toEqual(['All', 'ETF', 'Stock', 'Cash']);

    await switchLanguage(fixture, 'pl');

    const polish = textsOf(element, 'mat-chip-option');
    expect(polish).toEqual(['Wszystko', 'ETF', 'Akcje', 'Gotówka']);
    expect(polishProblems(english, polish, ['ETF'])).toEqual([]);
    expect(textsOf(element, '.dashboard-page__value .dashboard-page__kpi-label')[1]).toBe('Zmiana');
    expect(textOf(element.querySelector('.dashboard-page__change-class'))).toBe('ETF');
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
      } satisfies DashboardResponse),
    );
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h1')),
      textOf(element.querySelector('.dashboard-page__state p')),
      textOf(element.querySelector('.dashboard-page__state a')),
    ];

    const english = texts();
    expect(english).toEqual(['Dashboard', "You haven't entered any wealth yet.", 'Go to accounts']);

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

  it('orders the cards value, allocation, chart, accounts, cash, upcoming', async () => {
    await setup(jsonResponse(dashboard));
    const element = fixture.nativeElement as HTMLElement;

    const expected = [
      element.querySelector('.dashboard-page__value'),
      element.querySelector('app-pie-chart'),
      element.querySelector('app-net-worth-chart'),
      element.querySelector('app-accounts-card'),
      element.querySelector('app-cash-card'),
      element.querySelector('app-upcoming-card'),
    ];
    const ordered = [
      ...element.querySelectorAll(
        '.dashboard-page__value, app-pie-chart, app-net-worth-chart, app-accounts-card, app-cash-card, app-upcoming-card',
      ),
    ];

    expect(ordered).toHaveLength(6);
    expected.forEach((node, index) => expect(ordered[index]).toBe(node));
    expect(element.querySelectorAll('mat-button-toggle-group')).toHaveLength(1);
    expect(element.querySelector('app-net-worth-chart mat-button-toggle-group')).not.toBeNull();
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
    expect(element.querySelector('app-pie-chart')).not.toBeNull();
    expect(element.querySelector('app-net-worth-chart')).not.toBeNull();
    expect(element.querySelector('app-accounts-card')?.textContent).toContain('Retirement');
    expect(element.querySelector('app-cash-card')?.textContent).toContain(formatMoney(1000, 'PLN'));
  });

  it('the summary grid holds the accounts and cash cards', async () => {
    await setup(jsonResponse(dashboard), historyWithChange(250, 25));
    const summary = (fixture.nativeElement as HTMLElement).querySelector(
      '.dashboard-page__summary',
    ) as HTMLElement;

    expect(summary.querySelector('app-accounts-card')).not.toBeNull();
    expect(summary.querySelector('app-cash-card')).not.toBeNull();
  });

  describe('groups', () => {
    const groupCard = '.dashboard-page__group-card';

    function groupCircles(element: HTMLElement): SVGElement[] {
      return [...element.querySelectorAll<SVGElement>(`${groupCard} app-pie-chart circle`)];
    }

    function groupLegend(element: HTMLElement): HTMLButtonElement[] {
      return [
        ...element.querySelectorAll<HTMLButtonElement>('.dashboard-page__group-legend button'),
      ];
    }

    function groupLegendRow(element: HTMLElement, name: string): HTMLButtonElement {
      const row = groupLegend(element).find((button) => textOf(button).includes(name));
      if (!row) {
        throw new Error(`No group legend row for "${name}".`);
      }
      return row;
    }

    function groupChip(element: HTMLElement, label: string): HTMLButtonElement {
      const chip = [
        ...element.querySelectorAll<HTMLButtonElement>(
          'mat-chip-listbox.net-worth-chart__groups mat-chip-option button[role="option"]',
        ),
      ].find((button) => textOf(button) === label);
      if (!chip) {
        throw new Error(`No group chip labelled "${label}".`);
      }
      return chip;
    }

    function selectedGroupChips(element: HTMLElement): string[] {
      return textsOf(
        element,
        'mat-chip-listbox.net-worth-chart__groups mat-chip-option button[aria-selected="true"]',
      );
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

      expect(element.querySelectorAll('app-pie-chart')).toHaveLength(2);
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

    it('sends group and class filters', async () => {
      await setup(
        jsonResponse({
          ...dashboardWithGroups(),
          byAssetClass: dashboardWithClasses.byAssetClass,
        }),
        undefined,
        { groups: jsonResponse(groups) },
      );
      const element = fixture.nativeElement as HTMLElement;
      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);
      expect(lastHistoryParams(fetchSpy).has('ungrouped')).toBe(false);

      groupChip(element, 'Emerytura').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('groupId')).toBe(RETIREMENT_GROUP_ID);
      expect(lastHistoryParams(fetchSpy).has('assetClass')).toBe(false);

      chipOption(element, 'ETF').click();
      await fixture.whenStable();
      const both = lastHistoryParams(fetchSpy);
      expect(both.get('groupId')).toBe(RETIREMENT_GROUP_ID);
      expect(both.get('assetClass')).toBe('3');

      groupChip(element, 'No group').click();
      await fixture.whenStable();
      const ungrouped = lastHistoryParams(fetchSpy);
      expect(ungrouped.get('ungrouped')).toBe('true');
      expect(ungrouped.has('groupId')).toBe(false);
    });

    it('selects the group filter from the legend', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      const element = fixture.nativeElement as HTMLElement;

      groupLegendRow(element, 'Emerytura').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('groupId')).toBe(RETIREMENT_GROUP_ID);
      expect(selectedGroupChips(element)).toEqual(['Emerytura']);

      groupLegendRow(element, 'Emerytura').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);

      clickSvg(groupCircles(element)[1]);
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('groupId')).toBe(INVESTMENTS_GROUP_ID);

      groupLegendRow(element, 'No group').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('ungrouped')).toBe('true');
    });

    it('resets a deleted group filter', async () => {
      let current = groups;
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: () => jsonResponse(current),
      });
      const element = fixture.nativeElement as HTMLElement;

      groupLegendRow(element, 'Emerytura').click();
      await fixture.whenStable();
      expect(lastHistoryParams(fetchSpy).get('groupId')).toBe(RETIREMENT_GROUP_ID);

      current = groups.filter((group) => group.id !== RETIREMENT_GROUP_ID);
      component['groupsResource'].reload();
      await fixture.whenStable();

      expect(lastHistoryParams(fetchSpy).has('groupId')).toBe(false);
      expect(selectedGroupChips(element)).toEqual(['All']);
    });

    it('stacks the pies on a narrow container', async () => {
      await setup(jsonResponse(dashboardWithGroups()), undefined, {
        groups: jsonResponse(groups),
      });
      const element = fixture.nativeElement as HTMLElement;

      expect(
        element.querySelectorAll(
          '.dashboard-page__pies > * app-pie-chart, .dashboard-page__pies > app-pie-chart',
        ),
      ).toHaveLength(2);
      // jsdom cannot evaluate container queries, so read the component's compiled stylesheet.
      const css =
        [...document.querySelectorAll('style')]
          .map((style) => style.textContent ?? '')
          .find((text) => text.includes('.dashboard-page__pies')) ?? '';
      const split = css.indexOf('@container (min-width: 720px)');
      expect(split).toBeGreaterThan(-1);
      expect(css.slice(split)).toMatch(
        /\.dashboard-page__pies[^{]*\{[^}]*grid-template-columns:\s*repeat\(2/,
      );
      expect(css.slice(0, split)).toMatch(/\.dashboard-page__pies[^{]*\{/);
      expect(css.slice(0, split)).not.toMatch(/\.dashboard-page__pies[^{]*\{[^}]*repeat\(2/);
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
