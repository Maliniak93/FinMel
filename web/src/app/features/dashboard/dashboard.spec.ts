import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse } from '../../api/reporting';
import { Dashboard } from './dashboard';

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

  afterEach(() => {
    fetchSpy.mockRestore();
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
      providers: [provideRouter([])],
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
    const expectedNetWorth = new Intl.NumberFormat('pl-PL', {
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
      { label: 'Cash', percentage: 33.33, color: '#4C6EF5' },
      { label: 'Stock', percentage: 66.67, color: '#12B886' },
    ]);
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
});
