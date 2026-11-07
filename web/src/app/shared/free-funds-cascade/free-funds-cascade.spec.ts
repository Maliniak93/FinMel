import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../format';
import { FreeFundsCascade } from './free-funds-cascade';

const labels = {
  incomes: 'Incomes label',
  expenses: 'Expenses label',
  contributions: 'Contributions label',
  free: 'Free label',
  deficit: 'deficit word',
  plan: 'Plan label',
  actual: 'Actual label',
};

const groups = [
  { id: 'g-1', color: 'blue' },
  { id: 'g-2', color: 'green' },
];

@Component({
  imports: [FreeFundsCascade],
  template: `<app-free-funds-cascade
    [labels]="labels"
    [plan]="plan"
    [actual]="actual"
    [contributions]="contributions"
    [groups]="groups"
  />`,
})
class Host {
  labels = labels;
  plan = { incomes: 13600, expenses: 5679.99, contributions: 3000, free: 4920.01 };
  actual: { incomes: number; expenses: number; contributions: number; free: number } | null = null;
  contributions = [
    { groupId: 'g-1', name: 'Emerytura', amountPln: 2000 },
    { groupId: 'g-2', name: 'Inwestycje', amountPln: 1000 },
  ];
  groups = groups;
}

describe('FreeFundsCascade', () => {
  async function render(configure: (host: Host) => void = () => undefined) {
    await TestBed.configureTestingModule({ providers: [provideI18nTesting()] }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    configure(fixture.componentInstance);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function byId(element: HTMLElement, testId: string): HTMLElement | null {
    return element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
  }

  it('renders the four rows on one axis', async () => {
    const element = await render();

    const incomes = byId(element, 'cascade-plan-incomes')!;
    const expenses = byId(element, 'cascade-plan-expenses')!;
    const contributions = byId(element, 'cascade-plan-contributions')!;
    const free = byId(element, 'cascade-plan-free')!;

    expect(incomes.textContent).toContain('Incomes label');
    expect(incomes.textContent).toContain(formatMoney(13600, 'PLN'));
    expect(expenses.textContent).toContain('Expenses label');
    expect(expenses.textContent).toContain(formatMoney(5679.99, 'PLN'));
    expect(contributions.textContent).toContain('Contributions label');
    expect(contributions.textContent).toContain(formatMoney(3000, 'PLN'));
    expect(free.textContent).toContain('Free label');
    expect(free.textContent).toContain(formatMoney(4920.01, 'PLN'));

    expect(contributions.querySelectorAll('app-split-bar .split-bar__segment')).toHaveLength(2);
    expect(byId(element, 'cascade-actual-incomes')).toBeNull();

    const width = (testId: string) => parseFloat(byId(element, testId)!.style.width);
    expect(width('cascade-bar-plan-incomes')).toBeCloseTo(100, 1);
    expect(width('cascade-bar-plan-expenses')).toBeCloseTo((5679.99 / 13600) * 100, 1);
    expect(width('cascade-bar-plan-contributions')).toBeCloseTo((3000 / 13600) * 100, 1);
    expect(width('cascade-bar-plan-free')).toBeCloseTo((4920.01 / 13600) * 100, 1);
  });

  it('shows the Actual column when actual values are given', async () => {
    const element = await render((host) => {
      host.actual = { incomes: 10000, expenses: 5679.99, contributions: 3000, free: 1320.01 };
    });

    expect(byId(element, 'cascade-actual-incomes')!.textContent).toContain(
      formatMoney(10000, 'PLN'),
    );
    expect(byId(element, 'cascade-actual-free')!.textContent).toContain(
      formatMoney(1320.01, 'PLN'),
    );
    expect(element.textContent).toContain('Actual label');
  });

  it('marks a deficit with icon and word', async () => {
    const element = await render((host) => {
      host.plan = { incomes: 5000, expenses: 5700, contributions: 0, free: -700 };
      host.contributions = [];
    });

    const free = byId(element, 'cascade-plan-free')!;
    expect(free.textContent).toContain(formatMoney(-700, 'PLN'));
    const marker = byId(element, 'cascade-free-deficit')!;
    expect(marker.textContent).toContain('deficit word');
    expect(free.querySelector('mat-icon')).not.toBeNull();

    const bar = byId(element, 'cascade-bar-plan-free');
    const widths = bar === null ? 0 : parseFloat(bar.style.width || '0');
    expect(widths).toBeGreaterThanOrEqual(0);
    expect(bar?.style.width ?? '').not.toContain('-');
  });

  it('colours an unknown group neutrally', async () => {
    const element = await render((host) => {
      host.contributions = [
        { groupId: 'g-1', name: 'Emerytura', amountPln: 2000 },
        { groupId: 'missing', name: 'Nieznana', amountPln: 1000 },
      ];
    });

    const bar = byId(element, 'cascade-plan-contributions')!.querySelector<HTMLElement>(
      'app-split-bar [role="img"]',
    )!;
    expect(bar.getAttribute('aria-label')).toContain('Nieznana');
    const segments = [...bar.querySelectorAll<HTMLElement>('.split-bar__segment')];
    expect(segments[0].style.backgroundColor).toContain('--sk-g-blue');
    expect(segments[1].style.backgroundColor).toContain('--sk-g-none');
  });
});
