import { Injectable, computed, resource, signal } from '@angular/core';

import {
  getApiPortfolioPlanningOverdue,
  getApiPortfolioPlanningTodo,
  type OverdueOccurrenceResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../auth/problem-details';

const DEFAULT_PAYMENT_LEAD_DAYS = 7;

export type OverdueRecurringOccurrence = OverdueOccurrenceResponse & {
  recurringIncomeId: string;
  nominalMonth: string;
};

export type OverdueOneOffIncome = OverdueOccurrenceResponse & {
  oneOffIncomeId: string;
};

export type OverdueItem = OverdueRecurringOccurrence | OverdueOneOffIncome;

export function isOverdueOneOff(item: OverdueItem): item is OverdueOneOffIncome {
  return !!item.oneOffIncomeId;
}

function isOverdueItem(item: OverdueOccurrenceResponse): item is OverdueItem {
  return (
    (item.recurringIncomeId !== null && item.nominalMonth !== null) ||
    (item.oneOffIncomeId !== null && item.oneOffIncomeId !== undefined)
  );
}

@Injectable({ providedIn: 'root' })
export class PlanningState {
  private readonly session = signal(0);

  private readonly overdueResource = resource({
    params: () => this.session(),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPlanningOverdue({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load.');
      }
      return result.data?.items ?? [];
    },
  });

  private readonly todoResource = resource({
    params: () => this.session(),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPlanningTodo({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load.');
      }
      return result.data;
    },
  });

  readonly overdue = computed(() =>
    this.overdueResource.hasValue() ? this.overdueResource.value().filter(isOverdueItem) : [],
  );

  readonly todo = computed(() =>
    this.todoResource.hasValue() ? (this.todoResource.value()?.items ?? []) : [],
  );

  readonly todoCount = computed(() => this.todo().length);

  readonly paymentLeadDays = computed(() =>
    this.todoResource.hasValue()
      ? Number(this.todoResource.value()?.paymentLeadDays ?? DEFAULT_PAYMENT_LEAD_DAYS)
      : DEFAULT_PAYMENT_LEAD_DAYS,
  );

  startSession(): void {
    this.session.update((value) => value + 1);
  }

  reload(): void {
    this.overdueResource.reload();
    this.todoResource.reload();
  }
}
