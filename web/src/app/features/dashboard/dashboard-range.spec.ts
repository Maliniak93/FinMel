import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { NetWorthHistoryResponse } from '../../api/reporting';
import { provideI18nTesting } from '../../core/i18n/testing';
import { Dashboard } from './dashboard';
import {
  dashboardWithClasses,
  jsonResponse,
  lastHistoryParams,
  menuItem,
  mockApi,
  openFilterMenu,
} from './testing/dashboard-fixtures';

interface RangeResult {
  readonly from: string;
  readonly to: string;
}

function history(range = '1Y', firstDate: string | null = '2025-01-10'): Response {
  return jsonResponse({
    range,
    points: [
      { date: '2025-07-01', netWorthPln: 1000, isIncomplete: false },
      { date: '2026-08-01', netWorthPln: 1250, isIncomplete: false },
    ],
    changePln: 250,
    changePercent: 25,
    firstDate,
    lastDate: firstDate === null ? null : '2026-10-19',
  } satisfies NetWorthHistoryResponse);
}

describe('Dashboard custom range', () => {
  let fixture: ComponentFixture<Dashboard>;
  let fetchSpy: ReturnType<typeof mockApi>;
  let dialogResult: RangeResult | undefined;
  let dialogOpen: ReturnType<typeof vi.fn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    vi.useRealTimers();
    await restoreEnglish();
  });

  async function setup(): Promise<HTMLElement> {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20));
    fetchSpy = mockApi({ dashboard: jsonResponse(dashboardWithClasses), history: () => history() });
    dialogOpen = vi.fn(() => ({ afterClosed: () => of(dialogResult) }));

    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: MatDialog, useValue: { open: dialogOpen } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function radios(element: HTMLElement): HTMLButtonElement[] {
    return [
      ...element.querySelectorAll<HTMLButtonElement>('mat-button-toggle button[role="radio"]'),
    ];
  }

  function checked(element: HTMLElement): string[] {
    return radios(element).map((radio) => radio.getAttribute('aria-checked') ?? '');
  }

  async function applyRange(element: HTMLElement, result: RangeResult | undefined): Promise<void> {
    dialogResult = result;
    radios(element)[4].click();
    await fixture.whenStable();
  }

  async function polishChange(): Promise<string> {
    await switchLanguage(fixture, 'pl');
    return textOf(
      (fixture.nativeElement as HTMLElement).querySelector(
        '.dashboard-page__value .dashboard-page__change',
      ),
    );
  }

  it('starts at 1Y', async () => {
    const element = await setup();

    const params = lastHistoryParams(fetchSpy);
    expect(params.get('range')).toBe('1Y');
    expect(params.has('from')).toBe(false);
    expect(params.has('to')).toBe(false);
    expect(checked(element)).toEqual(['false', 'true', 'false', 'false', 'false']);
  });

  it('applying a custom range requests from and to', async () => {
    const element = await setup();

    await applyRange(element, { from: '2025-01-01', to: '2025-12-31' });

    const params = lastHistoryParams(fetchSpy);
    expect(params.get('from')).toBe('2025-01-01');
    expect(params.get('to')).toBe('2025-12-31');
    expect(params.has('range')).toBe(false);
  });

  it('custom range checks only Zakres', async () => {
    const element = await setup();

    await applyRange(element, { from: '2025-01-01', to: '2025-12-31' });

    expect(checked(element)).toEqual(['false', 'false', 'false', 'false', 'true']);
  });

  it('clicking a preset leaves the custom range', async () => {
    const element = await setup();
    await applyRange(element, { from: '2025-01-01', to: '2025-12-31' });

    radios(element)[0].click();
    await fixture.whenStable();

    const params = lastHistoryParams(fetchSpy);
    expect(params.get('range')).toBe('1M');
    expect(params.has('from')).toBe(false);
    expect(params.has('to')).toBe(false);
    expect(checked(element)).toEqual(['true', 'false', 'false', 'false', 'false']);
  });

  it('closing the dialog keeps the previous range', async () => {
    const element = await setup();

    await applyRange(element, undefined);

    expect(dialogOpen).toHaveBeenCalled();
    const params = lastHistoryParams(fetchSpy);
    expect(params.get('range')).toBe('1Y');
    expect(params.has('from')).toBe(false);
    expect(checked(element)).toEqual(['false', 'true', 'false', 'false', 'false']);
  });

  it('captions a full year', async () => {
    const element = await setup();
    await applyRange(element, { from: '2025-01-01', to: '2025-12-31' });

    expect(await polishChange()).toContain('zmiana za 2025');
  });

  it('captions the current year as to-date', async () => {
    const element = await setup();
    await applyRange(element, { from: '2026-01-01', to: '2026-10-20' });

    expect(await polishChange()).toContain('zmiana za 2026 (do dziś)');
  });

  it('captions typed dates', async () => {
    const element = await setup();
    await applyRange(element, { from: '2025-03-15', to: '2026-06-30' });

    expect(await polishChange()).toContain('zmiana za 15.03.2025–30.06.2026');
  });

  it('custom range keeps the asset filter', async () => {
    const element = await setup();
    await applyRange(element, { from: '2025-01-01', to: '2025-12-31' });

    menuItem(await openFilterMenu(fixture), 'ETF').click();
    await fixture.whenStable();

    const params = lastHistoryParams(fetchSpy);
    expect(params.get('assetClass')).toBe('3');
    expect(params.get('from')).toBe('2025-01-01');
    expect(params.get('to')).toBe('2025-12-31');
    expect(params.has('range')).toBe(false);
    expect(checked(element)[4]).toBe('true');
  });
});
