import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
import { provideRouter } from '@angular/router';

import {
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
  textsOf,
} from '../../../testing/i18n';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse } from '../../api/reporting';
import { ASSET_CLASS, assetClassLabel } from '../assets/asset-class';
import { Dashboard } from './dashboard';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatDate } from '../../shared/format';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const dashboard: DashboardResponse = {
  netWorthPln: 15000,
  asOf: '2026-08-09',
  isStale: false,
  byAssetClass: [
    { assetClass: 0, valuePln: 5000, percentage: 33.33 },
    { assetClass: 2, valuePln: 10000, percentage: 66.67 },
  ],
  byPortfolio: [
    {
      portfolioId: '11111111-1111-1111-1111-111111111111',
      valuePln: 15000,
      snapshotDate: '2026-08-09',
      isStale: false,
    },
  ],
};

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
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
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  async function setup(
    dashboardResponse: Response,
    historyResponse = jsonResponse({ range: '1Y', points: [] }),
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      return url.includes('/net-worth-history') ? historyResponse : dashboardResponse;
    });

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
      { label: assetClassLabel(ASSET_CLASS.Cash), percentage: 33.33, color: '#4C6EF5' },
      { label: assetClassLabel(ASSET_CLASS.Stock), percentage: 66.67, color: '#12B886' },
    ]);
  });

  // savings-accounts AC-11: the colours are indexed by AssetClass, so the appended Savings class
  // (9) gets a colour of its own instead of wrapping around to Cash's.
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
  // i18n screens (#132) AC-1: headings, the stale chip and its tooltip, the "as of" line and the
  // chart's own texts follow the language.
  it('renders in Polish', async () => {
    await setup(jsonResponse({ ...dashboard, isStale: true } satisfies DashboardResponse));
    const element = fixture.nativeElement as HTMLElement;
    const tooltip = () =>
      fixture.debugElement.query(By.css('mat-chip')).injector.get(MatTooltip).message;
    const texts = () => [
      ...textsOf(element, 'h1, h2'),
      textOf(element.querySelector('mat-chip')),
      tooltip(),
      textOf(element.querySelector('.net-worth-chart__empty')),
    ];
    const asOf = () => textOf(element.querySelector('.dashboard-page__as-of'));

    const english = texts();
    expect(english.slice(0, 3)).toEqual(['Dashboard', 'Net worth history', 'Stale']);
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
    // The backend's own message stays as it arrived.
    expect(textOf(element.querySelector('.dashboard-page__state p'))).toBe('Service unavailable.');
  });
});
