import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { PlanningState } from '../../../core/planning/planning-state';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatDate, formatMoney } from '../../../shared/format';
import { depositResponse } from '../../deposits/testing/deposit-fixtures';
import { jsonResponse, requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';
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
    },
    expenses: { payments, costPln: 0, definitions: [] },
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
  const originalMatchMedia = window.matchMedia;

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
    window.matchMedia = originalMatchMedia;
  });

  function narrowViewport(): void {
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

  async function render(data: Record<string, unknown>): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/api/portfolio/deposits')) {
        return jsonResponse([
          DEPOSIT,
          depositResponse({ name: 'Archived', maturityDate: '2027-03-22', isArchived: true }),
        ]);
      }
      if (url.includes('/api/portfolio/bonds')) {
        return jsonResponse([]);
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });
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
    fixture.componentRef.setInput('month', '2027-03');
    fixture.componentRef.setInput('data', data);
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

    const settled = itemsOf('2027-03-26');
    expect(settled).toHaveLength(1);
    expect(settled[0].dataset['status']).toBe('settled');
    expect(settled[0].textContent).toContain(formatMoney(810, 'PLN'));

    expect(itemsOf('2027-03-12')[0].dataset['status']).toBe('skipped');
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

  it('showsOnlyTheItemCountInACellOnANarrowViewport', async () => {
    narrowViewport();
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

    expect(itemsOf('2027-03-10')).toHaveLength(0);
    expect(
      cell('2027-03-10').querySelector('[data-testid="calendar-count"]')?.textContent?.trim(),
    ).toBe('5');

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
});
