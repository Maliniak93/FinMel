import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { Planning } from './planning';
import { RecurringIncomeFormDialog } from './recurring-income-form-dialog/recurring-income-form-dialog';
import { SettleOccurrenceDialog } from './settle-occurrence-dialog/settle-occurrence-dialog';

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
    status: 'Pending',
    outOfRule: false,
    plannedAccountId: accountId,
    actualAmount: null,
    actualDate: null,
    difference: null,
    actualAmountPln: null,
    depositCreated: false,
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
    effectiveFrom: '2026-01',
    nextChange: null,
    ...extra,
  };
}

function nextChange(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    effectiveFrom: '2027-01',
    amount: 9800,
    currency: 'PLN',
    accountName: 'mBank ROR',
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
      if ((input as Request).method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
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

  it('shows the next scheduled change with its month and amount, and the account only when it changes', async () => {
    responses['2026-10'] = month({
      definitions: [
        definition('Wypłata Dominik', { nextChange: nextChange() }),
        definition('Premia', { nextChange: nextChange({ accountName: 'Konto firmowe' }) }),
        definition('Najem'),
      ],
    });
    await setup();

    const changes = rows('next-change');
    expect(changes).toHaveLength(2);
    expect(changes[0].textContent).toContain('01.2027');
    expect(changes[0].textContent).toContain(formatMoney(9800, 'PLN'));
    expect(changes[0].textContent).not.toContain('Konto firmowe');
    expect(changes[1].textContent).toContain('Konto firmowe');
  });

  it('opens the edit dialog with the definition of the viewed month', async () => {
    responses['2026-10'] = month({ definitions: [definition('Wypłata Dominik')] });
    await setup();

    click('edit-income');

    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      RecurringIncomeFormDialog,
      expect.objectContaining({
        data: { definition: expect.objectContaining({ id: 'id-Wypłata Dominik' }) },
      }),
    );
  });

  it('deletes the scheduled change after confirmation and reloads the month', async () => {
    responses['2026-10'] = month({
      definitions: [definition('Najem', { nextChange: nextChange() })],
    });
    await setup();
    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);

    click('delete-change');

    await vi.waitFor(() => {
      const deletes = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'DELETE');
      expect(deletes).toHaveLength(1);
      expect(deletes[0].url).toContain('/recurring-incomes/id-Najem/changes/2027-01');
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));
  });

  it('shows status, actual and difference, the out-of-rule mark and the actions of each status, and confirms before undo', async () => {
    responses['2026-10'] = month({
      occurrences: [
        occurrence('Pensja', '2026-10-05', 500),
        occurrence('Wypłata', '2026-10-30', 9200, {
          status: 'Settled',
          accountName: 'Konto firmowe',
          actualAmount: 9143.27,
          actualDate: '2026-10-29',
          difference: -56.73,
          actualAmountPln: 9143.27,
          depositCreated: true,
        }),
        occurrence('800+', '2026-10-10', 800, { status: 'Skipped', difference: -800 }),
        occurrence('Stara premia', '2026-10-12', 300, {
          status: 'Settled',
          outOfRule: true,
          actualAmount: 300,
          actualDate: '2026-10-12',
          difference: 0,
          actualAmountPln: 300,
        }),
      ],
      totalPln: 10800,
    });
    await setup();

    const [pending, settled, skipped, outOfRule] = rows('occurrence-row');
    const has = (row: HTMLElement, testId: string): boolean =>
      row.querySelector(`[data-testid="${testId}"]`) !== null;
    const statusOf = (row: HTMLElement): string =>
      row.querySelector('[data-testid="occurrence-status"]')!.textContent!;

    expect(statusOf(pending)).toMatch(/pending/i);
    expect(statusOf(settled)).toMatch(/settled/i);
    expect(statusOf(skipped)).toMatch(/skipped/i);

    expect(has(pending, 'settle-occurrence')).toBe(true);
    expect(has(pending, 'skip-occurrence')).toBe(true);
    expect(has(pending, 'edit-settlement')).toBe(false);
    expect(has(pending, 'undo-occurrence')).toBe(false);

    expect(has(settled, 'edit-settlement')).toBe(true);
    expect(has(settled, 'undo-occurrence')).toBe(true);
    expect(has(settled, 'settle-occurrence')).toBe(false);
    expect(has(settled, 'skip-occurrence')).toBe(false);

    expect(has(skipped, 'settle-occurrence')).toBe(true);
    expect(has(skipped, 'undo-occurrence')).toBe(true);
    expect(has(skipped, 'skip-occurrence')).toBe(false);
    expect(has(skipped, 'edit-settlement')).toBe(false);

    expect(settled.textContent).toContain(formatMoney(9200, 'PLN'));
    expect(settled.querySelector('[data-testid="occurrence-actual"]')!.textContent).toContain(
      formatMoney(9143.27, 'PLN'),
    );
    expect(settled.querySelector('[data-testid="occurrence-difference"]')!.textContent).toContain(
      formatMoney(-56.73, 'PLN'),
    );
    expect(skipped.querySelector('[data-testid="occurrence-difference"]')!.textContent).toContain(
      formatMoney(-800, 'PLN'),
    );

    expect(rows('deposit-created')).toHaveLength(1);
    expect(has(settled, 'deposit-created')).toBe(true);
    expect(has(outOfRule, 'deposit-created')).toBe(false);

    expect(rows('out-of-rule')).toHaveLength(1);
    expect(has(outOfRule, 'out-of-rule')).toBe(true);

    pending.querySelector<HTMLElement>('[data-testid="settle-occurrence"]')!.click();
    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      SettleOccurrenceDialog,
      expect.objectContaining({
        data: {
          occurrence: expect.objectContaining({
            recurringIncomeId: 'id-Pensja',
            nominalMonth: '2026-10',
          }),
        },
      }),
    );

    const deletes = (): Request[] =>
      fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'DELETE');

    settled.querySelector<HTMLElement>('[data-testid="undo-occurrence"]')!.click();
    await settle();

    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    expect(deletes()).toHaveLength(0);

    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    settled.querySelector<HTMLElement>('[data-testid="undo-occurrence"]')!.click();

    await vi.waitFor(() => {
      expect(deletes()).toHaveLength(1);
      expect(decodeURIComponent(deletes()[0].url)).toContain(
        '/recurring-incomes/id-Wypłata/occurrences/2026-10',
      );
    });
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
