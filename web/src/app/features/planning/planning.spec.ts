import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { Planning } from './planning';

const accountId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const archivedAccountId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function occurrence(
  name: string,
  date: string,
  amount: number,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    recurringIncomeId: `id-${name}`,
    nominalMonth: date.slice(0, 7),
    date,
    name,
    accountId,
    accountName: 'mBank ROR',
    accountArchived: false,
    amount,
    currency: 'PLN',
    amountPln: amount,
    estimated: false,
    ...extra,
  };
}

function definition(name: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: `id-${name}`,
    name,
    startDate: '2026-01-01',
    endDate: null,
    status: 'Active',
    amount: 100,
    currency: 'PLN',
    accountId,
    accountName: 'mBank ROR',
    accountArchived: false,
    intervalMonths: 1,
    dayKind: 1,
    dayOfMonth: null,
    shift: 0,
    ...extra,
  };
}

function month(incomes: Record<string, unknown>): Record<string, unknown> {
  return {
    incomes: {
      occurrences: [],
      totalPln: 0,
      fxUnavailable: false,
      definitions: [],
      ...incomes,
    },
  };
}

describe('Planning', () => {
  let harness: RouterTestingHarness;
  let component: Planning;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let responses: Record<string, Record<string, unknown>>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
    responses = {};
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  async function setup(url = '/planning'): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const requested = /\/planning\/months\/(\d{4}-\d{2})/.exec(requestUrl(input));
      if (!requested) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return jsonResponse(responses[requested[1]] ?? month({}));
    });

    await TestBed.configureTestingModule({
      providers: [
        provideI18nTesting(),
        provideRouter([{ path: 'planning', component: Planning }], withComponentInputBinding()),
        { provide: MatDialog, useValue: { open: vi.fn(() => ({ afterClosed: () => of(false) })) } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();

    harness = await RouterTestingHarness.create();
    component = await harness.navigateByUrl(url, Planning);
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

  function requestedMonths(): string[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => /\/planning\/months\/(\d{4}-\d{2})/.exec(requestUrl(call[0]))?.[1])
      .filter((value: string | undefined): value is string => value !== undefined);
  }

  function click(testId: string): void {
    element().querySelector<HTMLElement>(`[data-testid="${testId}"]`)!.click();
  }

  function rows(testId: string): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>(`[data-testid="${testId}"]`));
  }

  it('requests the current Warsaw month without ?month and walks to the adjacent months', async () => {
    await setup();

    expect(component).toBeTruthy();
    expect(requestedMonths()).toEqual(['2026-10']);

    click('next-month');
    await settle();

    expect(requestedMonths().at(-1)).toBe('2026-11');
    expect(TestBed.inject(Router).url).toContain('month=2026-11');

    click('previous-month');
    await settle();
    click('previous-month');
    await settle();

    expect(requestedMonths().at(-1)).toBe('2026-09');
    expect(TestBed.inject(Router).url).toContain('month=2026-09');
  });

  it('deep-links to the month in the query param', async () => {
    await setup('/planning?month=2027-02');

    expect(requestedMonths()[0]).toBe('2027-02');
  });

  it('renders occurrences, the PLN estimate, the archived warning and an incomplete total', async () => {
    responses['2026-10'] = month({
      occurrences: [
        occurrence('Wypłata żona', '2026-10-09', 6500),
        occurrence('Najem EUR', '2026-10-15', 1000, {
          accountName: 'Revolut EUR',
          currency: 'EUR',
          amountPln: 4250,
          estimated: true,
        }),
        occurrence('Stara premia', '2026-10-20', 300, {
          accountId: archivedAccountId,
          accountName: 'Stare konto',
          accountArchived: true,
        }),
        occurrence('Zwrot USD', '2026-10-25', 50, {
          currency: 'USD',
          amountPln: null,
          estimated: true,
        }),
      ],
      totalPln: 11050,
      fxUnavailable: true,
      definitions: [definition('Wypłata żona'), definition('Najem EUR')],
    });
    await setup();

    const occurrenceRows = rows('occurrence-row');
    expect(occurrenceRows.map((row) => row.textContent)).toEqual([
      expect.stringContaining('Wypłata żona'),
      expect.stringContaining('Najem EUR'),
      expect.stringContaining('Stara premia'),
      expect.stringContaining('Zwrot USD'),
    ]);
    expect(occurrenceRows[0].textContent).toContain(formatMoney(6500, 'PLN'));
    expect(occurrenceRows[0].textContent).not.toContain('≈');
    expect(occurrenceRows[1].textContent).toContain(formatMoney(1000, 'EUR'));
    expect(occurrenceRows[1].textContent).toContain('≈');
    expect(occurrenceRows[1].textContent).toContain(formatMoney(4250, 'PLN'));
    expect(rows('archived-warning')).toHaveLength(1);
    expect(occurrenceRows[2].querySelector('[data-testid="archived-warning"]')).not.toBeNull();
    expect(element().textContent).toContain(formatMoney(11050, 'PLN'));
    expect(rows('total-incomplete')).toHaveLength(1);
    expect(rows('definition-row')).toHaveLength(2);
  });

  it('marks nothing incomplete when every rate is known', async () => {
    responses['2026-10'] = month({
      occurrences: [occurrence('Wypłata żona', '2026-10-09', 6500)],
      totalPln: 6500,
    });
    await setup();

    expect(rows('total-incomplete')).toHaveLength(0);
    expect(rows('archived-warning')).toHaveLength(0);
    expect(element().textContent).toContain(formatMoney(6500, 'PLN'));
  });
});
