import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { TodoItemResponse } from '../../../api/portfolio';
import { PlanningState } from '../../../core/planning/planning-state';
import { formatDate, formatMoney } from '../../../shared/format';
import { SectionError } from '../../../shared/section-error/section-error';

const VISIBLE_ITEMS = 5;

interface TodoRow {
  overdue: boolean;
  name: string;
  date: string;
  days: number;
  amount: string;
  month: string;
}

function rowOf(item: TodoItemResponse): TodoRow {
  const overdue = item.kind === 'OverdueIncome';
  const amount = Number(item.amount);
  return {
    overdue,
    name: item.name,
    date: formatDate(item.date),
    days: Number((overdue ? item.daysOverdue : item.daysUntil) ?? 0),
    amount: formatMoney(overdue ? amount : -amount, item.currency),
    month: item.date.slice(0, 7),
  };
}

@Component({
  selector: 'app-todo-card',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
    RouterLink,
    SectionError,
    TranslocoPipe,
  ],
  templateUrl: './todo-card.html',
  styleUrl: './todo-card.scss',
})
export class TodoCard {
  protected readonly planningState = inject(PlanningState);
  private readonly expanded = signal(false);

  private readonly rows = computed(() => this.planningState.todo().map(rowOf));

  protected readonly visibleRows = computed(() =>
    this.expanded() ? this.rows() : this.rows().slice(0, VISIBLE_ITEMS),
  );

  protected readonly canExpand = computed(
    () => !this.expanded() && this.rows().length > VISIBLE_ITEMS,
  );

  protected readonly rowCount = computed(() => this.rows().length);

  protected showAll(): void {
    this.expanded.set(true);
  }
}
