import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { declarationList, readStylesheet, ruleBodies } from '../../../../testing/stylesheet';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { PlanningState } from '../../../core/planning/planning-state';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatDate, formatMoney } from '../../../shared/format';
import { depositResponse } from '../../deposits/testing/deposit-fixtures';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { SettleExpenseDialog } from '../settle-expense-dialog/settle-expense-dialog';
import { SettleOccurrenceDialog } from '../settle-occurrence-dialog/settle-occurrence-dialog';
import { PlanningCalendar } from './planning-calendar';

const accountId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const MARCH_NON_BUSINESS_DAYS = [
  '2027-03-06',
  '2027-03-07',
  '2027-03-13',
  '2027-03-14',
  '2027-03-20',
  '2027-03-21',
  '2027-03-27',
  '2027-03-28',
  '2027-03-29',
];

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

function payment(name: string, date: string, amount: number): Record<string, unknown> {
  return {
    recurringExpenseId: `expense-${name}`,
    nominalMonth: date.slice(0, 7),
    date,
    name,
    amount,
  };
}

function oneOffIncome(name: string, date: string, planned: boolean): Record<string, unknown> {
  return {
    id: `one-off-${name}`,
    name,
    planned,
    date,
    accountId,
    accountName: 'mBank ROR',
    accountArchived: false,
    amount: 3000,
    currency: 'PLN',
    amountPln: 3000,
    estimated: false,
    inMonth: true,
    status: 'Pending',
    actualAmount: null,
    actualDate: null,
    actualAmountPln: null,
    difference: null,
    depositCreated: false,
    overdue: false,
  };
}

function monthData(
  occurrences: Record<string, unknown>[],
  payments: Record<string, unknown>[],
): Record<string, unknown> {
  return {
    incomes: {
      occurrences,
      balance: { planPln: 0, actualPln: 0, pendingPln: 0, differencePln: 0 },
      fxUnavailable: false,
      definitions: [],
      oneOffs: [],
    },
    expenses: { payments, costPln: 0, definitions: [], oneOffs: [] },
    nonBusinessDays: MARCH_NON_BUSINESS_DAYS,
  };
}

const PENDING = occurrence('Wypłata', '2027-03-10', 9000);
const SETTLED = occurrence('800+', '2027-03-26', 800, {
  status: 'Settled',
  actualAmount: 810,
  actualDate: '2027-03-25',
  actualAmountPln: 810,
});
const SKIPPED = occurrence('Premia', '2027-03-12', 500, { status: 'Skipped' });
const TOYOTA = payment('OC Toyota', '2027-03-15', 1200);
const DEPOSIT = depositResponse({ name: 'Lokata Pekao', maturityDate: '2027-03-22' });

