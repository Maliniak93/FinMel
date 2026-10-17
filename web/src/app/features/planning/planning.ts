import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioAllocationBucketsByBucketIdBalancesByMonth,
  deleteApiPortfolioAllocationPlanVersionsByMonth,
  deleteApiPortfolioRecurringExpensesById,
  deleteApiPortfolioRecurringExpensesByIdChangesByMonth,
  deleteApiPortfolioRecurringIncomesById,
  deleteApiPortfolioRecurringIncomesByIdChangesByMonth,
  deleteApiPortfolioRecurringIncomesByIdOccurrencesByMonth,
  getApiPortfolioPlanningMonthsByMonth,
  putApiPortfolioRecurringIncomesByIdOccurrencesByMonthSkip,
  type AllocationBucketResponse,
  type AllocationResponse,
  type ExpenseDefinitionResponse,
  type IncomeDefinitionResponse,
  type IncomeOccurrenceResponse,
  type OccurrenceStatus,
  type OverdueOccurrenceResponse,
  type RecurringIncomeStatus,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { PlanningState } from '../../core/planning/planning-state';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { fromDateOnly } from '../../shared/date-only';
import { formatDate, formatMoney, formatMonth, formatPercent } from '../../shared/format';
import {
  AllocationPlanDialog,
  type AllocationPlanDialogData,
} from './allocation-plan-dialog/allocation-plan-dialog';
import {
  BucketBalanceDialog,
  type BucketBalanceDialogData,
} from './bucket-balance-dialog/bucket-balance-dialog';
import { BUCKET_KIND, SHARE_TYPE, bucketKindKey, isLimitedKind } from './allocation';
import {
  EndRecurringExpenseDialog,
  type EndRecurringExpenseDialogData,
} from './end-recurring-expense-dialog/end-recurring-expense-dialog';
import {
  EndRecurringIncomeDialog,
  type EndRecurringIncomeDialogData,
} from './end-recurring-income-dialog/end-recurring-income-dialog';
import {
  RecurringExpenseFormDialog,
  type RecurringExpenseFormDialogData,
} from './recurring-expense-form-dialog/recurring-expense-form-dialog';
import {
  RecurringIncomeFormDialog,
  type RecurringIncomeFormDialogData,
} from './recurring-income-form-dialog/recurring-income-form-dialog';
import { currentWarsawMonth, ruleParts } from './recurrence';
import {
  SettleOccurrenceDialog,
  type SettleOccurrenceDialogData,
} from './settle-occurrence-dialog/settle-occurrence-dialog';

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

const OCCURRENCE_STATUS_KEYS: Record<OccurrenceStatus, string> = {
  Pending: 'planning.occurrence.status.pending',
  Settled: 'planning.occurrence.status.settled',
  Skipped: 'planning.occurrence.status.skipped',
};

const STATUS_KEYS: Record<RecurringIncomeStatus, string> = {
  Upcoming: 'planning.status.upcoming',
  Active: 'planning.status.active',
  Ended: 'planning.status.ended',
};

