import { ComponentFixture, TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../../core/i18n/testing';
import { PlanningYearChart } from './planning-year-chart';

function month(index: number, overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    month: `2026-${String(index + 1).padStart(2, '0')}`,
    hasDefinitions: true,
    incomePlanPln: 16500,
    incomeActualPln: null,
    differencePln: null,
    costPln: 4200,
    freeFundsPlanPln: 6000 + index * 1000,
    freeFundsForecastPln: 5000 + index * 1000,
    hasOverdue: false,
    incomplete: false,
    ...overrides,
  };
}

describe('PlanningYearChart', () => {
  let fixture: ComponentFixture<PlanningYearChart>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function render(months: Record<string, unknown>[]): Promise<void> {
    await TestBed.configureTestingModule({ providers: [provideI18nTesting()] }).compileComponents();
    fixture = TestBed.createComponent(PlanningYearChart);
    fixture.componentRef.setInput('months', months);
    fixture.componentRef.setInput('year', 2026);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function columns(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[data-testid="year-chart-column"]'));
  }

  function heightOf(node: Element | null): number {
    return parseFloat((node as HTMLElement).style.height);
  }

  it('renders twelve columns with plan and forecast', async () => {
    await render(Array.from({ length: 12 }, (_, index) => month(index)));

    expect(columns()).toHaveLength(12);
    for (const column of columns()) {
      expect(column.querySelector('[data-testid="year-chart-plan"]')).not.toBeNull();
      expect(column.querySelector('[data-testid="year-chart-forecast"]')).not.toBeNull();
    }
    expect(columns()[0].textContent).toContain('Jan');
    expect(columns()[11].textContent).toContain('Dec');

    const plans = columns().map((column) =>
      heightOf(column.querySelector('[data-testid="year-chart-plan"]')),
    );
    expect(plans[11]).toBeCloseTo(100, 0);
    expect(plans[0]).toBeCloseTo((6000 / 17000) * 100, 0);

    const chart = element().querySelector('[role="img"]')!;
    expect(chart.getAttribute('aria-label')).toContain('Free funds, plan and forecast in 2026');
    expect(chart.getAttribute('aria-label')).toContain('Jan');
  });

  it('marks a deficit month with outline and word', async () => {
    const months = Array.from({ length: 12 }, (_, index) =>
      index === 5
        ? month(index, { freeFundsPlanPln: -3000, freeFundsForecastPln: -3000 })
        : month(index),
    );
    await render(months);

    const bar = columns()[5].querySelector('[data-testid="year-chart-plan"]')!;
    expect(bar.getAttribute('data-deficit')).toBe('true');
    expect(heightOf(bar)).toBeGreaterThan(0);
    expect(columns()[5].querySelector('[data-testid="year-chart-deficit-mark"]')).not.toBeNull();
    expect(columns()[4].querySelector('[data-testid="year-chart-deficit-mark"]')).toBeNull();
    expect(element().querySelector('[role="img"]')!.getAttribute('aria-label')).toContain(
      'deficit',
    );
  });

  it('marks an incomplete month', async () => {
    const months = Array.from({ length: 12 }, (_, index) =>
      index === 3 ? month(index, { incomplete: true }) : month(index),
    );
    await render(months);

    expect(
      columns()[3].querySelector('[data-testid="year-chart-incomplete"]')?.textContent,
    ).toContain('incomplete');
    expect(columns()[2].querySelector('[data-testid="year-chart-incomplete"]')).toBeNull();
  });

  it('leaves the slot of a month without definitions empty and marks the current month', async () => {
    const months = Array.from({ length: 12 }, (_, index) =>
      index < 2
        ? month(index, {
            hasDefinitions: false,
            freeFundsPlanPln: null,
            freeFundsForecastPln: null,
          })
        : month(index),
    );
    await render(months);

    expect(columns()[0].querySelector('[data-testid="year-chart-plan"]')).toBeNull();
    expect(columns()[0].querySelector('[data-testid="year-chart-forecast"]')).toBeNull();
    expect(columns()[0].querySelector('[data-testid="year-chart-empty"]')).not.toBeNull();
    expect(
      columns().filter((column) => column.getAttribute('data-current') === 'true'),
    ).toHaveLength(1);
    expect(columns()[9].getAttribute('data-current')).toBe('true');
  });
});
