import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  attributesOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
  textsOf,
} from '../../../../testing/i18n';

import { client as reportingClient } from '../../../api/reporting/client.gen';
import type { NetWorthHistoryResponse } from '../../../api/reporting';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatDate } from '../../../shared/format';
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

  afterEach(async () => {
    fetchSpy.mockRestore();
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  async function setup(response: Response): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(response);

    await TestBed.configureTestingModule({
      imports: [NetWorthChart],
      providers: [provideI18nTesting()],
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
    await setup(
      jsonResponse({ range: '1Y', points: [{ date: '2026-08-01', netWorthPln: 10000 }] }),
    );

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Not enough history to chart yet.');
  });

  it('surfaces a load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));

    expect(component['historyResource'].error()?.message).toBe('Service unavailable.');
  });
  // i18n screens (#132) AC-1: the range group's and the chart's accessible names and the "as of"
  // line follow the language; the range buttons (1M, 1Y, YTD, MAX) are codes and stay.
  it('renders in Polish', async () => {
    await setup(jsonResponse(history));
    const element = fixture.nativeElement as HTMLElement;
    const labels = () => [
      ...attributesOf(element, 'mat-button-toggle-group', 'aria-label'),
      ...attributesOf(element, 'svg', 'aria-label'),
    ];
    const asOf = () => textOf(element.querySelector('.net-worth-chart__as-of'));

    const english = labels();
    expect(english).toEqual(['Chart range', 'Net worth history chart']);
    expect(asOf()).toMatch(/^As of /);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, labels())).toEqual([]);
    expect(asOf()).not.toMatch(/^As of /);
    expect(asOf()).toContain(formatDate('2026-08-01'));
    expect(matchesTranslation('pl', asOf()), `"${asOf()}" is not a pl.json value`).toBe(true);
    expect(textsOf(element, 'mat-button-toggle')).toEqual(['1M', '1Y', 'YTD', 'MAX']);
  });

  it('renders the not-enough-history message in Polish', async () => {
    await setup(
      jsonResponse({ range: '1Y', points: [{ date: '2026-08-01', netWorthPln: 10000 }] }),
    );
    const element = fixture.nativeElement as HTMLElement;
    const message = () => [textOf(element.querySelector('.net-worth-chart__empty'))];

    const english = message();
    expect(english).toEqual(['Not enough history to chart yet.']);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, message())).toEqual([]);
  });

  it('renders the load-failure retry button in Polish', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));
    const element = fixture.nativeElement as HTMLElement;
    const buttons = () => textsOf(element, '.net-worth-chart__state button');

    expect(buttons()).toEqual(['Retry']);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(['Retry'], buttons())).toEqual([]);
  });
});
