import { TestBed } from '@angular/core/testing';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { TodoItemKind } from '../../api/portfolio';
import {
  jsonResponse,
  requestUrl,
} from '../../features/assets/asset-form/testing/asset-form-fixtures';
import { PlanningState } from './planning-state';

let items: Record<string, unknown>[] = [];

function todoItem(kind: TodoItemKind): Record<string, unknown> {
  return {
    kind,
    name: kind,
    date: '2026-11-10',
    amount: 100,
    currency: 'PLN',
    daysOverdue: null,
    daysUntil: null,
    expensePayment: null,
    oneOffExpense: null,
  };
}

async function loadedWithCount(state: PlanningState, count: number): Promise<void> {
  await vi.waitFor(() => {
    TestBed.tick();
    expect(state.todoCount()).toBe(count);
  });
}

beforeAll(() => {
  portfolioClient.setConfig({ baseUrl: 'https://example.test' });
});

beforeEach(() => {
  items = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) =>
    requestUrl(input).includes('/planning/todo')
      ? jsonResponse({ items, paymentLeadDays: 7 })
      : jsonResponse({ items: [] }),
  );
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('PlanningState', () => {
  it('hasOverdue follows the item kinds', async () => {
    items = [todoItem('UpcomingPayment')];
    const state = TestBed.inject(PlanningState);
    await loadedWithCount(state, 1);
    expect(state.hasOverdue()).toBe(false);

    items = [todoItem('UpcomingPayment'), todoItem('OverdueExpense')];
    state.startSession();
    await loadedWithCount(state, 2);
    expect(state.hasOverdue()).toBe(true);

    items = [];
    state.startSession();
    await loadedWithCount(state, 0);
    expect(state.hasOverdue()).toBe(false);

    items = [todoItem('OverdueIncome')];
    state.startSession();
    await loadedWithCount(state, 1);
    expect(state.hasOverdue()).toBe(true);
  });
});
