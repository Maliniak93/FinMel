import { ComponentFixture, TestBed } from '@angular/core/testing';

import { client as marketdataClient } from '../../api/marketdata/client.gen';
import type { SyncStatusResponse } from '../../api/marketdata';
import { Settings } from './settings';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const noRunYet: SyncStatusResponse = { hasRun: false };

const completedRun: SyncStatusResponse = {
  hasRun: true,
  prices: {
    runId: '11111111-1111-1111-1111-111111111111',
    status: 1,
    startedAt: '2026-08-10T18:30:00Z',
    finishedAt: '2026-08-10T18:31:00Z',
    syncedCount: 5,
    noDataCount: 0,
    failedCount: 0,
  },
};

// The generated client calls `fetch(request)` with a single `Request` instance (see
// api/marketdata/client/client.gen.ts's `_fetch(request)`), not the two-arg `fetch(url, init)`
// form — the method lives on that Request object, not on a separate `init` argument.
function requestMethod(input: unknown): string {
  return input instanceof Request ? input.method : 'GET';
}

describe('Settings', () => {
  let fixture: ComponentFixture<Settings>;
  let component: Settings;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketdataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    statusResponse: Response,
    triggerResponse: Response = new Response(null, { status: 204 }),
  ): Promise<void> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input: RequestInfo | URL) =>
        requestMethod(input) === 'POST' ? triggerResponse : statusResponse,
      );

    await TestBed.configureTestingModule({
      imports: [Settings],
    }).compileComponents();

    fixture = TestBed.createComponent(Settings);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse(noRunYet));
    expect(component).toBeTruthy();
  });

  it('shows "no sync has run yet" before any run', async () => {
    await setup(jsonResponse(noRunYet));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No sync has run yet.');
  });

  it('shows the last run status and counts', async () => {
    await setup(jsonResponse(completedRun));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Completed');
    expect(text).toContain('synced 5');
  });

  it('shows the last Prices run and the last FX run', async () => {
    // spec-04 AC23 / design decision 14: SyncStatusResponse is reshaped (ADR-019, not kept
    // compatible) into per-kind summaries — this literal only compiles once the generated client
    // (web/api/marketdata) picks up the new `prices`/`fx`/`backfill` shape from `npm run gen:api`.
    const pricesAndFxRun: SyncStatusResponse = {
      hasRun: true,
      prices: {
        runId: '11111111-1111-1111-1111-111111111111',
        status: 1,
        startedAt: '2026-08-10T18:30:00Z',
        finishedAt: '2026-08-10T18:31:00Z',
        syncedCount: 5,
        noDataCount: 0,
        failedCount: 0,
      },
      fx: {
        runId: '22222222-2222-2222-2222-222222222222',
        status: 1,
        startedAt: '2026-08-10T13:00:00Z',
        finishedAt: '2026-08-10T13:00:30Z',
        syncedCount: 4,
        noDataCount: 0,
        failedCount: 0,
      },
    };

    await setup(jsonResponse(pricesAndFxRun));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Prices');
    expect(text).toContain('FX');
    expect(text).toContain('synced 5');
    expect(text).toContain('synced 4');
  });

  it('reloads the status after a successful trigger', async () => {
    await setup(jsonResponse(noRunYet));
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) =>
      requestMethod(input) === 'POST'
        ? new Response(null, { status: 204 })
        : jsonResponse(completedRun),
    );

    await component['triggerSync']();
    await fixture.whenStable();

    expect(component['statusResource'].value()).toEqual(completedRun);
    expect(component['triggerError']()).toBeNull();
  });

  it('surfaces a 409 "already running" trigger failure without touching the status', async () => {
    await setup(
      jsonResponse(noRunYet),
      jsonResponse({ detail: 'A price sync is already running.' }, 409),
    );

    await component['triggerSync']();
    await fixture.whenStable();

    expect(component['triggerError']()).toBe('A price sync is already running.');
  });
});
