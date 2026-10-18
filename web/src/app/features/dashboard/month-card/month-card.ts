import { Component, computed, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPlanningMonthsByMonth,
  type TodoItemResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { PlanningState } from '../../../core/planning/planning-state';
import { formatDate, formatMoney } from '../../../shared/format';
import { currentWarsawMonth } from '../../planning/recurrence';

const VISIBLE_ITEMS = 5;

interface MonthCardItem {
  key: string;
  params: Record<string, string | number>;
  month: string;
  fragment: string;
}

function monthOf(date: string | null): string {
  return (date ?? '').slice(0, 7);
}

function itemOf(item: TodoItemResponse): MonthCardItem {
  const currency = item.currency ?? 'PLN';
  const amount = formatMoney(Number(item.amount ?? 0), currency);

  switch (item.kind) {
    case 'OverdueIncome':
      return {
        key: 'dashboard.monthCard.todo.overdueIncome',
        params: {
          name: item.name ?? '',
          date: formatDate(item.date ?? ''),
          days: Number(item.daysOverdue ?? 0),
          amount,
        },
        month: monthOf(item.date),
        fragment: 'do-rozliczenia',
      };
    case 'TransferPlanToAccept':
      return {
        key: item.isTopUp ? 'dashboard.monthCard.todo.topUp' : 'dashboard.monthCard.todo.toAccept',
        params: { amount },
        month: item.month ?? '',
        fragment: 'plan-przelewow',
      };
    case 'TransferPlanUnchecked':
      return {
        key: 'dashboard.monthCard.todo.unchecked',
        params: {
          unchecked: Number(item.uncheckedRows ?? 0),
          total: Number(item.totalRows ?? 0),
        },
        month: item.month ?? '',
        fragment: 'plan-przelewow',
      };
    case 'UpcomingPayment':
      return {
        key: 'dashboard.monthCard.todo.payment',
        params: {
          name: item.name ?? '',
          date: formatDate(item.date ?? ''),
          days: Number(item.daysUntil ?? 0),
          amount,
        },
        month: monthOf(item.date),
        fragment: 'wydatki',
      };
  }
}

@Component({
  selector: 'app-month-card',
  imports: [MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink, TranslocoPipe],
  templateUrl: './month-card.html',
  styleUrl: './month-card.scss',
})
export class MonthCard {
  private readonly planningState = inject(PlanningState);
  private readonly expanded = signal(false);

  protected readonly monthResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPlanningMonthsByMonth({
        path: { month: currentWarsawMonth() },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.monthCard.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly month = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value() : null,
  );

  protected readonly hasDefinitions = computed(() => {
    const month = this.month();
    return (
      !!month && (month.incomes.definitions.length > 0 || month.expenses.definitions.length > 0)
    );
  });

  protected readonly planPln = computed(() => Number(this.month()?.freeFunds.planPln ?? 0));

  protected readonly forecastPln = computed(() => Number(this.month()?.freeFunds.forecastPln ?? 0));

  protected readonly settled = computed(() => {
    const occurrences = this.month()?.incomes.occurrences ?? [];
    return {
      done: occurrences.filter((o) => o.status === 'Settled' || o.status === 'Skipped').length,
      total: occurrences.length,
    };
  });

  protected readonly planStatus = computed(() => this.month()?.transferPlan?.status ?? 'None');

  private readonly items = computed(() => this.planningState.todo().map(itemOf));

  protected readonly itemCount = computed(() => this.items().length);

  protected readonly visibleItems = computed(() =>
    this.expanded() ? this.items() : this.items().slice(0, VISIBLE_ITEMS),
  );

  protected readonly canExpand = computed(
    () => !this.expanded() && this.items().length > VISIBLE_ITEMS,
  );

  protected readonly formatMoney = formatMoney;

  protected showAll(): void {
    this.expanded.set(true);
  }
}
