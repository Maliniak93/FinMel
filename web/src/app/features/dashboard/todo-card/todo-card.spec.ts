import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { restoreEnglish } from '../../../../testing/i18n';
import { dateOnlyFromToday, jsonResponse, renderCard } from '../testing/dashboard-fixtures';
import { TodoCard } from './todo-card';

function overdue(name: string): Record<string, unknown> {
  return {
    kind: 'OverdueIncome',
    name,
    date: dateOnlyFromToday(-3),
    amount: 100,
    currency: 'PLN',
    daysOverdue: 3,
    daysUntil: null,
  };
}

describe('TodoCard', () => {
  afterEach(async () => {
    vi.restoreAllMocks();
    await restoreEnglish();
  });

  async function render(items: Record<string, unknown>[]) {
    const { fixture } = await renderCard(TodoCard, {
      todo: jsonResponse({ items, paymentLeadDays: 7 }),
    });
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the empty text without items', async () => {
    const { element } = await render([]);

    expect(element.querySelectorAll('[data-testid="todo-card-item"]')).toHaveLength(0);
    expect(element.querySelector('[data-testid="todo-card-empty"]')).not.toBeNull();
  });

  it('caps the list at five rows until show all', async () => {
    const { fixture, element } = await render(
      ['a', 'b', 'c', 'd', 'e', 'f', 'g'].map((name) => overdue(`Income ${name}`)),
    );

    expect(element.querySelectorAll('[data-testid="todo-card-item"]')).toHaveLength(5);

    element.querySelector<HTMLButtonElement>('[data-testid="todo-card-show-all"]')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelectorAll('[data-testid="todo-card-item"]')).toHaveLength(7);
  });
});

describe('TodoCard stylesheet', () => {
  it('rules the list and every row, with a 24 px title', () => {
    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/dashboard/todo-card/todo-card.scss'),
      'utf8',
    );

    expect(scss).toMatch(/\.todo-card__list\s*\{[^}]*border-top:/);
    expect(scss).toMatch(/\.todo-card__row\s*\{[^}]*border-bottom:/);
  });
});
