import { Component, computed, inject, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { TodoItemResponse } from '../../../api/portfolio';
import { PlanningState } from '../../../core/planning/planning-state';
import { formatDate, formatMoney } from '../../../shared/format';
import { SectionError } from '../../../shared/section-error/section-error';
import {
  SettleExpenseDialog,
  type SettleExpenseDialogData,
} from '../../planning/settle-expense-dialog/settle-expense-dialog';

const VISIBLE_ITEMS = 5;

type TodoRowKind = 'income' | 'expense' | 'payment';

interface TodoRow {
  kind: TodoRowKind;
  name: string;
  date: string;
  days: number;
  amount: string;
  month: string;
  dialogData: SettleExpenseDialogData | null;
}

function dialogDataOf(item: TodoItemResponse): SettleExpenseDialogData | null {
  if (item.expensePayment) {
    return { payment: item.expensePayment };
  }
  return item.oneOffExpense ? { oneOffExpense: item.oneOffExpense } : null;
}

function rowOf(item: TodoItemResponse): TodoRow {
  const kind: TodoRowKind =
    item.kind === 'OverdueIncome'
      ? 'income'
      : item.kind === 'OverdueExpense'
        ? 'expense'
        : 'payment';
  const amount = Number(item.amount);
  return {
    kind,
    name: item.name,
    date: formatDate(item.date),
    days: Number((kind === 'payment' ? item.daysUntil : item.daysOverdue) ?? 0),
    amount: formatMoney(kind === 'income' ? amount : -amount, item.currency),
    month: item.date.slice(0, 7),
    dialogData: dialogDataOf(item),
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
  private readonly dialog = inject(MatDialog);
  private readonly expanded = signal(false);

  readonly settled = output<void>();

  private readonly rows = computed(() => this.planningState.todo().map(rowOf));

  protected readonly visibleRows = computed(() =>
    this.expanded() ? this.rows() : this.rows().slice(0, VISIBLE_ITEMS),
  );

  protected readonly canExpand = computed(
    () => !this.expanded() && this.rows().length > VISIBLE_ITEMS,
  );

  protected readonly hasOverdue = computed(() => this.rows().some((row) => row.kind !== 'payment'));

  protected readonly rowCount = computed(() => this.rows().length);

  protected showAll(): void {
    this.expanded.set(true);
  }

  protected openSettleDialog(row: TodoRow): void {
    if (!row.dialogData) {
      return;
    }
    const ref = this.dialog.open(SettleExpenseDialog, { width: '420px', data: row.dialogData });
    ref.afterClosed().subscribe((saved) => {
      if (saved === true) {
        this.planningState.reloadTodo();
        this.settled.emit();
      }
    });
  }
}
