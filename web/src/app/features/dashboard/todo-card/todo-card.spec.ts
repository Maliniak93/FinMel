import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { SettleExpenseDialog } from '../../planning/settle-expense-dialog/settle-expense-dialog';
import {
  dateOnlyFromToday,
  jsonResponse,
  renderCard,
  upcomingPaymentItem,
} from '../testing/dashboard-fixtures';
import { TodoCard } from './todo-card';

function overdueOneOffExpense(name: string, daysAgo: number): Record<string, unknown> {
  return {
    kind: 'OverdueExpense',
    name,
    date: dateOnlyFromToday(-daysAgo),
    amount: 120,
    currency: 'PLN',
    daysOverdue: daysAgo,
    daysUntil: null,
    oneOffExpense: {
      id: `one-off-${name}`,
      name,
      date: dateOnlyFromToday(-daysAgo),
      amount: 120,
      inMonth: true,
      status: 'Pending',
    },
  };
}

function overdueRecurringPayment(name: string, daysAgo: number): Record<string, unknown> {
  return {
    kind: 'OverdueExpense',
    name,
    date: dateOnlyFromToday(-daysAgo),
    amount: 2400,
    currency: 'PLN',
    daysOverdue: daysAgo,
    daysUntil: null,
    expensePayment: {
      recurringExpenseId: `expense-${name}`,
      nominalMonth: dateOnlyFromToday(-daysAgo).slice(0, 7),
      date: dateOnlyFromToday(-daysAgo),
      name,
      amount: 2400,
      status: 'Pending',
    },
  };
}

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

  it('caps the merged list at five and expands it', async () => {
    const { fixture, element } = await render([
      overdue('Income a'),
      overdueOneOffExpense('Expense a', 5),
      overdueOneOffExpense('Expense b', 4),
      overdueRecurringPayment('Rent a', 3),
      overdueRecurringPayment('Rent b', 2),
      overdueOneOffExpense('Expense c', 1),
      overdue('Income b'),
    ]);

    expect(element.querySelectorAll('[data-testid="todo-card-item"]')).toHaveLength(5);

    element.querySelector<HTMLButtonElement>('[data-testid="todo-card-show-all"]')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelectorAll('[data-testid="todo-card-item"]')).toHaveLength(7);
  });

  it('renders overdue expenses with a Rozlicz button', async () => {
    const { fixture, element } = await render([
      { ...overdue('Premia'), date: dateOnlyFromToday(-5), daysOverdue: 5 },
      overdueRecurringPayment('Czynsz', 4),
      overdueOneOffExpense('Abonament', 2),
    ]);
    await switchLanguage(fixture, 'pl');

    const rows = [...element.querySelectorAll<HTMLElement>('[data-testid="todo-card-item"]')];

    expect(rows.map((row) => row.textContent?.includes('Premia'))).toEqual([true, false, false]);
    expect([...rows[0].querySelectorAll('a')].some((link) => textOf(link) === 'Rozlicz')).toBe(
      true,
    );
    expect(rows[0].querySelector('button')).toBeNull();
    for (const [row, days] of [
      [rows[1], 4],
      [rows[2], 2],
    ] as const) {
      const text = (row.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain(`zaległe: ${days} dni · wydatek`);
      expect(text).toMatch(/[−-]\s?(120,00|2\s?400,00)\s?zł/);
      expect(row.querySelector('a')).toBeNull();
      expect([...row.querySelectorAll('button')].some((b) => textOf(b) === 'Rozlicz')).toBe(true);
    }
  });

  it('opens the settle dialog with the row payload', async () => {
    const { fixture, element } = await render([
      overdueRecurringPayment('Czynsz', 4),
      overdueOneOffExpense('Abonament', 2),
    ]);
    await switchLanguage(fixture, 'pl');
    const open = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of(false) } as never);
    const rows = [...element.querySelectorAll<HTMLElement>('[data-testid="todo-card-item"]')];
    const settle = (row: HTMLElement) =>
      [...row.querySelectorAll('button')].find((b) => textOf(b) === 'Rozlicz')!;

    settle(rows[0]).click();
    settle(rows[1]).click();

    expect(open).toHaveBeenNthCalledWith(
      1,
      SettleExpenseDialog,
      expect.objectContaining({
        data: { payment: expect.objectContaining({ recurringExpenseId: 'expense-Czynsz' }) },
      }),
    );
    expect(open).toHaveBeenNthCalledWith(
      2,
      SettleExpenseDialog,
      expect.objectContaining({
        data: { oneOffExpense: expect.objectContaining({ id: 'one-off-Abonament' }) },
      }),
    );
  });

  it('tints the block only while a row is overdue', async () => {
    const block = (element: HTMLElement) => element.querySelector('.todo-card')?.classList;

    const withOverdue = await render([overdue('Premia')]);
    expect(block(withOverdue.element)).toContain('todo-card--overdue');

    TestBed.resetTestingModule();
    const upcoming = await render([upcomingPaymentItem('Ubezpieczenie OC', 5)]);
    expect(block(upcoming.element)).not.toContain('todo-card--overdue');

    TestBed.resetTestingModule();
    const empty = await render([]);
    expect(block(empty.element)).not.toContain('todo-card--overdue');
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
