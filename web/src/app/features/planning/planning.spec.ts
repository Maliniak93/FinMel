import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney, formatPercent } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import {
  allocation,
  allocationBucket,
  cushionBucket,
  firstPlanBuckets,
  limitedBucket,
  transferGroup,
  transferPlan,
  transferRow,
} from '../../../testing/allocation-fixtures';
import { AllocationPlanDialog } from './allocation-plan-dialog/allocation-plan-dialog';
import { BucketBalanceDialog } from './bucket-balance-dialog/bucket-balance-dialog';
import { Planning } from './planning';
import { RecurringExpenseFormDialog } from './recurring-expense-form-dialog/recurring-expense-form-dialog';
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
    overdue: false,
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

function balance(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { planPln: 0, actualPln: 0, pendingPln: 0, differencePln: 0, ...extra };
}

function overdueItem(name: string, date: string, amount: number): Record<string, unknown> {
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
  };
}

function expenseDefinition(
  name: string,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    id: `expense-${name}`,
    name,
    startDate: '2026-01-01',
    endDate: null,
    status: 'Active',
    amount: 1000,
    intervalMonths: 1,
    dayKind: 0,
    dayOfMonth: 10,
    shift: 0,
    reservePln: 1000,
    nextPayment: null,
    effectiveFrom: '2026-01',
    nextChange: null,
    ...extra,
  };
}

function expenseNextChange(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    effectiveFrom: '2027-01',
    amount: 1320,
    intervalMonths: 12,
    dayKind: 0,
    dayOfMonth: 15,
    shift: 0,
    ...extra,
  };
}

function expenses(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { payments: [], costPln: 0, definitions: [], ...extra };
}

function freeFunds(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { planPln: 0, forecastPln: 0, incomplete: false, ...extra };
}

function month(
  incomes: Record<string, unknown>,
  expensesPart: Record<string, unknown> = {},
  freeFundsPart: Record<string, unknown> = {},
  allocationPart: Record<string, unknown> = {},
  transferPlanPart: Record<string, unknown> | null = null,
): Record<string, unknown> {
  return {
    ...(transferPlanPart ? { transferPlan: transferPlan(transferPlanPart) } : {}),
    incomes: {
      occurrences: [],
      balance: balance(),
      fxUnavailable: false,
      definitions: [],
      ...incomes,
    },
    expenses: expenses(expensesPart),
    freeFunds: freeFunds(freeFundsPart),
    allocation: allocation(allocationPart),
  };
}

