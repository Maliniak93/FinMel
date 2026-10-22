import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatDate, formatMoney } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
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
  return {
    payments: [],
    costPln: 0,
    definitions: [],
    oneOffs: [],
    groupContributions: [],
    ...extra,
  };
}

function freeFunds(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { planPln: 0, forecastPln: 0, incomplete: false, ...extra };
}

function month(
  incomes: Record<string, unknown>,
  expensesPart: Record<string, unknown> = {},
  freeFundsPart: Record<string, unknown> = {},
  split: Record<string, unknown> | null = null,
): Record<string, unknown> {
  return {
    incomes: {
      occurrences: [],
      balance: balance(),
      fxUnavailable: false,
      definitions: [],
      oneOffs: [],
      ...incomes,
    },
    expenses: expenses(expensesPart),
    freeFunds: freeFunds(freeFundsPart),
    nonBusinessDays: [],
    split,
  };
}

const cushionId = 'c0000000-0000-0000-0000-000000000001';
const retirementId = 'c0000000-0000-0000-0000-000000000002';
const investingId = 'c0000000-0000-0000-0000-000000000003';

function splitView(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    freeFundsPln: 4000,
    deficitPln: null,
    cushion: {
      groupId: cushionId,
      name: 'Poduszka',
      levelPln: 10500,
      targetPln: 12000,
      missingPln: 1500,
      allocationPln: 1500,
    },
    groups: [
      {
        groupId: retirementId,
        name: 'Emerytura',
        sharePercent: 50,
        contributionPln: 2000,
        allocationPln: 1250,
        breakdown: [
          { assetClass: 1, amountPln: 0 },
          { assetClass: 9, amountPln: 2000 },
        ],
      },
      {
        groupId: investingId,
        name: 'Inwestycje',
        sharePercent: 50,
        contributionPln: 1000,
        allocationPln: 1250,
        breakdown: [
          { assetClass: 1, amountPln: 500 },
          { assetClass: 9, amountPln: 500 },
        ],
      },
    ],
    unassignedPln: 0,
    valuationMissing: false,
    ...extra,
  };
}

const contributions = [
  { groupId: retirementId, name: 'Emerytura', amountPln: 2000 },
  { groupId: investingId, name: 'Inwestycje', amountPln: 1000 },
];

