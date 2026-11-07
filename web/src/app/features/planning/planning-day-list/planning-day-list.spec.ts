import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import { bondResponse } from '../../../../testing/bond-fixtures';
import { depositResponse } from '../../deposits/testing/deposit-fixtures';
import { PlanningDayList } from './planning-day-list';

const accountId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const blueGroupId = 'c0000000-0000-0000-0000-000000000001';
const unknownGroupId = 'c0000000-0000-0000-0000-000000000009';

function occurrence(name: string, date: string, amount: number): Record<string, unknown> {
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
  };
}

function monthData(
  occurrences: Record<string, unknown>[],
  expenses: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    incomes: {
      occurrences,
      balance: { planPln: 0, actualPln: 0, pendingPln: 0, differencePln: 0 },
      fxUnavailable: false,
      definitions: [],
      oneOffs: [],
    },
    expenses: {
      payments: [],
      costPln: 0,
      definitions: [],
      oneOffs: [],
      groupContributions: [],
      ...expenses,
    },
    freeFunds: { planPln: 0, actualPln: 0, incomplete: false },
    nonBusinessDays: [],
    split: null,
  };
}

describe('PlanningDayList', () => {
  let fixture: ComponentFixture<PlanningDayList>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function render(
    data: Record<string, unknown>,
    extra: { deposits?: unknown[]; bonds?: unknown[]; groups?: unknown[] } = {},
  ): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      providers: [
        provideI18nTesting(),
        { provide: MatDialog, useValue: { open: vi.fn(() => ({ afterClosed: () => of(false) })) } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlanningDayList);
    fixture.componentRef.setInput('month', '2026-10');
    fixture.componentRef.setInput('data', data);
    fixture.componentRef.setInput('deposits', extra.deposits ?? []);
    fixture.componentRef.setInput('bonds', extra.bonds ?? []);
    fixture.componentRef.setInput('groups', extra.groups ?? []);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function all(root: HTMLElement, testId: string): HTMLElement[] {
    return Array.from(root.querySelectorAll<HTMLElement>(`[data-testid="${testId}"]`));
  }

  it('groups rows by day in date order', async () => {
    const root = await render(
      monthData(
        [occurrence('Wypłata', '2026-10-09', 6500), occurrence('Pensja', '2026-10-05', 500)],
        {
          payments: [
            {
              recurringExpenseId: 'expense-Czynsz',
              nominalMonth: '2026-10',
              date: '2026-10-05',
              name: 'Czynsz',
              amount: 1450,
            },
          ],
        },
      ),
    );

    const days = all(root, 'day-group');
    expect(days.map((day) => day.getAttribute('data-date'))).toEqual(['2026-10-05', '2026-10-09']);

    const firstDayRows = all(days[0], 'day-row');
    expect(firstDayRows).toHaveLength(2);
    expect(firstDayRows.map((row) => row.textContent).join('|')).toContain('Pensja');
    expect(firstDayRows.map((row) => row.textContent).join('|')).toContain('Czynsz');
    expect(days[0].textContent).toContain(formatMoney(1450, 'PLN'));

    const secondDayRows = all(days[1], 'day-row');
    expect(secondDayRows).toHaveLength(1);
    expect(secondDayRows[0].textContent).toContain('Wypłata');
    expect(secondDayRows[0].textContent).toContain(formatMoney(6500, 'PLN'));
    expect(secondDayRows[0].querySelector('mat-icon')).not.toBeNull();
    expect(root.querySelector('[role="list"], ul, ol')).not.toBeNull();
  });

  it('lists deposit and bond deadlines', async () => {
    const root = await render(monthData([]), {
      deposits: [
        depositResponse({
          name: 'Lokata Pekao',
          maturityDate: '2026-10-22',
          returnAccountName: 'Konto zwrotne',
          currency: 'PLN',
        }),
      ],
      bonds: [
        bondResponse({
          name: 'Obligacje EDO',
          maturityDate: '2026-10-28',
          accountName: 'Konto obligacji',
          bookValue: 5000,
        }),
      ],
    });

    const deadlines = all(root, 'deadline-row');
    expect(deadlines).toHaveLength(2);
    expect(deadlines[0].textContent).toContain('Lokata Pekao');
    expect(deadlines[0].textContent).toContain('Deposit ends');
    expect(deadlines[0].textContent).toContain('Konto zwrotne');
    expect(deadlines[0].textContent).toContain(formatMoney(10119.83, 'PLN'));
    expect(deadlines[1].textContent).toContain('Obligacje EDO');
    expect(deadlines[1].textContent).toContain('Bond matures');
    expect(deadlines[1].textContent).toContain('Konto obligacji');
    expect(deadlines[1].textContent).toContain(formatMoney(5000, 'PLN'));
    expect(deadlines[0].closest('[data-testid="day-group"]')!.getAttribute('data-date')).toBe(
      '2026-10-22',
    );
    expect(deadlines[0].querySelector('button')).toBeNull();
  });

  it('shows undated group contributions in the group colours', async () => {
    const root = await render(
      monthData([], {
        groupContributions: [
          {
            groupId: blueGroupId,
            name: 'Poduszka finansowa',
            amountPln: 1000,
            fundedPln: null,
            status: null,
          },
          {
            groupId: unknownGroupId,
            name: 'Emerytura',
            amountPln: 1500,
            fundedPln: null,
            status: null,
          },
        ],
      }),
      { groups: [{ id: blueGroupId, name: 'Poduszka finansowa', color: 'blue' }] },
    );

    const block = all(root, 'no-date-block');
    expect(block).toHaveLength(1);
    expect(block[0].textContent).toContain('no date');

    const lines = all(root, 'contribution-row');
    expect(lines).toHaveLength(2);
    expect(lines[0].textContent).toContain('Poduszka finansowa');
    expect(lines[0].textContent).toContain('Group contribution');
    expect(lines[0].textContent).toContain(formatMoney(-1000, 'PLN'));
    expect(lines[0].innerHTML).toContain('var(--sk-g-blue)');
    expect(lines[1].textContent).toContain(formatMoney(-1500, 'PLN'));
    expect(lines[1].innerHTML).toContain('var(--sk-g-none)');
  });

  function expensePayment(extra: Record<string, unknown> = {}): Record<string, unknown> {
    return {
      recurringExpenseId: 'expense-Czynsz',
      nominalMonth: '2026-10',
      date: '2026-10-05',
      name: 'Czynsz',
      amount: 2400,
      status: 'Pending',
      accountId,
      accountName: 'mBank ROR',
      plannedAccountId: accountId,
      currency: 'PLN',
      actualAmount: null,
      actualDate: null,
      actualAmountPln: null,
      difference: null,
      withdrawalCreated: false,
      ...extra,
    };
  }

  const settledPayment = {
    status: 'Settled',
    actualAmount: 2350,
    actualDate: '2026-10-06',
    actualAmountPln: 2350,
    difference: -50,
    withdrawalCreated: true,
  };

  it('shows settle for a pending expense payment', async () => {
    const root = await render(
      monthData([], {
        payments: [expensePayment()],
        oneOffs: [
          {
            id: 'one-off-expense-1',
            name: 'Abonament',
            date: '2026-10-07',
            amount: 100,
            inMonth: true,
            accountId: null,
            accountName: null,
            status: 'Pending',
            currency: null,
            actualAmount: null,
            actualDate: null,
            actualAmountPln: null,
            difference: null,
            withdrawalCreated: false,
          },
        ],
      }),
    );
    const settleRequests: unknown[] = [];
    fixture.componentInstance.settleExpense.subscribe((row) => settleRequests.push(row));

    const rows = all(root, 'expense-payment-row');
    expect(rows).toHaveLength(2);
    for (const row of rows) {
      expect(row.querySelector('[data-testid="settle-expense"]')).not.toBeNull();
      expect(row.querySelector('[data-testid="edit-expense-settlement"]')).toBeNull();
      expect(row.querySelector('[data-testid="undo-expense"]')).toBeNull();
    }

    rows[0].querySelector<HTMLElement>('[data-testid="settle-expense"]')!.click();
    rows[1].querySelector<HTMLElement>('[data-testid="settle-expense"]')!.click();

    expect(settleRequests).toEqual([
      expect.objectContaining({
        oneOff: false,
        item: expect.objectContaining({ recurringExpenseId: 'expense-Czynsz' }),
      }),
      expect.objectContaining({
        oneOff: true,
        item: expect.objectContaining({ id: 'one-off-expense-1' }),
      }),
    ]);
  });

  it('shows settled expense', async () => {
    const root = await render(monthData([], { payments: [expensePayment(settledPayment)] }));

    const [row] = all(root, 'expense-payment-row');
    expect(row.querySelector('[data-testid="expense-status"]')!.textContent).toMatch(/settled/i);
    expect(row.querySelector('[data-testid="expense-actual"]')!.textContent).toContain(
      formatMoney(2350, 'PLN'),
    );
    expect(row.querySelector('[data-testid="expense-difference"]')!.textContent).toContain(
      formatMoney(-50, 'PLN'),
    );
    expect(row.querySelector('[data-testid="settle-expense"]')).toBeNull();
    expect(row.querySelector('[data-testid="edit-expense-settlement"]')).not.toBeNull();
    expect(row.querySelector('[data-testid="undo-expense"]')).not.toBeNull();
  });

  it('undo calls the endpoint', async () => {
    const root = await render(monthData([], { payments: [expensePayment(settledPayment)] }));
    const undone: unknown[] = [];
    fixture.componentInstance.undoExpense.subscribe((row) => undone.push(row));

    all(root, 'undo-expense')[0].click();

    expect(undone).toEqual([
      expect.objectContaining({
        oneOff: false,
        item: expect.objectContaining({ recurringExpenseId: 'expense-Czynsz' }),
      }),
    ]);
  });

  it('edit opens a filled dialog', async () => {
    const root = await render(monthData([], { payments: [expensePayment(settledPayment)] }));
    const requests: unknown[] = [];
    fixture.componentInstance.settleExpense.subscribe((row) => requests.push(row));

    all(root, 'edit-expense-settlement')[0].click();

    expect(requests).toEqual([
      expect.objectContaining({
        oneOff: false,
        item: expect.objectContaining({ status: 'Settled', actualAmount: 2350 }),
      }),
    ]);
  });
});