describe('Planning', () => {
  let harness: RouterTestingHarness;
  let component: Planning;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let responses: Record<string, Record<string, unknown>>;
  let overdueItems: Record<string, unknown>[];

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
    responses = {};
    overdueItems = [];
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  async function setup(url = '/planning'): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const requested = /\/planning\/months\/(\d{4}-\d{2})/.exec(requestUrl(input));
      const method = (input as Request).method;
      if (method === 'DELETE' || method === 'PUT' || method === 'POST') {
        return new Response(null, { status: 204 });
      }
      if (requestUrl(input).includes('/planning/overdue')) {
        return jsonResponse({ items: overdueItems });
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
      balance: balance({ planPln: 11050 }),
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
    expect(rows('balance-incomplete')).toHaveLength(1);
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
      balance: balance({ planPln: 10800 }),
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

  function overdueRequests(): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) =>
      requestUrl(call[0]).includes('/planning/overdue'),
    ).length;
  }

  it('lists overdue occurrences in a section with settle and skip, highlights them in the month and shows the balance', async () => {
    overdueItems = [
      overdueItem('800+', '2026-09-10', 800),
      overdueItem('Pensja', '2026-09-12', 500),
    ];
    responses['2026-10'] = month({
      occurrences: [
        occurrence('Pensja', '2026-10-05', 500, { overdue: true }),
        occurrence('Wypłata', '2026-10-30', 9200),
      ],
      balance: balance({ planPln: 9700, actualPln: 0, pendingPln: 9700, differencePln: -56.73 }),
    });
    await setup();

    const section = rows('overdue-section');
    expect(section).toHaveLength(1);
    const overdueRows = rows('overdue-row');
    expect(overdueRows).toHaveLength(2);
    expect(overdueRows[0].textContent).toContain('800+');
    expect(overdueRows[0].textContent).toContain('mBank ROR');
    expect(overdueRows[0].textContent).toContain(formatMoney(800, 'PLN'));
    expect(overdueRows[0].querySelector('[data-testid="settle-overdue"]')).not.toBeNull();
    expect(overdueRows[0].querySelector('[data-testid="skip-overdue"]')).not.toBeNull();

    overdueRows[0].querySelector<HTMLElement>('[data-testid="settle-overdue"]')!.click();
    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      SettleOccurrenceDialog,
      expect.objectContaining({
        data: {
          occurrence: expect.objectContaining({
            recurringIncomeId: 'id-800+',
            nominalMonth: '2026-09',
          }),
        },
      }),
    );

    const [overdueRow, regularRow] = rows('occurrence-row');
    expect(overdueRow.classList.contains('planning-table__row--overdue')).toBe(true);
    expect(regularRow.classList.contains('planning-table__row--overdue')).toBe(false);

    const text = (testId: string): string => rows(testId)[0].textContent!;
    expect(text('balance-plan')).toContain(formatMoney(9700, 'PLN'));
    expect(text('balance-actual')).toContain(formatMoney(0, 'PLN'));
    expect(text('balance-pending')).toContain(formatMoney(9700, 'PLN'));
    expect(text('balance-difference')).toContain(formatMoney(-56.73, 'PLN'));
  });

  it('hides the overdue section when nothing is overdue', async () => {
    await setup();

    expect(rows('overdue-section')).toHaveLength(0);
    expect(rows('overdue-row')).toHaveLength(0);
  });

  it('reloads the overdue list after a settle and after a skip', async () => {
    overdueItems = [overdueItem('800+', '2026-09-10', 800)];
    await setup();
    const loaded = overdueRequests();
    expect(loaded).toBeGreaterThan(0);

    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    click('settle-overdue');
    await vi.waitFor(() => expect(overdueRequests()).toBeGreaterThan(loaded));
    const afterSettle = overdueRequests();

    click('skip-overdue');
    await vi.waitFor(() => expect(overdueRequests()).toBeGreaterThan(afterSettle));
  });

  it('marks nothing incomplete when every rate is known', async () => {
    responses['2026-10'] = month({
      occurrences: [occurrence('Wypłata żona', '2026-10-09', 6500)],
      balance: balance({ planPln: 6500 }),
    });
    await setup();

    expect(rows('balance-incomplete')).toHaveLength(0);
    expect(rows('archived-warning')).toHaveLength(0);
    expect(element().textContent).toContain(formatMoney(6500, 'PLN'));
  });

  it('renders the expenses payments, the monthly cost and the definitions with the reserve and the next payment of a multi-month interval', async () => {
    responses['2026-10'] = month(
      {},
      {
        payments: [
          {
            recurringExpenseId: 'expense-Życie',
            nominalMonth: '2026-10',
            date: '2026-10-01',
            name: 'Życie',
            amount: 3000,
          },
          {
            recurringExpenseId: 'expense-Rachunki',
            nominalMonth: '2026-10',
            date: '2026-10-10',
            name: 'Rachunki',
            amount: 1000,
          },
        ],
        costPln: 4200,
        definitions: [
          expenseDefinition('OC Toyota', {
            startDate: '2027-03-15',
            status: 'Upcoming',
            amount: 1200,
            intervalMonths: 12,
            dayOfMonth: 15,
            reservePln: 200,
            nextPayment: { date: '2027-03-15', amount: 1200 },
          }),
          expenseDefinition('Rachunki'),
        ],
      },
    );
    await setup();

    const payments = rows('expense-payment-row');
    expect(payments).toHaveLength(2);
    expect(payments[0].textContent).toContain('Życie');
    expect(payments[0].textContent).toContain(formatMoney(3000, 'PLN'));
    expect(payments[1].textContent).toContain('Rachunki');
    expect(rows('expense-cost')[0].textContent).toContain(formatMoney(4200, 'PLN'));

    const definitions = rows('expense-definition-row');
    expect(definitions).toHaveLength(2);
    expect(definitions[0].textContent).toContain('OC Toyota');
    expect(definitions[0].querySelector('[data-testid="expense-reserve"]')!.textContent).toContain(
      formatMoney(200, 'PLN'),
    );
    expect(
      definitions[0].querySelector('[data-testid="expense-next-payment"]')!.textContent,
    ).toContain('15');
    expect(definitions[1].querySelector('[data-testid="expense-next-payment"]')).toBeNull();
  });

  it('shows the next scheduled expense change with its month and amount and a delete action', async () => {
    responses['2026-10'] = month(
      {},
      {
        definitions: [
          expenseDefinition('OC Toyota', {
            startDate: '2027-03-15',
            intervalMonths: 12,
            dayOfMonth: 15,
            reservePln: 200,
            nextChange: expenseNextChange(),
          }),
          expenseDefinition('Rachunki'),
        ],
      },
    );
    await setup();

    const changes = rows('expense-next-change');
    expect(changes).toHaveLength(1);
    expect(changes[0].textContent).toContain('01.2027');
    expect(changes[0].textContent).toContain(formatMoney(1320, 'PLN'));
    expect(changes[0].querySelector('[data-testid="delete-expense-change"]')).not.toBeNull();
  });

  it('opens the expense edit dialog with the definition of the viewed month', async () => {
    responses['2026-10'] = month(
      {},
      { definitions: [expenseDefinition('OC Toyota', { effectiveFrom: '2026-01', amount: 1200 })] },
    );
    await setup();

    click('edit-expense');

    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      RecurringExpenseFormDialog,
      expect.objectContaining({
        data: { definition: expect.objectContaining({ id: 'expense-OC Toyota' }) },
      }),
    );
  });

  it('deletes the scheduled expense change after confirmation and reloads the month', async () => {
    responses['2026-10'] = month(
      {},
      { definitions: [expenseDefinition('Najem', { nextChange: expenseNextChange() })] },
    );
    await setup();
    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);

    click('delete-expense-change');

    await vi.waitFor(() => {
      const deletes = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'DELETE');
      expect(deletes).toHaveLength(1);
      expect(deletes[0].url).toContain('/recurring-expenses/expense-Najem/changes/2027-01');
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));
  });

  it('opens the add expense dialog from the Wydatki section', async () => {
    await setup();

    click('add-expense');

    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      RecurringExpenseFormDialog,
      expect.anything(),
    );
  });

  it('shows the free funds plan and forecast, a negative value as a deficit and the incomplete marker', async () => {
    responses['2026-10'] = month({}, {}, { planPln: 12300, forecastPln: 11443.27 });
    await setup();

    expect(rows('free-funds-plan')[0].textContent).toContain(formatMoney(12300, 'PLN'));
    expect(rows('free-funds-forecast')[0].textContent).toContain(formatMoney(11443.27, 'PLN'));
    expect(rows('free-funds-deficit')).toHaveLength(0);
    expect(rows('free-funds-incomplete')).toHaveLength(0);

    responses['2026-11'] = month({}, {}, { planPln: -700, forecastPln: 50, incomplete: true });
    click('next-month');
    await settle();

    expect(rows('free-funds-plan')[0].textContent).toContain(formatMoney(-700, 'PLN'));
    const deficits = rows('free-funds-deficit');
    expect(deficits).toHaveLength(1);
    expect(deficits[0].textContent).toContain(formatMoney(-700, 'PLN'));
    expect(rows('free-funds-incomplete')).toHaveLength(1);
  });

  it('renders the allocation groups with the underfunded and remainder markers, the deficit message, the amount to invest and the next change', async () => {
    const buckets = [
      ...firstPlanBuckets(),
      allocationBucket('Remont', {
        kind: 1,
        shareType: 0,
        share: 15000,
        position: 1,
        allocatedPln: 11743,
        underfunded: true,
      }),
    ];
    responses['2026-10'] = month(
      {},
      {},
      { planPln: 12243.27, forecastPln: 12243.27 },
      {
        effectiveFrom: '2026-10',
        nextChange: '2027-01',
        forecastPln: 12243.27,
        incomplete: true,
        toInvestPln: 9395.27,
        buckets,
      },
    );
    await setup();

    expect(rows('allocation-section')).toHaveLength(1);
    const fixedRows = rows('allocation-group-fixed')[0].querySelectorAll(
      '[data-testid="allocation-bucket-row"]',
    );
    const percentRows = rows('allocation-group-percent')[0].querySelectorAll(
      '[data-testid="allocation-bucket-row"]',
    );
    expect(Array.from(fixedRows).map((row) => row.textContent)).toEqual([
      expect.stringContaining('Wakacje'),
      expect.stringContaining('Remont'),
    ]);
    expect(Array.from(percentRows).map((row) => row.textContent)).toEqual([
      expect.stringContaining('Akcje'),
      expect.stringContaining('Lokata'),
      expect.stringContaining('ETF'),
    ]);
    expect(fixedRows[0].textContent).toContain(formatMoney(500, 'PLN'));
    expect(percentRows[2].textContent).toContain(formatMoney(3524.27, 'PLN'));
    expect(rows('allocation-underfunded')).toHaveLength(1);
    expect(fixedRows[1].querySelector('[data-testid="allocation-underfunded"]')).not.toBeNull();
    expect(rows('allocation-remainder')).toHaveLength(1);
    expect(percentRows[2].querySelector('[data-testid="allocation-remainder"]')).not.toBeNull();
    expect(rows('allocation-to-invest')[0].textContent).toContain(formatMoney(9395.27, 'PLN'));
    expect(rows('allocation-incomplete')).toHaveLength(1);
    expect(rows('allocation-deficit')).toHaveLength(0);
    expect(rows('allocation-next-change')[0].textContent).toContain('01.2027');
    expect(rows('delete-allocation-change')).toHaveLength(1);

    responses['2026-11'] = month(
      {},
      {},
      {},
      {
        effectiveFrom: '2026-10',
        forecastPln: -756.73,
        deficitPln: 756.73,
        buckets: firstPlanBuckets().map((bucket) => ({ ...bucket, allocatedPln: 0 })),
      },
    );
    click('next-month');
    await settle();

    expect(rows('allocation-deficit')[0].textContent).toContain(formatMoney(756.73, 'PLN'));
    expect(rows('allocation-incomplete')).toHaveLength(0);
    expect(rows('allocation-next-change')).toHaveLength(0);
  });

  it('shows the read-only reserve row above the buckets with the month amount, the balance and the account and no actions', async () => {
    responses['2026-10'] = month(
      {},
      {},
      { planPln: 12243.27, forecastPln: 12243.27 },
      {
        effectiveFrom: '2026-10',
        forecastPln: 12243.27,
        toInvestPln: 9395.27,
        buckets: firstPlanBuckets(),
        reserve: {
          monthPln: 200,
          balancePln: 600,
          accountId,
          accountName: 'Konto oszczędnościowe',
        },
      },
    );
    await setup();

    const reserve = rows('reserve-row');
    expect(reserve).toHaveLength(1);
    expect(reserve[0].textContent).toContain(formatMoney(200, 'PLN'));
    expect(reserve[0].textContent).toContain(formatMoney(600, 'PLN'));
    expect(reserve[0].textContent).toContain('Konto oszczędnościowe');
    expect(reserve[0].querySelectorAll('button')).toHaveLength(0);
    const firstGroup = rows('allocation-group-fixed')[0];
    expect(
      reserve[0].compareDocumentPosition(firstGroup) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('hides the reserve row when the month has no reserve', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      { buckets: firstPlanBuckets(), forecastPln: 0, toInvestPln: 0 },
    );
    await setup();

    expect(rows('allocation-section')).toHaveLength(1);
    expect(rows('reserve-row')).toHaveLength(0);
  });

  it('renders the cushion first without a share, with its balance against the target and the reached marker', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {
        effectiveFrom: '2026-10',
        buckets: [cushionBucket(), ...firstPlanBuckets()],
      },
    );
    await setup();

    const cushionRows = rows('allocation-group-cushion');
    expect(cushionRows).toHaveLength(1);
    const allRows = rows('allocation-bucket-row');
    expect(allRows[0].textContent).toContain('Poduszka');
    expect(cushionRows[0].contains(allRows[0])).toBe(true);
    expect(allRows[0].textContent).toContain(formatMoney(5200, 'PLN'));
    const balance = rows('allocation-cushion-balance');
    expect(balance).toHaveLength(1);
    expect(balance[0].textContent).toContain(formatMoney(20000, 'PLN'));
    expect(balance[0].textContent).toContain(formatMoney(25200, 'PLN'));
    expect(rows('allocation-cushion-reached')).toHaveLength(0);
    expect(rows('allocation-remainder')).toHaveLength(1);

    responses['2026-11'] = month(
      {},
      {},
      {},
      {
        effectiveFrom: '2026-10',
        buckets: [
          cushionBucket({
            allocatedPln: 0,
            cushion: {
              balanceStartPln: 25200,
              targetPln: 25200,
              targetMonths: 6,
              balanceEndPln: 25200,
              reached: true,
            },
          }),
          ...firstPlanBuckets(),
        ],
      },
    );
    click('next-month');
    await settle();

    expect(rows('allocation-cushion-reached')).toHaveLength(1);
  });

  it('shows a limited bucket with its contributed against the limit, the reached marker and the estimated warning', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {
        effectiveFrom: '2026-10',
        buckets: [limitedBucket('IKE', { position: 0, share: 50 }), ...firstPlanBuckets()],
      },
    );
    await setup();

    const limit = rows('allocation-limit');
    expect(limit).toHaveLength(1);
    expect(limit[0].textContent).toContain(formatMoney(28260, 'PLN'));
    expect(limit[0].textContent).toContain('2026');
    expect(rows('allocation-limit-reached')).toHaveLength(1);
    expect(rows('allocation-limit-estimated')).toHaveLength(0);

    responses['2026-11'] = month(
      {},
      {},
      {},
      {
        effectiveFrom: '2026-10',
        buckets: [
          limitedBucket('IKE', {
            allocatedPln: 5871,
            limitReached: false,
            limit: {
              year: 2027,
              limitPln: 28260,
              contributedStartPln: 0,
              contributedEndPln: 5871,
              reached: false,
              estimated: true,
            },
          }),
          ...firstPlanBuckets(),
        ],
      },
    );
    click('next-month');
    await settle();

    expect(rows('allocation-limit')[0].textContent).toContain(formatMoney(5871, 'PLN'));
    expect(rows('allocation-limit-reached')).toHaveLength(0);
    expect(rows('allocation-limit-estimated')).toHaveLength(1);
    expect(rows('allocation-limit-estimated')[0].textContent).toContain('2027');
  });

  it('shows no buckets and an edit action when no plan is in force, and opens the plan dialog for the viewed month', async () => {
    responses['2026-10'] = month({}, {}, {}, allocation());
    await setup();

    expect(rows('allocation-section')).toHaveLength(1);
    expect(rows('allocation-bucket-row')).toHaveLength(0);

    click('edit-allocation');

    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      AllocationPlanDialog,
      expect.objectContaining({
        data: expect.objectContaining({ month: '2026-10' }),
      }),
    );
  });

  it('openAllocationDialog_WideDialog_LiftsMaterialMaxWidth', async () => {
    responses['2026-10'] = month({}, {}, {}, allocation());
    await setup();

    click('edit-allocation');

    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      AllocationPlanDialog,
      expect.objectContaining({
        width: '960px',
        maxWidth: expect.anything(),
      }),
    );
  });

  it('deletes the scheduled allocation change after confirmation and reloads the month', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      { effectiveFrom: '2026-10', nextChange: '2027-01', buckets: firstPlanBuckets() },
    );
    await setup();
    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);

    click('delete-allocation-change');

    await vi.waitFor(() => {
      const deletes = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'DELETE');
      expect(deletes).toHaveLength(1);
      expect(deletes[0].url).toContain('/allocation-plan/versions/2027-01');
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));
  });

  it('renders the transfer plan rows with the three markers, the groups table, the fallback link, the nothing-to-invest message and saves the minimum', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        toInvestPln: 6543.27,
        rows: [
          transferRow('IKE', 2260, {
            accountName: 'IKE mBank',
            groupName: 'Akcje/ETF',
            marker: 'AboveTarget',
          }),
          transferRow('Kasa', 300, { marker: 'OutsideAllocation' }),
          transferRow('Inwestycje', 1979.27, { groupName: 'Obligacje', marker: 'BelowMinimum' }),
        ],
        groups: [
          transferGroup('Akcje/ETF', 60.4, 59.5, 50),
          transferGroup('Obligacje', 33, 33.7, 40),
        ],
      },
    );
    await setup();

    const planRows = rows('transfer-plan-row');
    expect(rows('transfer-plan-section')).toHaveLength(1);
    expect(planRows).toHaveLength(3);
    expect(planRows[0].textContent).toContain('IKE mBank');
    expect(planRows[0].textContent).toContain('Akcje/ETF');
    expect(planRows[0].textContent).toContain(formatMoney(2260, 'PLN'));
    expect(
      planRows[0].querySelector('[data-testid="transfer-marker-above-target"]'),
    ).not.toBeNull();
    expect(
      planRows[1].querySelector('[data-testid="transfer-marker-outside-allocation"]'),
    ).not.toBeNull();
    expect(
      planRows[2].querySelector('[data-testid="transfer-marker-below-minimum"]'),
    ).not.toBeNull();
    expect(rows('transfer-group-row')).toHaveLength(2);
    expect(rows('transfer-group-row')[0].textContent).toContain(formatPercent(59.5));
    expect(rows('transfer-plan-fallback')).toHaveLength(0);

    const input = rows('transfer-minimum-input')[0] as HTMLInputElement;
    input.value = '250';
    input.dispatchEvent(new Event('change'));

    await vi.waitFor(async () => {
      const puts = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'PUT');
      expect(puts).toHaveLength(1);
      expect(puts[0].url).toContain('/transfer-plans/settings');
      expect(await puts[0].clone().json()).toEqual({ minimumTransferPln: 250 });
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));
  });

  it('renders the nothing-to-invest message instead of rows', async () => {
    responses['2026-10'] = month({}, {}, {}, {}, { status: 'None', noneReason: 'NothingToInvest' });
    await setup();

    expect(rows('transfer-plan-section')).toHaveLength(1);
    expect(rows('transfer-plan-nothing-to-invest')).toHaveLength(1);
    expect(rows('transfer-plan-row')).toHaveLength(0);
  });

  it('shows the fallback with a link to the target allocation', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        fallbackReason: 'NoTargetAllocation',
        rows: [
          transferRow('IKE', 2260, { accountName: 'IKE mBank' }),
          transferRow('Inwestycje', 1979.27),
        ],
      },
    );
    await setup();

    expect(rows('transfer-plan-row')).toHaveLength(2);
    expect(rows('transfer-plan-fallback')).toHaveLength(1);
    expect(rows('transfer-plan-set-target-link')[0].getAttribute('href')).toBe('/allocation');
    expect(rows('transfer-group-row')).toHaveLength(0);
  });

  it('offers the balance action on cushion and limited buckets only, lists their adjustments and deletes one after confirmation', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {
        effectiveFrom: '2026-10',
        buckets: [
          cushionBucket({
            adjustments: [
              { month: '2026-10', amountPln: 20000 },
              { month: '2026-12', amountPln: 20000 },
            ],
          }),
          limitedBucket('IKE', { position: 0, share: 50 }),
          ...firstPlanBuckets(),
        ],
      },
    );
    await setup();

    const bucketRow = (name: string): HTMLElement =>
      rows('allocation-bucket-row').find((row) => row.textContent!.includes(name))!;
    expect(
      bucketRow('Poduszka').querySelector('[data-testid="set-bucket-balance"]'),
    ).not.toBeNull();
    expect(bucketRow('IKE').querySelector('[data-testid="set-bucket-balance"]')).not.toBeNull();
    for (const name of ['Wakacje', 'Akcje', 'Lokata', 'ETF']) {
      expect(bucketRow(name).querySelector('[data-testid="set-bucket-balance"]')).toBeNull();
    }
    const adjustments = rows('bucket-adjustment');
    expect(adjustments).toHaveLength(2);
    expect(adjustments[1].textContent).toContain('2026');
    expect(adjustments[1].textContent).toContain(formatMoney(20000, 'PLN'));

    (bucketRow('IKE').querySelector('[data-testid="set-bucket-balance"]') as HTMLElement).click();
    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      BucketBalanceDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          month: '2026-10',
          bucket: expect.objectContaining({ name: 'IKE' }),
        }),
      }),
    );

    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    (
      adjustments[1].querySelector('[data-testid="delete-bucket-adjustment"]') as HTMLElement
    ).click();

    await vi.waitFor(() => {
      const deletes = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'DELETE');
      expect(deletes).toHaveLength(1);
      expect(deletes[0].url).toContain('/allocation-buckets/bucket-Poduszka/balances/2026-12');
    });
  });

  function transferRequests(method: string): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter(
        (request: Request) => request.method === method && /transfer-plans/.test(request.url),
      );
  }

  function frozenRow(name: string, id: string, isChecked: boolean): Record<string, unknown> {
    return transferRow(name, 1000, { id, batchNumber: 1, checked: isChecked });
  }

  it('accepts or skips a preview and reloads the month', async () => {
    responses['2026-10'] = month({}, {}, {}, {}, { rows: [transferRow('IKE', 2260)] });
    await setup();

    expect(rows('transfer-plan-status')[0].textContent).toContain('Preview');
    expect(rows('transfer-undo')).toHaveLength(0);
    expect(rows('transfer-unskip')).toHaveLength(0);
    expect(rows('transfer-row-check')).toHaveLength(0);

    click('transfer-accept');
    await vi.waitFor(() => {
      const posts = transferRequests('POST');
      expect(posts).toHaveLength(1);
      expect(posts[0].url).toContain('/transfer-plans/2026-10/accept');
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));

    click('transfer-skip');
    await vi.waitFor(() => {
      const posts = transferRequests('POST');
      expect(posts).toHaveLength(2);
      expect(posts[1].url).toContain('/transfer-plans/2026-10/skip');
    });
    await vi.waitFor(() => expect(requestedMonths()).toHaveLength(3));
  });

  it('shows an accepted plan with a checkbox per row, checks a row and undoes the acceptance after confirmation', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        status: 'Accepted',
        canUndo: true,
        rows: [frozenRow('IKE', 'row-1', false), frozenRow('IKZE', 'row-2', true)],
      },
    );
    await setup();

    const boxes = rows('transfer-row-check') as HTMLInputElement[];
    expect(rows('transfer-plan-status')[0].textContent).toContain('Accepted');
    expect(rows('transfer-plan-row')).toHaveLength(2);
    expect(boxes.map((box) => box.checked)).toEqual([false, true]);
    expect(rows('transfer-accept')).toHaveLength(0);
    expect(rows('transfer-minimum-input')).toHaveLength(0);
    expect((rows('transfer-undo')[0] as HTMLButtonElement).disabled).toBe(false);
    expect(rows('transfer-undo-hint')).toHaveLength(0);

    boxes[0].checked = true;
    boxes[0].dispatchEvent(new Event('change'));
    await vi.waitFor(async () => {
      const puts = transferRequests('PUT');
      expect(puts).toHaveLength(1);
      expect(puts[0].url).toContain('/transfer-plans/rows/row-1/checked');
      expect(await puts[0].clone().json()).toEqual({ checked: true });
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));

    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    click('transfer-undo');
    await vi.waitFor(() => {
      const posts = transferRequests('POST');
      expect(posts).toHaveLength(1);
      expect(posts[0].url).toContain('/transfer-plans/2026-10/undo');
    });
    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      ConfirmDialog,
      expect.objectContaining({ data: expect.objectContaining({ destructive: true }) }),
    );
  });

  it('does not undo the acceptance when the confirmation is declined and disables undo while a row is checked', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        status: 'Accepted',
        canUndo: false,
        rows: [frozenRow('IKE', 'row-1', true), frozenRow('IKZE', 'row-2', false)],
      },
    );
    await setup();

    expect((rows('transfer-undo')[0] as HTMLButtonElement).disabled).toBe(true);
    expect(rows('transfer-undo-hint')).toHaveLength(1);

    component['undoTransferPlan']();
    await settle();

    expect(transferRequests('POST')).toHaveLength(0);
  });

  it('shows a completed plan as completed', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        status: 'Completed',
        canUndo: false,
        rows: [frozenRow('IKE', 'row-1', true)],
      },
    );
    await setup();

    expect(rows('transfer-plan-status')[0].textContent).toContain('Completed');
    expect((rows('transfer-row-check')[0] as HTMLInputElement).checked).toBe(true);
  });

  it('shows the differences, the top-up preview and the batches as separate groups, and accepts the top-up', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        status: 'Accepted',
        canUndo: true,
        rows: [
          frozenRow('IKE', 'row-1', true),
          transferRow('IKZE', 300, { id: 'row-2', batchNumber: 1, checked: false }),
          transferRow('Obligacje', 200, { id: 'row-3', batchNumber: 2, checked: false }),
        ],
        differences: [
          {
            bucketId: 'bucket-IKE',
            bucketName: 'IKE',
            acceptedPln: 1000,
            currentPln: 1200,
            differencePln: 200,
          },
          {
            bucketId: 'bucket-IKZE',
            bucketName: 'IKZE',
            acceptedPln: 300,
            currentPln: 250,
            differencePln: -50,
          },
        ],
        topUp: {
          minimumTransferPln: 100,
          valuationDate: '2026-10-14',
          fallbackReason: null,
          rows: [transferRow('IKE', 200, { marker: 'BelowMinimum' })],
          groups: [transferGroup('Akcje', 40, 45, 50)],
        },
      },
    );
    await setup();

    const titles = rows('transfer-batch-title').map((title) => title.textContent!.trim());
    expect(titles).toEqual(['Plan', 'Top-up 1']);
    expect(rows('transfer-batch').map((batch) => batch.querySelectorAll('li').length)).toEqual([
      2, 1,
    ]);

    const differences = rows('transfer-difference');
    expect(differences).toHaveLength(2);
    expect(differences[0].textContent).toContain(formatMoney(200, 'PLN'));
    expect(differences[0].querySelector('[data-testid="transfer-difference-info"]')).toBeNull();
    expect(differences[1].textContent).toContain(formatMoney(-50, 'PLN'));
    expect(differences[1].querySelector('[data-testid="transfer-difference-info"]')).not.toBeNull();

    const topUp = rows('transfer-top-up')[0];
    expect(topUp.querySelectorAll('[data-testid="transfer-plan-row"]')).toHaveLength(1);
    expect(topUp.querySelectorAll('[data-testid="transfer-row-check"]')).toHaveLength(0);
    expect(topUp.querySelector('[data-testid="transfer-marker-below-minimum"]')).not.toBeNull();
    expect(topUp.querySelectorAll('[data-testid="transfer-group-row"]')).toHaveLength(1);

    click('transfer-top-up-accept');
    await vi.waitFor(() => {
      const posts = transferRequests('POST');
      expect(posts).toHaveLength(1);
      expect(posts[0].url).toContain('/transfer-plans/2026-10/top-up/accept');
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));
  });

  it('shows no top-up block when the response has none', async () => {
    responses['2026-10'] = month(
      {},
      {},
      {},
      {},
      {
        status: 'Completed',
        rows: [frozenRow('IKE', 'row-1', true)],
        differences: [
          {
            bucketId: 'bucket-IKE',
            bucketName: 'IKE',
            acceptedPln: 1000,
            currentPln: 800,
            differencePln: -200,
          },
        ],
      },
    );
    await setup();

    expect(rows('transfer-top-up')).toHaveLength(0);
    expect(rows('transfer-difference')).toHaveLength(1);
    expect(rows('transfer-difference-info')).toHaveLength(1);
  });

  it('shows a skipped month without rows and unskips it', async () => {
    responses['2026-10'] = month({}, {}, {}, {}, { status: 'Skipped' });
    await setup();

    expect(rows('transfer-plan-status')[0].textContent).toContain('Skipped');
    expect(rows('transfer-plan-skipped')).toHaveLength(1);
    expect(rows('transfer-plan-row')).toHaveLength(0);
    expect(rows('transfer-accept')).toHaveLength(0);
    expect(rows('transfer-skip')).toHaveLength(0);

    click('transfer-unskip');
    await vi.waitFor(() => {
      const deletes = transferRequests('DELETE');
      expect(deletes).toHaveLength(1);
      expect(deletes[0].url).toContain('/transfer-plans/2026-10/skip');
    });
    await vi.waitFor(() => expect(requestedMonths()).toEqual(['2026-10', '2026-10']));
  });
});
