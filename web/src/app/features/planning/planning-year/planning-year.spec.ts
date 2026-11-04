import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
import { PlanningYear } from './planning-year';

const VALUE_KEYS = [
  'incomePlanPln',
  'incomeActualPln',
  'differencePln',
  'costPln',
  'freeFundsPlanPln',
  'freeFundsForecastPln',
] as const;

const EMPTY_MONTH = {
  hasDefinitions: false,
  incomePlanPln: null,
  incomeActualPln: null,
  differencePln: null,
  costPln: null,
  freeFundsPlanPln: null,
  freeFundsForecastPln: null,
  hasOverdue: false,
  incomplete: false,
};

const FULL_MONTH = {
  hasDefinitions: true,
  incomePlanPln: 16500,
  incomeActualPln: 16443.27,
  differencePln: -56.73,
  costPln: 4200,
  freeFundsPlanPln: 12300,
  freeFundsForecastPln: 12243.27,
  hasOverdue: false,
  incomplete: false,
};

let emptyYear = false;
let yearFails = false;

function totalsOf(months: Record<string, unknown>[]): Record<string, number> {
  return Object.fromEntries(
    VALUE_KEYS.map((key) => [
      key,
      Math.round(months.reduce((sum, row) => sum + ((row[key] as number | null) ?? 0), 0) * 100) /
        100,
    ]),
  );
}

// 2026: January and February have no definitions; November and December are future months.
// 2027: every actual is future.
function yearPage(year: number): Record<string, unknown> {
  const months = Array.from({ length: 12 }, (_, index) => {
    const month = `${year}-${String(index + 1).padStart(2, '0')}`;
    if (year === 2026 && index < 2) {
      return { month, ...EMPTY_MONTH };
    }

    const future = year > 2026 || index > 9;
    return {
      month,
      ...FULL_MONTH,
      incomeActualPln: future ? null : FULL_MONTH.incomeActualPln,
      freeFundsForecastPln: month === '2026-09' ? 11111.11 : FULL_MONTH.freeFundsForecastPln,
      hasOverdue: month === '2026-10',
      incomplete: month === '2026-04',
    };
  });

  if (emptyYear) {
    const empty = months.map(({ month }) => ({ month, ...EMPTY_MONTH }));
    return { year, firstYear: 2026, lastYear: 2027, months: empty, totals: totalsOf(empty) };
  }

  return { year, firstYear: 2026, lastYear: 2027, months, totals: totalsOf(months) };
}

