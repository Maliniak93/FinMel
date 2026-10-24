import { restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { formatMoney } from '../../../shared/format';
import {
  CUSHION_GROUP_ID,
  INVESTMENTS_GROUP_ID,
  RETIREMENT_GROUP_ID,
  groups,
  jsonResponse,
  planningMonthResponse,
  renderCard,
} from '../testing/dashboard-fixtures';
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

function plain(text: string): string {
  return text.replace(/\s+/g, ' ');
}

function splitOf(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    freeFundsPln: 5000,
    deficitPln: null,
    cushion: {
      groupId: CUSHION_GROUP_ID,
      name: 'Poduszka',
      levelPln: 28000,
      targetPln: 30000,
      missingPln: 2000,
      allocationPln: 2000,
    },
    groups: [
      {
        groupId: RETIREMENT_GROUP_ID,
        name: 'Emerytura',
        sharePercent: 60,
        contributionPln: 1500,
        allocationPln: 1800,
        breakdown: null,
      },
      {
        groupId: INVESTMENTS_GROUP_ID,
        name: 'Inwestycje',
        sharePercent: 40,
        contributionPln: 1000,
        allocationPln: 1200,
        breakdown: null,
      },
    ],
    unassignedPln: 0,
    valuationMissing: false,
    ...overrides,
  };
}

function splitMonth(split: Record<string, unknown>): Record<string, unknown> {
  return incomeMonth({ split });
}

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

  async function render(month: Record<string, unknown>): Promise<HTMLElement> {
    const { fixture } = await renderCard(MonthCard, {
      planningMonth: jsonResponse(month),
      todo: jsonResponse({ items: [], paymentLeadDays: 7 }),
      groups: jsonResponse(groups),
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

  it('renders the cascade', async () => {
    const element = await render(
      planningMonthResponse({
        incomes: {
          occurrences: [occurrence('Wypłata', 'Settled'), occurrence('Premia', 'Pending')],
          balance: { planPln: 13600, actualPln: 0, pendingPln: 0, differencePln: 0 },
          fxUnavailable: false,
          definitions: [{ id: 'income-1', name: 'Wypłata' }],
          oneOffs: [],
        },
        expenses: {
          payments: [],
          costPln: 8179.99,
          groupContributions: [
            { groupId: RETIREMENT_GROUP_ID, name: 'Emerytura', amountPln: 1500 },
            { groupId: INVESTMENTS_GROUP_ID, name: 'Inwestycje', amountPln: 1000 },
          ],
          definitions: [],
          oneOffs: [],
        },
        freeFunds: { planPln: 5420.01, forecastPln: 5300, incomplete: false },
      }),
    );
    const text = (id: string) => plain(all(element, id)[0].textContent ?? '');

    expect(text('month-card-incomes')).toContain(plain(formatMoney(13600, 'PLN')));
    expect(text('month-card-expenses')).toContain(plain(formatMoney(5679.99, 'PLN')));
    expect(text('month-card-contributions')).toContain(plain(formatMoney(2500, 'PLN')));
    expect(text('month-card-free')).toContain(plain(formatMoney(5420.01, 'PLN')));
    const colours = Array.from(
      all(element, 'month-card-contributions')[0].querySelectorAll('.split-bar__segment'),
    ).map((segment) => segment.getAttribute('style') ?? '');
    expect(colours).toHaveLength(2);
    expect(colours[0]).toContain('var(--sk-g-blue)');
    expect(colours[1]).toContain('var(--sk-g-green)');
    expect(all(element, 'month-card-forecast')[0].textContent).toContain(formatMoney(5300, 'PLN'));
    expect(all(element, 'month-card-settled')[0].textContent).toMatch(/1\D+2/);
  });

  it('shows the free-funds split', async () => {
    const element = await render(splitMonth(splitOf()));
    const rows = all(element, 'month-card-split-row').map((row) => plain(row.textContent ?? ''));

    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain('Poduszka');
    expect(rows[0]).toContain(plain(formatMoney(2000, 'PLN')));
    expect(rows[1]).toContain('Emerytura');
    expect(rows[1]).toMatch(/60\s?%/);
    expect(rows[1]).toContain(plain(formatMoney(1800, 'PLN')));
    expect(rows[2]).toContain('Inwestycje');
    expect(rows[2]).toMatch(/40\s?%/);
    expect(rows[2]).toContain(plain(formatMoney(1200, 'PLN')));
    expect(all(element, 'month-card-free-deficit')).toHaveLength(0);
  });

  it('marks a deficit', async () => {
    const element = await render(splitMonth(splitOf({ deficitPln: 300, freeFundsPln: -300 })));

    expect(all(element, 'month-card-free-deficit')).toHaveLength(1);
    expect(all(element, 'month-card-split-row')).toHaveLength(0);
  });

  it('hides the split without valuation', async () => {
    const element = await render(splitMonth(splitOf({ valuationMissing: true })));

    expect(all(element, 'month-card-free')).toHaveLength(1);
    expect(all(element, 'month-card-split-row')).toHaveLength(0);
    expect(all(element, 'month-card-free-deficit')).toHaveLength(0);
  });

  it('styles a negative forecast as a deficit', async () => {
    const element = await render(
      incomeMonth({ freeFunds: { planPln: 12300, forecastPln: -700, incomplete: false } }),
    );

    expect(all(element, 'month-card-deficit')).toHaveLength(1);
    expect(all(element, 'month-card-deficit')[0].textContent).toContain(formatMoney(-700, 'PLN'));
  });

  it('shows the setup link without definitions', async () => {
    const element = await render(planningMonthResponse());

    const setup = all(element, 'month-card-setup');
    expect(setup).toHaveLength(1);
    expect(setup[0].getAttribute('href')).toBe('/planning');
    expect(all(element, 'month-card-forecast')).toHaveLength(0);
    expect(all(element, 'month-card-incomes')).toHaveLength(0);
    expect(all(element, 'month-card-free')).toHaveLength(0);
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

    expect(all(element, 'month-card-settled')[0].textContent).toContain('Wpływy załatwione');
  });
});