describe('PlanningCalendar', () => {
  let fixture: ComponentFixture<PlanningCalendar>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialogOpen: ReturnType<typeof vi.fn>;
  let stateReload: ReturnType<typeof vi.fn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2027, 2, 5, 12));
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  async function render(data: Record<string, unknown>, month = '2027-03'): Promise<void> {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => jsonResponse({ detail: 'Not found.' }, 404));
    dialogOpen = vi.fn(() => ({ afterClosed: () => of(true) }));
    stateReload = vi.fn();

    await TestBed.configureTestingModule({
      providers: [
        provideI18nTesting(),
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: PlanningState, useValue: { reload: stateReload } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlanningCalendar);
    fixture.componentRef.setInput('month', month);
    fixture.componentRef.setInput('data', data);
    fixture.componentRef.setInput('deposits', [
      DEPOSIT,
      depositResponse({ name: 'Archived', maturityDate: '2027-03-22', isArchived: true }),
    ]);
    fixture.componentRef.setInput('bonds', []);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function cells(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[data-testid="calendar-cell"]'));
  }

  function cell(date: string): HTMLElement {
    return cells().find((candidate) => candidate.dataset['date'] === date)!;
  }

  function itemsOf(date: string): HTMLElement[] {
    return Array.from(cell(date).querySelectorAll<HTMLElement>('[data-testid="calendar-item"]'));
  }

  it('placesItemsOnTheirDaysWithStatusAndAmount', async () => {
    await render(monthData([PENDING, SETTLED, SKIPPED], [TOYOTA]));
    await vi.waitFor(async () => {
      await settle();
      expect(itemsOf('2027-03-22')).toHaveLength(1);
    });

    expect(cells()[0].dataset['date']).toBe('2027-03-01');
    expect(new Date(2027, 2, 1).getDay()).toBe(1);

    const pending = itemsOf('2027-03-10');
    expect(pending).toHaveLength(1);
    expect(pending[0].dataset['status']).toBe('pending');
    expect(pending[0].textContent).toContain('Wypłata');
    expect(pending[0].textContent).toContain(formatMoney(9000, 'PLN'));
    expect(pending[0].querySelector('mat-icon')?.textContent).toBe('schedule');

    const settled = itemsOf('2027-03-26');
    expect(settled).toHaveLength(1);
    expect(settled[0].dataset['status']).toBe('settled');
    expect(settled[0].textContent).toContain(formatMoney(810, 'PLN'));

    expect(settled[0].querySelector('mat-icon')?.textContent).toBe('check_circle');
    expect(itemsOf('2027-03-12')[0].dataset['status']).toBe('skipped');
    expect(itemsOf('2027-03-12')[0].querySelector('mat-icon')?.textContent).toBe('block');
    expect(itemsOf('2027-03-15')[0].textContent).toContain('OC Toyota');
    expect(itemsOf('2027-03-15')[0].textContent).toContain(formatMoney(1200, 'PLN'));

    const deposit = itemsOf('2027-03-22');
    expect(deposit[0].textContent).toContain('Lokata Pekao');
    expect(deposit[0].textContent).toContain(formatMoney(DEPOSIT.projection.finalAmount, 'PLN'));
    expect(element().textContent).not.toContain('Archived');

    const dimmed = cells()
      .filter((candidate) => candidate.dataset['dimmed'] === 'true')
      .map((candidate) => candidate.dataset['date']);
    expect(dimmed).toEqual(MARCH_NON_BUSINESS_DAYS);

    cell('2027-03-26').click();
    await settle();
    const details = element().querySelector<HTMLElement>('[data-testid="calendar-details"]')!;
    expect(details.textContent).toContain('800+');
    expect(details.textContent).toContain(formatDate('2027-03-25'));
  });

  it('renders the weekday row and the grid', async () => {
    await render(monthData([], []));
    await vi.waitFor(async () => {
      await settle();
      expect(cells().length).toBeGreaterThan(0);
    });

    expect(element().querySelectorAll('.planning-calendar__weekdays > span')).toHaveLength(7);
    expect(
      element().querySelectorAll('.planning-calendar__grid [data-testid="calendar-cell"]'),
    ).toHaveLength(cells().length);
  });

  it('renders out-of-month days as blank cells', async () => {
    await render(monthData([occurrence('Czynsz', '2026-12-01', 500)], []), '2026-11');
    await vi.waitFor(async () => {
      await settle();
      expect(cells().length).toBeGreaterThan(0);
    });

    const blank = cells().filter((candidate) => !candidate.dataset['date']);
    expect(blank).toHaveLength(12);
    for (const candidate of blank) {
      expect(candidate.classList.contains('planning-calendar__cell--empty')).toBe(true);
      expect(candidate.textContent?.trim()).toBe('');
      expect(candidate.querySelector('[data-testid="calendar-item"]')).toBeNull();
    }

    const scss = readStylesheet(
      'src/app/features/planning/planning-calendar/planning-calendar.scss',
    );
    const emptyRule = ruleBodies(scss, '.planning-calendar__cell--empty');
    expect(emptyRule).toMatch(/border(?:-right|-bottom|-style)?:\s*(?:0|none)\b/);
    expect(emptyRule).not.toMatch(/dashed|solid|background/);
  });

  it('draws all four edges of in-month days next to blank cells', async () => {
    await render(monthData([], []), '2026-11');
    await vi.waitFor(async () => {
      await settle();
      expect(cells().length).toBeGreaterThan(0);
    });

    expect(cell('2026-11-01').classList.contains('planning-calendar__cell--empty')).toBe(false);

    const scss = readStylesheet(
      'src/app/features/planning/planning-calendar/planning-calendar.scss',
    );
    const edges = Object.fromEntries(declarationList(ruleBodies(scss, '.planning-calendar__cell')));
    for (const side of ['top', 'right', 'bottom', 'left']) {
      expect(edges[`border-${side}`]).toBe('1px solid var(--mat-sys-outline-variant)');
    }
    expect(edges['margin']).toBe('-1px 0 0 -1px');
  });

  it('keeps the today highlight border', async () => {
    await render(monthData([], []));
    await vi.waitFor(async () => {
      await settle();
      expect(cells().length).toBeGreaterThan(0);
    });

    expect(cell('2027-03-05').classList.contains('planning-calendar__cell--today')).toBe(true);

    const scss = readStylesheet(
      'src/app/features/planning/planning-calendar/planning-calendar.scss',
    );
    const today = Object.fromEntries(
      declarationList(ruleBodies(scss, '.planning-calendar__cell--today')),
    );
    expect(today['border-color']).toBe('var(--mat-sys-primary)');
    expect(today['border-width']).toBe('2px');
    expect(today['position']).toBe('relative');
    expect(today['z-index']).toBe('1');
  });

  it('dims settled items', async () => {
    await render(monthData([PENDING, SETTLED], []));
    await vi.waitFor(async () => {
      await settle();
      expect(itemsOf('2027-03-26')).toHaveLength(1);
    });

    expect(itemsOf('2027-03-26')[0].dataset['status']).toBe('settled');
    expect(itemsOf('2027-03-10')[0].dataset['status']).toBe('pending');

    const scss = readStylesheet(
      'src/app/features/planning/planning-calendar/planning-calendar.scss',
    );
    expect(ruleBodies(scss, ".planning-calendar__item[data-status='settled']")).toMatch(
      /opacity:\s*0?\.\d+|color:\s*var\(--mat-sys-on-surface-variant\)/,
    );
    expect(ruleBodies(scss, ".planning-calendar__item[data-status='pending']")).not.toMatch(
      /opacity|on-surface-variant/,
    );
  });

  it('gives the day details heading the sub section title classes', async () => {
    await render(monthData([], []));
    await vi.waitFor(async () => {
      await settle();
      expect(cells().length).toBeGreaterThan(0);
    });

    cell('2027-03-05').click();
    await settle();

    const heading = element().querySelector<HTMLElement>('[data-testid="calendar-details"] h3')!;
    expect(heading.classList.contains('sk-section-title')).toBe(true);
    expect(heading.classList.contains('sk-section-title--sub')).toBe(true);
  });

  it('places one-off entries on their dates', async () => {
    const data = monthData([], []) as {
      incomes: Record<string, unknown>;
      expenses: Record<string, unknown>;
    };
    data.incomes['oneOffs'] = [
      {
        id: 'one-off-premia',
        name: 'Premia roczna',
        date: '2027-03-18',
        accountId,
        accountName: 'mBank ROR',
        accountArchived: false,
        amount: 8000,
        currency: 'PLN',
        amountPln: 8000,
        estimated: false,
        inMonth: true,
        status: 'Pending',
        actualAmount: null,
        actualDate: null,
        actualAmountPln: null,
        difference: null,
        depositCreated: false,
        overdue: false,
      },
      {
        id: 'one-off-later',
        name: 'Poza miesiącem',
        date: '2027-04-18',
        accountId,
        accountName: 'mBank ROR',
        accountArchived: false,
        amount: 100,
        currency: 'PLN',
        amountPln: 100,
        estimated: false,
        inMonth: false,
        status: 'Pending',
        actualAmount: null,
        actualDate: null,
        actualAmountPln: null,
        difference: null,
        depositCreated: false,
        overdue: false,
      },
    ];
    data.expenses['oneOffs'] = [
      {
        id: 'one-off-expense-przeglad',
        name: 'Przegląd auta',
        date: '2027-03-24',
        amount: 1200,
        inMonth: true,
      },
    ];
    await render(data);

    const income = itemsOf('2027-03-18');
    expect(income).toHaveLength(1);
    expect(income[0].textContent).toContain('Premia roczna');
    expect(income[0].textContent).toContain(formatMoney(8000, 'PLN'));
    expect(income[0].dataset['status']).toBe('pending');

    const expense = itemsOf('2027-03-24');
    expect(expense).toHaveLength(1);
    expect(expense[0].textContent).toContain('Przegląd auta');
    expect(expense[0].textContent).toContain(formatMoney(1200, 'PLN'));

    expect(element().textContent).not.toContain('Poza miesiącem');
  });

  it('marks unplanned one-off day', async () => {
    const data = monthData([], []);
    data['incomes'] = {
      ...(data['incomes'] as Record<string, unknown>),
      oneOffs: [
        oneOffIncome('Premia', '2027-03-15', false),
        oneOffIncome('Pensja dodatkowa', '2027-03-18', true),
      ],
    };
    await render(data);

    expect(cell('2027-03-15').querySelector('[data-testid="unplanned-mark"]')).not.toBeNull();
    expect(cell('2027-03-18').querySelector('[data-testid="unplanned-mark"]')).toBeNull();
    expect(cell('2027-03-18').textContent).toContain('Pensja dodatkowa');
  });

  it('showsThreeItemsAndMoreInACellAndAllInTheDayDetails', async () => {
    const crowded = monthData(
      [PENDING],
      [
        payment('Czynsz', '2027-03-10', 2000),
        payment('Prąd', '2027-03-10', 200),
        payment('Internet', '2027-03-10', 60),
        payment('Telefon', '2027-03-10', 40),
      ],
    );
    await render(crowded);

    expect(itemsOf('2027-03-10')).toHaveLength(3);
    expect(
      cell('2027-03-10').querySelector('[data-testid="calendar-more"]')?.textContent,
    ).toContain('+2');

    cell('2027-03-10').click();
    await settle();
    const details = element().querySelector<HTMLElement>('[data-testid="calendar-details"]')!;
    expect(details.querySelectorAll('[data-testid="calendar-detail-item"]')).toHaveLength(5);
  });

  it('opensTheSettleDialogForAPendingIncomeAndReloadsAfterSaving', async () => {
    await render(monthData([PENDING], []));
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);

    itemsOf('2027-03-10')[0].click();
    await settle();

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    const [component, config] = dialogOpen.mock.calls[0] as unknown as [
      unknown,
      { data: { occurrence: { recurringIncomeId: string } } },
    ];
    expect(component).toBe(SettleOccurrenceDialog);
    expect(config.data.occurrence.recurringIncomeId).toBe('id-Wypłata');
    expect(stateReload).toHaveBeenCalledTimes(1);
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('expense payment is settleable', async () => {
    const pendingPayment = {
      ...payment('Czynsz', '2027-03-10', 2400),
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
    };
    const settledPayment = {
      ...pendingPayment,
      recurringExpenseId: 'expense-Prąd',
      name: 'Prąd',
      date: '2027-03-12',
      amount: 200,
      status: 'Settled',
      actualAmount: 210,
      actualDate: '2027-03-11',
      actualAmountPln: 210,
      difference: 10,
      withdrawalCreated: true,
    };
    await render(monthData([], [pendingPayment, settledPayment]));
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);

    expect(itemsOf('2027-03-10')[0].dataset['status']).toBe('pending');
    expect(itemsOf('2027-03-12')[0].dataset['status']).toBe('settled');
    expect(itemsOf('2027-03-12')[0].textContent).toContain(formatMoney(210, 'PLN'));

    itemsOf('2027-03-12')[0].click();
    await settle();
    expect(dialogOpen).not.toHaveBeenCalled();

    itemsOf('2027-03-10')[0].click();
    await settle();

    expect(dialogOpen).toHaveBeenCalledTimes(1);
    const [component, config] = dialogOpen.mock.calls[0] as unknown as [
      unknown,
      { data: { payment: { recurringExpenseId: string } } },
    ];
    expect(component).toBe(SettleExpenseDialog);
    expect(config.data.payment.recurringExpenseId).toBe('expense-Czynsz');
    expect(stateReload).toHaveBeenCalledTimes(1);
    expect(saved).toHaveBeenCalledTimes(1);
  });
});