describe('PlanningYear', () => {
  const originalMatchMedia = window.matchMedia;
  let harness: RouterTestingHarness;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
    emptyYear = false;
    yearFails = false;
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
    window.matchMedia = originalMatchMedia;
  });

  function phoneViewport(): void {
    window.matchMedia = ((query: string) => ({
      matches: query.includes('max-width'),
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
      onchange: null,
    })) as typeof window.matchMedia;
  }

  async function setup(url = '/planning?tab=year&year=2026'): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const requested = /\/planning\/years\/(\d{4})/.exec(requestUrl(input));
      if (!requested) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }

      if (yearFails) {
        return jsonResponse({ detail: 'Year failed.' }, 500);
      }

      return jsonResponse(yearPage(Number(requested[1])));
    });

    await TestBed.configureTestingModule({
      providers: [
        provideI18nTesting(),
        provideRouter([{ path: 'planning', component: PlanningYear }], withComponentInputBinding()),
      ],
    }).compileComponents();

    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, PlanningYear);
    await settle();
  }

  async function settle(): Promise<void> {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  function element(): HTMLElement {
    return harness.fixture.nativeElement as HTMLElement;
  }

  function rows(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[data-testid="year-row"]'));
  }

  function cellsOf(row: HTMLElement): string[] {
    return Array.from(row.querySelectorAll('td'), (cell) => cell.textContent?.trim() ?? '');
  }

  function button(testId: string): HTMLButtonElement {
    return element().querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!;
  }

  function yearLabel(): string {
    return element().querySelector('[data-testid="year-label"]')?.textContent ?? '';
  }

  it('renders the six value columns of a month with the formatted values', async () => {
    await setup();

    expect(rows()).toHaveLength(12);
    expect(cellsOf(rows()[9])).toEqual([
      formatMoney(16500, 'PLN'),
      formatMoney(16443.27, 'PLN'),
      formatMoney(-56.73, 'PLN'),
      formatMoney(4200, 'PLN'),
      formatMoney(12300, 'PLN'),
      formatMoney(12243.27, 'PLN'),
    ]);
  });

  it('shows an em dash in every value of a month without definitions', async () => {
    await setup();

    expect(cellsOf(rows()[0])).toEqual(Array(6).fill('—'));
    expect(cellsOf(rows()[1])).toEqual(Array(6).fill('—'));
  });

  it('leaves the actual empty for future months', async () => {
    await setup();

    expect(cellsOf(rows()[10])[1]).toBe('');
    expect(cellsOf(rows()[11])[1]).toBe('');
    expect(cellsOf(rows()[10])[0]).toBe(formatMoney(16500, 'PLN'));
  });

  it('marks the overdue month and highlights the current month', async () => {
    await setup();

    expect(
      element().querySelector('app-planning-year-chart [data-current="true"]')?.textContent,
    ).toContain('Oct');

    expect(rows()[9].querySelector('[data-testid="year-overdue"]')).not.toBeNull();
    expect(rows()[9].getAttribute('aria-current')).toBe('date');
    expect(rows().filter((row) => row.getAttribute('aria-current') !== null)).toHaveLength(1);
  });

  it('shows the total row with the column sums', async () => {
    await setup();

    const totals = yearPage(2026)['totals'] as Record<(typeof VALUE_KEYS)[number], number>;
    const total = element().querySelector<HTMLElement>('[data-testid="year-total"]')!;

    expect(cellsOf(total)).toEqual(VALUE_KEYS.map((key) => formatMoney(totals[key], 'PLN')));
  });

  it('disables the arrows at the bounds and opens the next year with an empty actual', async () => {
    await setup();

    expect(button('year-previous').disabled).toBe(true);
    expect(button('year-next').disabled).toBe(false);

    button('year-next').click();
    await settle();
    await vi.waitFor(() => {
      harness.fixture.detectChanges();
      expect(yearLabel()).toContain('2027');
    });

    expect(button('year-previous').disabled).toBe(false);
    expect(button('year-next').disabled).toBe(true);
    expect(rows().map((row) => cellsOf(row)[1])).toEqual(Array(12).fill(''));
  });

  it('navigates to the month view when a month row is clicked', async () => {
    await setup();

    rows()[9].click();
    await settle();

    expect(TestBed.inject(Router).url).toBe('/planning?month=2026-10');
  });

  it('marks an incomplete month in the table', async () => {
    await setup();

    expect(rows()[3].querySelector('[data-testid="year-incomplete"]')?.textContent).toContain(
      'incomplete',
    );
    expect(rows()[4].querySelector('[data-testid="year-incomplete"]')).toBeNull();
  });

  it('shows the result of a past month instead of the forecast', async () => {
    await setup();

    const past = rows()[8].querySelector('[data-testid="year-result-actual"]');
    expect(past?.textContent?.trim()).toBe(formatMoney(11111.11, 'PLN'));
    expect(rows()[9].querySelector('[data-testid="year-result-actual"]')).toBeNull();
    expect(rows()[10].querySelector('[data-testid="year-result-actual"]')).toBeNull();
  });

  it('opens the month on Enter from a focused row', async () => {
    await setup();

    expect(rows()[9].tabIndex).toBe(0);
    rows()[9].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();

    expect(TestBed.inject(Router).url).toBe('/planning?month=2026-10');
  });

  it('shows one empty state for a year without definitions', async () => {
    emptyYear = true;
    await setup();

    expect(element().querySelectorAll('app-empty-state')).toHaveLength(1);
    expect(rows()).toHaveLength(0);
    expect(element().querySelector('app-planning-year-chart')).toBeNull();
    expect(element().querySelector('table')).toBeNull();
  });

  it('narrows the table on a phone and expands a row', async () => {
    phoneViewport();
    await setup();

    expect(element().querySelector('app-planning-year-chart')).not.toBeNull();
    const headers = Array.from(element().querySelectorAll('thead th'), (th) =>
      th.textContent?.trim(),
    );
    expect(headers).toContain('Month');
    expect(headers).toContain('Expenses');
    expect(headers).not.toContain('Income plan');

    const toggle = rows()[9].querySelector<HTMLButtonElement>('[data-testid="year-expand"]')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    await settle();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const detail = element().querySelector<HTMLElement>('[data-testid="year-detail"]')!;
    expect(detail.textContent).toContain(formatMoney(16500, 'PLN'));
    expect(TestBed.inject(Router).url).not.toContain('month=');
  });

  it('shows a retry error when the year fails', async () => {
    yearFails = true;
    await setup();

    const error = element().querySelector('app-section-error');
    expect(error).not.toBeNull();
    expect(error!.textContent).toContain('Retry');
    expect(rows()).toHaveLength(0);
  });
});