function yearPage(year: number): Record<string, unknown> {
  const months = Array.from({ length: 12 }, (_, index) => ({
    month: `${year}-${String(index + 1).padStart(2, '0')}`,
    hasDefinitions: true,
    incomePlanPln: 16500,
    incomeActualPln: null,
    differencePln: 0,
    costPln: 4200,
    freeFundsPlanPln: 12300,
    freeFundsForecastPln: 12300,
    hasOverdue: false,
    incomplete: false,
  }));

  return {
    year,
    firstYear: 2026,
    lastYear: 2027,
    months,
    totals: {
      incomePlanPln: 198000,
      incomeActualPln: 0,
      differencePln: 0,
      costPln: 50400,
      freeFundsPlanPln: 147600,
      freeFundsForecastPln: 147600,
    },
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
      if (requestUrl(input).includes('/planning/todo')) {
        return jsonResponse({ items: [], paymentLeadDays: 7 });
      }
      const requestedYear = /\/planning\/years\/(\d{4})/.exec(requestUrl(input));
      if (requestedYear) {
        return jsonResponse(yearPage(Number(requestedYear[1])));
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

  function requestedYears(): string[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => /\/planning\/years\/(\d{4})/.exec(requestUrl(call[0]))?.[1])
      .filter((value: string | undefined): value is string => value !== undefined);
  }

  function tabs(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[role="tab"]'));
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

  function oneOffIncome(
    name: string,
    date: string,
    amount: number,
    extra: Record<string, unknown> = {},
  ): Record<string, unknown> {
    return {
      id: `one-off-${name}`,
      name,
      date,
      accountId,
      accountName: 'mBank ROR',
      accountArchived: false,
      amount,
      currency: 'PLN',
      amountPln: amount,
      estimated: false,
      inMonth: true,
      status: 'Pending',
      actualAmount: null,
      actualDate: null,
      actualAmountPln: null,
      difference: null,
      depositCreated: false,
      overdue: false,
      ...extra,
    };
  }

  function oneOffExpense(
    name: string,
    date: string,
    amount: number,
    extra: Record<string, unknown> = {},
  ): Record<string, unknown> {
    return { id: `one-off-expense-${name}`, name, date, amount, inMonth: true, ...extra };
  }

  function deleteRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method === 'DELETE');
  }

  it('lists one-off entries with their date', async () => {
    responses['2026-10'] = month(
      { oneOffs: [oneOffIncome('Premia roczna', '2026-12-15', 8000, { inMonth: false })] },
      { oneOffs: [oneOffExpense('Przegląd auta', '2027-02-10', 1200, { inMonth: false })] },
    );
    await setup();

    const incomeRows = rows('one-off-income-row');
    expect(incomeRows).toHaveLength(1);
    expect(incomeRows[0].textContent).toContain('Premia roczna');
    expect(incomeRows[0].textContent).toContain(formatMoney(8000, 'PLN'));
    expect(incomeRows[0].querySelector('[data-testid="one-off-mark"]')).not.toBeNull();
    expect(incomeRows[0].querySelector('[data-testid="one-off-date"]')!.textContent).toContain(
      formatDate('2026-12-15'),
    );
    expect(incomeRows[0].querySelector('[data-testid="edit-one-off-income"]')).not.toBeNull();
    expect(incomeRows[0].querySelector('[data-testid="delete-one-off-income"]')).not.toBeNull();

    const expenseRows = rows('one-off-expense-row');
    expect(expenseRows).toHaveLength(1);
    expect(expenseRows[0].textContent).toContain('Przegląd auta');
    expect(expenseRows[0].querySelector('[data-testid="one-off-mark"]')).not.toBeNull();
    expect(expenseRows[0].querySelector('[data-testid="one-off-date"]')!.textContent).toContain(
      formatDate('2027-02-10'),
    );
    expect(expenseRows[0].querySelector('[data-testid="edit-one-off-expense"]')).not.toBeNull();
    expect(expenseRows[0].querySelector('[data-testid="delete-one-off-expense"]')).not.toBeNull();

    expect(rows('occurrence-row')).toHaveLength(0);
    expect(rows('expense-payment-row')).toHaveLength(0);
  });

  it('settles and undoes a one-off income', async () => {
    responses['2026-10'] = month({
      oneOffs: [
        oneOffIncome('Zwrot podatku', '2026-10-25', 2500),
        oneOffIncome('Premia', '2026-10-28', 800, {
          status: 'Settled',
          actualAmount: 800,
          actualDate: '2026-10-28',
          actualAmountPln: 800,
          difference: 0,
          depositCreated: true,
        }),
      ],
    });
    await setup();

    const [pending, settled] = rows('occurrence-row');
    expect(pending.textContent).toContain('Zwrot podatku');
    expect(pending.querySelector('[data-testid="occurrence-status"]')!.textContent).toMatch(
      /pending/i,
    );
    expect(pending.querySelector('[data-testid="settle-occurrence"]')).not.toBeNull();
    expect(settled.querySelector('[data-testid="edit-settlement"]')).not.toBeNull();
    expect(settled.querySelector('[data-testid="undo-occurrence"]')).not.toBeNull();

    pending.querySelector<HTMLElement>('[data-testid="settle-occurrence"]')!.click();
    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      SettleOccurrenceDialog,
      expect.objectContaining({
        data: { oneOffIncome: expect.objectContaining({ id: 'one-off-Zwrot podatku' }) },
      }),
    );

    vi.mocked(TestBed.inject(MatDialog).open).mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    settled.querySelector<HTMLElement>('[data-testid="undo-occurrence"]')!.click();

    await vi.waitFor(() => {
      expect(deleteRequests()).toHaveLength(1);
      expect(deleteRequests()[0].url).toContain('/one-off-incomes/one-off-Premia/settlement');
    });
  });

  it('one-off expense has no settlement', async () => {
    responses['2026-10'] = month(
      {},
      {
        oneOffs: [
          oneOffExpense('Przegląd auta', '2026-10-10', 1200),
          oneOffExpense('Za miesiąc', '2026-11-10', 300, { inMonth: false }),
        ],
      },
    );
    await setup();

    const payments = rows('expense-payment-row');
    expect(payments).toHaveLength(1);
    expect(payments[0].textContent).toContain('Przegląd auta');
    expect(payments[0].textContent).toContain(formatMoney(1200, 'PLN'));
    expect(payments[0].querySelector('[data-testid="settle-occurrence"]')).toBeNull();
    expect(payments[0].querySelector('[data-testid="occurrence-status"]')).toBeNull();
    expect(rows('occurrence-row')).toHaveLength(0);
  });

  it('locks a settled one-off income', async () => {
    responses['2026-10'] = month({
      oneOffs: [
        oneOffIncome('Premia', '2026-10-28', 800, {
          status: 'Settled',
          actualAmount: 800,
          actualDate: '2026-10-28',
          actualAmountPln: 800,
        }),
        oneOffIncome('Zwrot podatku', '2026-10-25', 2500),
      ],
    });
    await setup();

    const [settled, pending] = rows('one-off-income-row');
    const edit = (row: HTMLElement): HTMLButtonElement =>
      row.querySelector<HTMLButtonElement>('[data-testid="edit-one-off-income"]')!;
    const remove = (row: HTMLElement): HTMLButtonElement =>
      row.querySelector<HTMLButtonElement>('[data-testid="delete-one-off-income"]')!;

    expect(edit(settled).disabled).toBe(true);
    expect(remove(settled).disabled).toBe(true);
    expect(settled.querySelector('[data-testid="one-off-locked-hint"]')).not.toBeNull();
    expect(edit(pending).disabled).toBe(false);
    expect(remove(pending).disabled).toBe(false);
    expect(pending.querySelector('[data-testid="one-off-locked-hint"]')).toBeNull();
  });

  it('shows an overdue one-off income', async () => {
    overdueItems = [
      {
        recurringIncomeId: null,
        nominalMonth: null,
        oneOffIncomeId: 'one-off-Zwrot podatku',
        date: '2026-10-05',
        name: 'Zwrot podatku',
        accountId,
        accountName: 'mBank ROR',
        accountArchived: false,
        amount: 2500,
        currency: 'PLN',
      },
    ];
    responses['2026-10'] = month({
      oneOffs: [oneOffIncome('Zwrot podatku', '2026-10-05', 2500, { overdue: true })],
    });
    await setup();

    expect(rows('overdue-section')).toHaveLength(1);
    const overdueRows = rows('overdue-row');
    expect(overdueRows).toHaveLength(1);
    expect(overdueRows[0].textContent).toContain('Zwrot podatku');
    expect(overdueRows[0].textContent).toContain(formatMoney(2500, 'PLN'));

    overdueRows[0].querySelector<HTMLElement>('[data-testid="settle-overdue"]')!.click();
    expect(TestBed.inject(MatDialog).open).toHaveBeenCalledWith(
      SettleOccurrenceDialog,
      expect.objectContaining({
        data: { oneOffIncome: expect.objectContaining({ id: 'one-off-Zwrot podatku' }) },
      }),
    );
    expect(rows('occurrence-row')[0].classList.contains('planning-table__row--overdue')).toBe(true);
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

  function todoRequests(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        requestUrl(call[0]).includes('/planning/todo') && (call[0] as Request).method === 'GET',
    ).length;
  }

  it('reloads the planning state after the lead days are saved', async () => {
    await setup();
    const loaded = todoRequests();
    expect(loaded).toBeGreaterThan(0);

    const input = rows('payment-lead-days-input')[0] as HTMLInputElement;
    expect(input.value).toBe('7');
    input.value = '14';
    input.dispatchEvent(new Event('change'));
    await vi.waitFor(async () => {
      const puts = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter(
          (request: Request) =>
            request.method === 'PUT' && request.url.includes('/planning/settings'),
        );
      expect(puts).toHaveLength(1);
      expect(await puts[0].clone().json()).toEqual({ paymentLeadDays: 14 });
    });
    await vi.waitFor(() => expect(todoRequests()).toBeGreaterThan(loaded));
  });

  it('scrolls to the section named in the URL fragment after the month loads', async () => {
    const scrolled: string[] = [];
    const scrollIntoView = vi.fn(function (this: HTMLElement) {
      scrolled.push(this.id);
    });
    Object.defineProperty(Element.prototype, 'scrollIntoView', {
      configurable: true,
      writable: true,
      value: scrollIntoView,
    });

    await setup('/planning?month=2026-10#wydatki');
    await vi.waitFor(() => expect(scrolled).toContain('wydatki'));

    expect(element().querySelector('#wydatki')).not.toBeNull();
  });

  it('opens the Rok tab from the query params and loads the requested year', async () => {
    await setup('/planning?tab=year&year=2027');

    expect(new Set(requestedYears())).toEqual(new Set(['2027']));
    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
  });

  it('shows the calendar from layout=calendar, drops layout on Lista and keeps it across months', async () => {
    await setup('/planning?month=2027-03&layout=calendar');

    expect(rows('calendar-cell').length).toBeGreaterThan(0);

    click('next-month');
    await vi.waitFor(async () => {
      await settle();
      expect(TestBed.inject(Router).url).toContain('month=2027-04');
    });
    expect(TestBed.inject(Router).url).toContain('layout=calendar');
    expect(rows('calendar-cell').length).toBeGreaterThan(0);

    click('layout-list');
    await vi.waitFor(async () => {
      await settle();
      expect(TestBed.inject(Router).url).not.toContain('layout');
    });
    expect(TestBed.inject(Router).url).toContain('month=2027-04');
    expect(rows('calendar-cell')).toHaveLength(0);
  });

  it('switches to the Rok tab and back, keeping the year in the query params', async () => {
    await setup();

    tabs()[1].click();
    await vi.waitFor(async () => {
      await settle();
      expect(TestBed.inject(Router).url).toContain('tab=year');
      expect(TestBed.inject(Router).url).toContain('year=2026');
      expect(new Set(requestedYears())).toEqual(new Set(['2026']));
    });

    tabs()[0].click();
    await vi.waitFor(async () => {
      await settle();
      expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
      expect(TestBed.inject(Router).url).not.toContain('tab=year');
    });
  });

  it('shows the contribution lines, the cushion state, the per-group amounts and expandable class breakdowns', async () => {
    responses['2026-10'] = month(
      {},
      { groupContributions: contributions, costPln: 4000 },
      { planPln: 4000, forecastPln: 4000 },
      splitView(),
    );
    await setup();

    const lines = rows('group-contribution');
    expect(lines).toHaveLength(2);
    expect(lines[0].textContent).toContain('Emerytura');
    expect(lines[0].textContent).toContain(formatMoney(2000, 'PLN'));
    expect(lines[1].textContent).toContain('Inwestycje');

    expect(rows('split-section')).toHaveLength(1);
    const cushion = rows('split-cushion')[0].textContent!;
    expect(cushion).toContain('Poduszka');
    expect(cushion).toContain(formatMoney(1500, 'PLN'));
    expect(cushion).toContain(formatMoney(12000, 'PLN'));

    const groups = rows('split-group');
    expect(groups).toHaveLength(2);
    expect(groups[0].textContent).toContain('Emerytura');
    expect(groups[0].textContent).toContain(formatMoney(1250, 'PLN'));
    expect(rows('split-deficit')).toHaveLength(0);
    expect(rows('split-valuation-missing')).toHaveLength(0);
    expect(rows('split-breakdown')).toHaveLength(0);

    rows('split-breakdown-toggle')[0].click();
    await settle();

    const parts = rows('split-breakdown');
    expect(parts).toHaveLength(2);
    expect(parts[1].textContent).toContain(formatMoney(2000, 'PLN'));
  });

  it('shows neither contribution lines nor a split in a past month', async () => {
    responses['2026-10'] = month(
      {},
      { groupContributions: contributions, costPln: 4000 },
      { planPln: 4000, forecastPln: 4000 },
      splitView(),
    );
    responses['2026-09'] = month({}, {}, { planPln: 7000, forecastPln: 7000 }, null);
    await setup();

    expect(rows('group-contribution')).toHaveLength(2);
    expect(rows('split-section')).toHaveLength(1);

    click('previous-month');
    await settle();

    expect(requestedMonths().at(-1)).toBe('2026-09');
    expect(rows('group-contribution')).toHaveLength(0);
    expect(rows('split-section')).toHaveLength(0);
    expect(rows('split-cushion')).toHaveLength(0);
  });

  it('shows the deficit instead of amounts and a notice when the valuation is missing', async () => {
    responses['2026-10'] = month(
      {},
      { groupContributions: contributions },
      { planPln: -500, forecastPln: -500 },
      splitView({
        freeFundsPln: -500,
        deficitPln: 500,
        valuationMissing: true,
        cushion: null,
      }),
    );
    await setup();

    const deficit = rows('split-deficit');
    expect(deficit).toHaveLength(1);
    expect(deficit[0].textContent).toContain(formatMoney(500, 'PLN'));
    expect(rows('split-valuation-missing')).toHaveLength(1);
    expect(rows('split-cushion')).toHaveLength(0);
  });

  it('blocks saving the shares unless they sum to 100 and saves the whole list', async () => {
    responses['2026-10'] = month(
      {},
      { groupContributions: contributions },
      { planPln: 4000, forecastPln: 4000 },
      splitView(),
    );
    await setup();

    const inputs = () => rows('share-input') as HTMLInputElement[];
    const save = () => element().querySelector<HTMLButtonElement>('[data-testid="shares-save"]')!;
    expect(inputs()).toHaveLength(2);
    expect(rows('share-sum')[0].textContent).toContain('100');
    expect(save().disabled).toBe(true);

    inputs()[0].value = '30';
    inputs()[0].dispatchEvent(new Event('input'));
    await settle();

    expect(rows('share-sum')[0].textContent).toContain('80');
    expect(save().disabled).toBe(true);

    inputs()[1].value = '70';
    inputs()[1].dispatchEvent(new Event('input'));
    await settle();

    expect(rows('share-sum')[0].textContent).toContain('100');
    expect(save().disabled).toBe(false);

    save().click();
    await settle();

    const put = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find(
        (request: Request) =>
          request.method === 'PUT' && request.url.includes('/asset-groups/shares'),
      )!;
    expect(put).toBeDefined();
    expect(await put.clone().json()).toEqual({
      shares: [
        { groupId: retirementId, sharePercent: 30 },
        { groupId: investingId, sharePercent: 70 },
      ],
    });
  });
});
