import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { formatMoney } from '../../../shared/format';
import { jsonResponse, planningMonthResponse, renderCard } from '../testing/dashboard-fixtures';
import { MonthCard } from './month-card';

function occurrence(name: string, status: string): Record<string, unknown> {
  return { recurringIncomeId: `id-${name}`, name, status, amount: 1000, currency: 'PLN' };
}

function incomeMonth(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return planningMonthResponse({
    incomes: {
      occurrences: [
        occurrence('Wypłata Dominik', 'Settled'),
        occurrence('Wypłata żona', 'Skipped'),
        occurrence('800+', 'Pending'),
      ],
      balance: { planPln: 0, actualPln: 0, pendingPln: 0, differencePln: 0 },
      fxUnavailable: false,
      definitions: [{ id: 'income-1', name: 'Wypłata Dominik' }],
    },
    freeFunds: { planPln: 12300, forecastPln: 12243.27, incomplete: false },
    ...extra,
  });
}

const todoItems = [
  { kind: 'OverdueIncome', name: '800+', date: '2026-11-10', amount: 800, currency: 'PLN' },
  { kind: 'UpcomingPayment', name: 'Przegląd', date: '2026-11-02', amount: 400, currency: 'PLN' },
  { kind: 'UpcomingPayment', name: 'OC Toyota', date: '2026-11-05', amount: 1200, currency: 'PLN' },
  {
    kind: 'UpcomingPayment',
    name: 'Ubezpieczenie',
    date: '2026-12-01',
    amount: 900,
    currency: 'PLN',
  },
  { kind: 'UpcomingPayment', name: 'Podatek', date: '2026-12-02', amount: 300, currency: 'PLN' },
  { kind: 'UpcomingPayment', name: 'Szkoła', date: '2026-12-03', amount: 500, currency: 'PLN' },
  { kind: 'UpcomingPayment', name: 'Czynsz', date: '2026-12-04', amount: 700, currency: 'PLN' },
];

describe('MonthCard', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
  });

  afterEach(async () => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  async function render(
    month: Record<string, unknown>,
    items: Record<string, unknown>[] = [],
  ): Promise<HTMLElement> {
    const { fixture } = await renderCard(MonthCard, {
      planningMonth: jsonResponse(month),
      todo: jsonResponse({ items, paymentLeadDays: 7 }),
    });
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function all(element: HTMLElement, testId: string): HTMLElement[] {
    return Array.from(element.querySelectorAll<HTMLElement>(`[data-testid="${testId}"]`));
  }

  it('shows the free funds, the settled incomes and five todo items, then reveals the rest', async () => {
    const element = await render(incomeMonth(), todoItems);

    expect(all(element, 'month-card-plan')[0].textContent).toContain(formatMoney(12300, 'PLN'));
    expect(all(element, 'month-card-forecast')[0].textContent).toContain(
      formatMoney(12243.27, 'PLN'),
    );
    expect(all(element, 'month-card-settled')[0].textContent).toMatch(/2\D+3/);
    expect(all(element, 'month-card-todo-item')).toHaveLength(5);
    expect(all(element, 'month-card-empty')).toHaveLength(0);

    all(element, 'month-card-show-all')[0].click();
    await vi.waitFor(() => expect(all(element, 'month-card-todo-item')).toHaveLength(7));

    expect(all(element, 'month-card-todo-item').map((item) => item.getAttribute('href'))).toEqual([
      '/planning?month=2026-11#do-rozliczenia',
      '/planning?month=2026-11#wydatki',
      '/planning?month=2026-11#wydatki',
      '/planning?month=2026-12#wydatki',
      '/planning?month=2026-12#wydatki',
      '/planning?month=2026-12#wydatki',
      '/planning?month=2026-12#wydatki',
    ]);
  });

  it('styles a negative forecast as a deficit', async () => {
    const element = await render(
      incomeMonth({ freeFunds: { planPln: 12300, forecastPln: -700, incomplete: false } }),
    );

    expect(all(element, 'month-card-deficit')).toHaveLength(1);
    expect(all(element, 'month-card-deficit')[0].textContent).toContain(formatMoney(-700, 'PLN'));
  });

  it('shows "Wszystko załatwione" when the list is empty', async () => {
    const element = await render(incomeMonth(), []);

    expect(all(element, 'month-card-empty')).toHaveLength(1);
    expect(all(element, 'month-card-todo-item')).toHaveLength(0);
    expect(all(element, 'month-card-show-all')).toHaveLength(0);
  });

  it('shows only the "Zaplanuj miesiąc" link when there are no definitions', async () => {
    const element = await render(planningMonthResponse(), []);

    const setup = all(element, 'month-card-setup');
    expect(setup).toHaveLength(1);
    expect(setup[0].getAttribute('href')).toBe('/planning');
    expect(all(element, 'month-card-plan')).toHaveLength(0);
    expect(all(element, 'month-card-forecast')).toHaveLength(0);
    expect(all(element, 'month-card-todo-item')).toHaveLength(0);
    expect(all(element, 'month-card-empty')).toHaveLength(0);
  });

  it('renders in Polish', async () => {
    const { fixture } = await renderCard(MonthCard, {
      planningMonth: jsonResponse(incomeMonth()),
      todo: jsonResponse({ items: [], paymentLeadDays: 7 }),
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    await switchLanguage(fixture, 'pl');

    expect(all(element, 'month-card-empty')[0].textContent).toContain('Wszystko załatwione');
  });
});
