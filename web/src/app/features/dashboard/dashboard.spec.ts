import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
import { provideRouter } from '@angular/router';

import {
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
  cashAccountsResponse,
  dashboard,
  dashboardWithClasses,
  jsonResponse,
  lastHistoryParams,
  mockApi,
  portfolioResponse,
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
        color: '#4C6EF5',
      },
      {
        key: String(ASSET_CLASS.Stock),
        label: assetClassLabel(ASSET_CLASS.Stock),
        percentage: 66.67,
        color: '#12B886',
      },
    ]);
  });

  it('gives the Savings class a colour of its own', async () => {
    await setup(
      jsonResponse({
        ...dashboard,
        byAssetClass: [
          { assetClass: ASSET_CLASS.Cash, valuePln: 5000, percentage: 50 },
          { assetClass: ASSET_CLASS.Savings, valuePln: 5000, percentage: 50 },
        ],
      } satisfies DashboardResponse),
    );

    const [cash, savings] = component['pieSegments']();

    expect(savings.label).toBe(assetClassLabel(ASSET_CLASS.Savings));
    expect(savings.color).toBeTruthy();
    expect(savings.color).not.toBe(cash.color);
  });

  it('shows an empty state when no snapshot has ever been computed', async () => {
    await setup(
      jsonResponse({
        netWorthPln: 0,
        asOf: null,
        isStale: false,
        byAssetClass: [],
        byPortfolio: [],
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
        byPortfolio: [],
      } satisfies DashboardResponse),
    );
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      textOf(element.querySelector('h1')),
      textOf(element.querySelector('.dashboard-page__state p')),
      textOf(element.querySelector('.dashboard-page__state a')),
    ];

    const english = texts();
    expect(english).toEqual([
      'Dashboard',
      "You haven't entered any wealth yet.",
      'Go to portfolios',
    ]);

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

  it('orders the cards value, allocation, chart, portfolios, cash, upcoming', async () => {
    await setup(jsonResponse(dashboard));
    const element = fixture.nativeElement as HTMLElement;

    const expected = [
      element.querySelector('.dashboard-page__value'),
      element.querySelector('app-pie-chart'),
      element.querySelector('app-net-worth-chart'),
      element.querySelector('app-portfolios-card'),
      element.querySelector('app-cash-card'),
      element.querySelector('app-upcoming-card'),
    ];
    const ordered = [
      ...element.querySelectorAll(
        '.dashboard-page__value, app-pie-chart, app-net-worth-chart, app-portfolios-card, app-cash-card, app-upcoming-card',
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
      portfolios: jsonResponse([portfolioResponse()]),
      cash: jsonResponse(cashAccountsResponse([{ currency: 'PLN', balance: 1000 }])),
    });
    const element = fixture.nativeElement as HTMLElement;
    const upcoming = element.querySelector('app-upcoming-card') as HTMLElement;

    expect(upcoming.textContent).toContain('Bonds unavailable.');
    expect(upcoming.querySelector('button')).not.toBeNull();
    expect(element.querySelector('.dashboard-page__value')).not.toBeNull();
    expect(element.querySelector('app-pie-chart')).not.toBeNull();
    expect(element.querySelector('app-net-worth-chart')).not.toBeNull();
    expect(element.querySelector('app-portfolios-card')?.textContent).toContain('Retirement');
    expect(element.querySelector('app-cash-card')?.textContent).toContain(formatMoney(1000, 'PLN'));
  });
});