function shiftMonth(month: string, delta: number): string {
  const [year, number] = month.split('-').map(Number);
  const index = year * 12 + (number - 1) + delta;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, '0')}`;
}

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-planning',
  imports: [
    NgTemplateOutlet,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './planning.html',
  styleUrl: './planning.scss',
})
export class Planning {
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly planningState = inject(PlanningState);

  protected readonly overdue = this.planningState.overdue;

  readonly month = input<string>();

  protected readonly selectedMonth = computed(() => {
    const requested = this.month();
    return requested && MONTH_PATTERN.test(requested) ? requested : currentWarsawMonth();
  });

  protected readonly monthLabel = computed(() =>
    formatMonth(fromDateOnly(`${this.selectedMonth()}-01`)),
  );

  protected readonly monthResource = resource({
    params: () => ({ month: this.selectedMonth() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPlanningMonthsByMonth({
        path: { month: params.month },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly incomes = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().incomes : null,
  );

  protected readonly expenses = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().expenses : null,
  );

  protected readonly freeFunds = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().freeFunds : null,
  );

  protected readonly allocation = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().allocation : null,
  );

  protected readonly cushionBuckets = computed(() =>
    (this.allocation()?.buckets ?? []).filter((bucket) => !!bucket.cushion),
  );

  protected readonly fixedBuckets = computed(() =>
    (this.allocation()?.buckets ?? []).filter(
      (bucket) => bucket.shareType !== null && Number(bucket.shareType) === SHARE_TYPE.Fixed,
    ),
  );

  protected readonly percentBuckets = computed(() =>
    (this.allocation()?.buckets ?? []).filter(
      (bucket) => bucket.shareType !== null && Number(bucket.shareType) === SHARE_TYPE.Percent,
    ),
  );

  protected readonly estimatedLimitYear = computed(() => {
    const estimated = (this.allocation()?.buckets ?? []).find((bucket) => bucket.limit?.estimated);
    return estimated?.limit ? Number(estimated.limit.year) : null;
  });

  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;
  protected readonly formatPercent = formatPercent;
  protected readonly ruleParts = ruleParts;
  protected readonly bucketKindKey = bucketKindKey;

  protected statusKey(status: RecurringIncomeStatus): string {
    return STATUS_KEYS[status];
  }

  protected occurrenceStatusKey(status: OccurrenceStatus): string {
    return OCCURRENCE_STATUS_KEYS[status];
  }

  protected isMultiMonth(definition: Pick<ExpenseDefinitionResponse, 'intervalMonths'>): boolean {
    return Number(definition.intervalMonths) > 1;
  }

  protected isNegative(amount: number | string): boolean {
    return Number(amount) < 0;
  }

  protected isPositive(amount: number | string): boolean {
    return Number(amount) > 0;
  }

  protected periodLabel(definition: Pick<IncomeDefinitionResponse, 'startDate' | 'endDate'>): {
    key: string;
    params: Record<string, string>;
  } {
    return definition.endDate
      ? {
          key: 'planning.definitions.periodClosed',
          params: { start: formatDate(definition.startDate), end: formatDate(definition.endDate) },
        }
      : {
          key: 'planning.definitions.periodOpen',
          params: { start: formatDate(definition.startDate) },
        };
  }

  protected goToMonth(delta: number): void {
    void this.router.navigate([], {
      queryParams: { month: shiftMonth(this.selectedMonth(), delta) },
      queryParamsHandling: 'merge',
    });
  }

  protected openAddDialog(): void {
    const ref = this.dialog.open(RecurringIncomeFormDialog, { width: '520px' });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openEditDialog(definition: IncomeDefinitionResponse): void {
    const data: RecurringIncomeFormDialogData = { definition };
    const ref = this.dialog.open(RecurringIncomeFormDialog, { width: '520px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openSettleDialog(occurrence: IncomeOccurrenceResponse): void {
    const data: SettleOccurrenceDialogData = { occurrence };
    const ref = this.dialog.open(SettleOccurrenceDialog, { width: '420px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected async skipOccurrence(
    occurrence: Pick<IncomeOccurrenceResponse, 'recurringIncomeId' | 'nominalMonth'>,
  ): Promise<void> {
    const result = await putApiPortfolioRecurringIncomesByIdOccurrencesByMonthSkip({
      path: { id: occurrence.recurringIncomeId, month: occurrence.nominalMonth },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.occurrence.skipFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAll();
  }

  protected openSettleOverdueDialog(item: OverdueOccurrenceResponse): void {
    this.openSettleDialog({
      ...item,
      amountPln: null,
      estimated: false,
      status: 'Pending',
      outOfRule: false,
      plannedAccountId: item.accountId,
      actualAmount: null,
      actualDate: null,
      difference: null,
      actualAmountPln: null,
      depositCreated: false,
      overdue: true,
    });
  }

  protected async undoOccurrence(occurrence: IncomeOccurrenceResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.occurrence.undoTitle'),
            message: translate('planning.occurrence.undoMessage', {
              name: occurrence.name,
              date: formatDate(occurrence.date),
            }),
            confirmLabel: translate('planning.occurrence.undo'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioRecurringIncomesByIdOccurrencesByMonth({
      path: { id: occurrence.recurringIncomeId, month: occurrence.nominalMonth },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.occurrence.undoFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAll();
  }

  protected changeMonthLabel(effectiveFrom: string): string {
    const [year, month] = effectiveFrom.split('-');
    return `${month}.${year}`;
  }

  protected async removeChange(definition: IncomeDefinitionResponse): Promise<void> {
    const change = definition.nextChange;
    if (!change) {
      return;
    }

    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.change.deleteTitle'),
            message: translate('planning.change.deleteMessage', {
              name: definition.name,
              month: this.changeMonthLabel(change.effectiveFrom),
            }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioRecurringIncomesByIdChangesByMonth({
      path: { id: definition.id, month: change.effectiveFrom },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.change.deleteFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAll();
  }

  protected openEndDialog(definition: IncomeDefinitionResponse): void {
    const data: EndRecurringIncomeDialogData = {
      id: definition.id,
      name: definition.name,
      startDate: definition.startDate,
    };
    const ref = this.dialog.open(EndRecurringIncomeDialog, { width: '420px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected async remove(definition: IncomeDefinitionResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.delete.title'),
            message: translate('planning.delete.message', { name: definition.name }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioRecurringIncomesById({ path: { id: definition.id } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAll();
  }

  protected openAddExpenseDialog(): void {
    const ref = this.dialog.open(RecurringExpenseFormDialog, { width: '520px' });
    this.reloadMonthWhenSaved(ref.afterClosed());
  }

  protected openEditExpenseDialog(definition: ExpenseDefinitionResponse): void {
    const data: RecurringExpenseFormDialogData = { definition };
    const ref = this.dialog.open(RecurringExpenseFormDialog, { width: '520px', data });
    this.reloadMonthWhenSaved(ref.afterClosed());
  }

  protected async removeExpenseChange(definition: ExpenseDefinitionResponse): Promise<void> {
    const change = definition.nextChange;
    if (!change) {
      return;
    }

    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.change.deleteTitle'),
            message: translate('planning.change.deleteMessage', {
              name: definition.name,
              month: this.changeMonthLabel(change.effectiveFrom),
            }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioRecurringExpensesByIdChangesByMonth({
      path: { id: definition.id, month: change.effectiveFrom },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.change.deleteFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.monthResource.reload();
  }

  protected openEndExpenseDialog(definition: ExpenseDefinitionResponse): void {
    const data: EndRecurringExpenseDialogData = { id: definition.id, name: definition.name };
    const ref = this.dialog.open(EndRecurringExpenseDialog, { width: '420px', data });
    this.reloadMonthWhenSaved(ref.afterClosed());
  }

  protected async removeExpense(definition: ExpenseDefinitionResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.expenses.delete.title'),
            message: translate('planning.expenses.delete.message', { name: definition.name }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioRecurringExpensesById({ path: { id: definition.id } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.expenses.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.monthResource.reload();
  }

  protected openAllocationDialog(allocation: AllocationResponse): void {
    const data: AllocationPlanDialogData = { month: this.selectedMonth(), allocation };
    const ref = this.dialog.open(AllocationPlanDialog, { width: '960px', data });
    this.reloadMonthWhenSaved(ref.afterClosed());
  }

  protected async removeAllocationChange(nextChange: string): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.allocation.deleteTitle'),
            message: translate('planning.allocation.deleteMessage', {
              month: this.changeMonthLabel(nextChange),
            }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioAllocationPlanVersionsByMonth({
      path: { month: nextChange },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.allocation.deleteFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.monthResource.reload();
  }

  protected hasBalance(bucket: AllocationBucketResponse): boolean {
    return Number(bucket.kind) === BUCKET_KIND.Cushion || isLimitedKind(bucket.kind);
  }

  protected openBucketBalanceDialog(bucket: AllocationBucketResponse): void {
    const data: BucketBalanceDialogData = { month: this.selectedMonth(), bucket };
    const ref = this.dialog.open(BucketBalanceDialog, { width: '420px', data });
    this.reloadMonthWhenSaved(ref.afterClosed());
  }

  protected async removeBucketAdjustment(
    bucket: AllocationBucketResponse,
    adjustmentMonth: string,
  ): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.allocation.balance.deleteTitle'),
            message: translate('planning.allocation.balance.deleteMessage', {
              name: bucket.name,
              month: this.changeMonthLabel(adjustmentMonth),
            }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioAllocationBucketsByBucketIdBalancesByMonth({
      path: { bucketId: bucket.bucketId, month: adjustmentMonth },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ??
          translate('planning.allocation.balance.deleteFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.monthResource.reload();
  }

  private reloadMonthWhenSaved(closed: {
    subscribe(next: (saved: boolean | undefined) => void): unknown;
  }): void {
    closed.subscribe((saved) => {
      if (saved) {
        this.monthResource.reload();
      }
    });
  }

  private reloadAll(): void {
    this.monthResource.reload();
    this.planningState.reloadOverdue();
  }

  private reloadWhenSaved(closed: {
    subscribe(next: (saved: boolean | undefined) => void): unknown;
  }): void {
    closed.subscribe((saved) => {
      if (saved) {
        this.reloadAll();
      }
    });
  }
}
