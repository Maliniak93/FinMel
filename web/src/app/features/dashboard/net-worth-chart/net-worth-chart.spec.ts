import { ComponentFixture, TestBed } from '@angular/core/testing';

import { client as reportingClient } from '../../../api/reporting/client.gen';
import type { NetWorthHistoryResponse } from '../../../api/reporting';
import { NetWorthChart } from './net-worth-chart';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const history: NetWorthHistoryResponse = {
  range: '1Y',
  points: [
    { date: '2026-06-01', netWorthPln: 10000 },
    { date: '2026-07-01', netWorthPln: 11000 },
    { date: '2026-08-01', netWorthPln: 10500 },
  ],
};

describe('NetWorthChart', () => {
  let fixture: ComponentFixture<NetWorthChart>;
  let component: NetWorthChart;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(response: Response): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response);

    await TestBed.configureTestingModule({
      imports: [NetWorthChart],
    }).compileComponents();

    fixture = TestBed.createComponent(NetWorthChart);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse(history));
    expect(component).toBeTruthy();
  });

  it('requests the default 1Y range on load', async () => {
    await setup(jsonResponse(history));

    const url = (fetchSpy.mock.calls[0][0] as Request).url;
    expect(url).toContain('range=1Y');
  });

  it('builds a chart point per history point and shows the latest date as-of', async () => {
    await setup(jsonResponse(history));

    expect(component['points']()).toHaveLength(3);
    expect(component['asOf']()).toBe('2026-08-01');
  });

  it('re-requests history with the newly selected range', async () => {
    await setup(jsonResponse(history));
    const callsBefore = fetchSpy.mock.calls.length;

    component['setRange']('MAX');
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore);
    const url = (fetchSpy.mock.calls.at(-1)?.[0] as Request).url;
    expect(url).toContain('range=MAX');
  });

  it('shows a not-enough-data message with fewer than two points', async () => {
    await setup(jsonResponse({ range: '1Y', points: [{ date: '2026-08-01', netWorthPln: 10000 }] }));

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Not enough history to chart yet.');
  });

  it('surfaces a load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));

    expect(component['historyResource'].error()?.message).toBe('Service unavailable.');
  });
});
