import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { WealthSummaryResponse } from '../../api/portfolio';
import { Dashboard } from './dashboard';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const summary: WealthSummaryResponse = {
  totalNetWorth: 15000,
  baseCurrency: 'PLN',
  fxConversionIsNaive: true,
  byAssetClass: [
    { assetClass: 0, value: 5000, percentage: 33.33 },
    { assetClass: 2, value: 10000, percentage: 66.67 },
  ],
  byPortfolio: [
    { portfolioId: '11111111-1111-1111-1111-111111111111', name: 'Retirement', value: 15000 },
  ],
};

describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let component: Dashboard;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(response: Response): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response);

    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(Dashboard);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse(summary));
    expect(component).toBeTruthy();
  });

  it('loads the wealth summary into its resource', async () => {
    await setup(jsonResponse(summary));
    expect(component['summaryResource'].value()).toEqual(summary);
  });

  it('shows the formatted net worth and the FX caveat', async () => {
    await setup(jsonResponse(summary));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    const expectedNetWorth = new Intl.NumberFormat('pl-PL', {
      style: 'currency',
      currency: 'PLN',
    }).format(15000);
    expect(text).toContain(expectedNetWorth);
    expect(text).toContain('without a real exchange rate');
  });

  it('builds pie segments from the asset-class breakdown', async () => {
    await setup(jsonResponse(summary));
    expect(component['pieSegments']()).toEqual([
      { label: 'Cash', percentage: 33.33, color: '#4C6EF5' },
      { label: 'Stock', percentage: 66.67, color: '#12B886' },
    ]);
  });

  it('shows an empty state when net worth is zero', async () => {
    await setup(
      jsonResponse({
        totalNetWorth: 0,
        baseCurrency: 'PLN',
        fxConversionIsNaive: false,
        byAssetClass: [],
        byPortfolio: [],
      } satisfies WealthSummaryResponse),
    );
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("You haven't entered any wealth yet.");
  });

  it('surfaces a load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
    expect(component['summaryResource'].error()?.message).toBe('Service unavailable.');
  });
});
