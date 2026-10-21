import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { Component, afterRenderEffect, computed, inject, input, resource } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioAllocationBucketsByBucketIdBalancesByMonth,
  deleteApiPortfolioAllocationPlanVersionsByMonth,
  deleteApiPortfolioOneOffExpensesById,
  deleteApiPortfolioOneOffIncomesById,
  deleteApiPortfolioOneOffIncomesByIdSettlement,
  deleteApiPortfolioRecurringExpensesById,
  deleteApiPortfolioRecurringExpensesByIdChangesByMonth,
  deleteApiPortfolioRecurringIncomesById,
  deleteApiPortfolioRecurringIncomesByIdChangesByMonth,
  deleteApiPortfolioRecurringIncomesByIdOccurrencesByMonth,
  deleteApiPortfolioTransferPlansByMonthSkip,
  getApiPortfolioPlanningMonthsByMonth,
  getApiPortfolioPlanningSettings,
  postApiPortfolioTransferPlansByMonthAccept,
  postApiPortfolioTransferPlansByMonthSkip,
  postApiPortfolioTransferPlansByMonthTopUpAccept,
  postApiPortfolioTransferPlansByMonthUndo,
  putApiPortfolioPlanningSettings,
  putApiPortfolioRecurringIncomesByIdOccurrencesByMonthSkip,
  putApiPortfolioTransferPlansRowsByRowIdChecked,
  type AllocationBucketResponse,
  type AllocationResponse,
  type ExpenseDefinitionResponse,
  type IncomeDefinitionResponse,
  type IncomeOccurrenceResponse,
  type OccurrenceStatus,
  type OneOffExpenseResponse,
  type OneOffIncomeResponse,
  type RecurringIncomeStatus,
  type TransferMarker,
  type TransferPlanFallbackReason,
  type TransferPlanStatus,
  type TransferRowResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import {
  type OverdueItem,
  type OverdueOneOffIncome,
  PlanningState,
  isOverdueOneOff,
} from '../../core/planning/planning-state';
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
import { PlanningCalendar } from './planning-calendar/planning-calendar';
import { PlanningYear } from './planning-year/planning-year';
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

const TRANSFER_MARKER_KEYS: Record<TransferMarker, string | null> = {
  None: null,
  AboveTarget: 'planning.transferPlan.markers.aboveTarget',
  OutsideAllocation: 'planning.transferPlan.markers.outsideAllocation',
  BelowMinimum: 'planning.transferPlan.markers.belowMinimum',
};

const TRANSFER_MARKER_TEST_IDS: Record<TransferMarker, string | null> = {
  None: null,
  AboveTarget: 'transfer-marker-above-target',
  OutsideAllocation: 'transfer-marker-outside-allocation',
  BelowMinimum: 'transfer-marker-below-minimum',
};

const TRANSFER_FALLBACK_KEYS: Record<TransferPlanFallbackReason, string> = {
  NoTargetAllocation: 'planning.transferPlan.fallback.NoTargetAllocation',
  NoValuation: 'planning.transferPlan.fallback.NoValuation',
  EmptyBase: 'planning.transferPlan.fallback.EmptyBase',
};

const TRANSFER_STATUS_KEYS: Record<TransferPlanStatus, string | null> = {
  None: null,
  Preview: 'planning.transferPlan.status.preview',
  Accepted: 'planning.transferPlan.status.accepted',
  Completed: 'planning.transferPlan.status.completed',
  Skipped: 'planning.transferPlan.status.skipped',
};

type IncomeRow =
  | { oneOff: false; key: string; item: IncomeOccurrenceResponse }
  | { oneOff: true; key: string; item: OneOffIncomeResponse };

interface TransferBatchGroup {
  number: number | null;
  title: { key: string; params: Record<string, number> } | null;
  rows: TransferRowResponse[];
}

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
    MatTabsModule,
    PlanningCalendar,
    PlanningYear,
    RouterLink,
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
  private readonly document = inject(DOCUMENT);
  private readonly fragment = toSignal(inject(ActivatedRoute).fragment);
  private scrolledTo: string | null = null;

  protected readonly overdue = this.planningState.overdue;
  protected readonly paymentLeadDays = this.planningState.paymentLeadDays;

  readonly month = input<string>();
  readonly tab = input<string>();
  readonly year = input<string>();
  readonly layout = input<string>();

  protected readonly calendarLayout = computed(() => this.layout() === 'calendar');

  protected readonly tabIndex = computed(() => (this.tab() === 'year' ? 1 : 0));

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

  private readonly settingsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPlanningSettings({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly monthData = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value() : null,
  );

  protected readonly incomes = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().incomes : null,
  );

  protected readonly expenses = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().expenses : null,
  );

  protected readonly incomeRows = computed<IncomeRow[]>(() => {
    const plan = this.incomes();
    if (!plan) {
      return [];
    }

    return [
      ...plan.occurrences.map((item): IncomeRow => ({
        oneOff: false,
        key: item.recurringIncomeId + item.nominalMonth,
        item,
      })),
      ...plan.oneOffs
        .filter((item) => item.inMonth)
        .map((item): IncomeRow => ({ oneOff: true, key: `one-off-${item.id}`, item })),
    ];
  });

  protected readonly monthOneOffExpenses = computed(() =>
    (this.expenses()?.oneOffs ?? []).filter((item) => item.inMonth),
  );

  protected readonly freeFunds = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().freeFunds : null,
  );

  protected readonly allocation = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().allocation : null,
  );

  protected readonly transferPlan = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().transferPlan : null,
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

  constructor() {
    afterRenderEffect(() => {
      const fragment = this.fragment();
      if (!fragment || !this.monthResource.hasValue() || this.monthResource.isLoading()) {
        return;
      }

      const key = `${this.selectedMonth()}#${fragment}`;
      if (key === this.scrolledTo) {
        return;
      }

      this.scrolledTo = key;
      this.document.getElementById(fragment)?.scrollIntoView();
    });
  }

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

  protected selectTab(index: number): void {
    if (index === this.tabIndex()) {
      return;
    }

    void this.router.navigate([], {
      queryParams:
        index === 1
          ? { tab: 'year', year: this.year() ?? currentWarsawMonth().slice(0, 4) }
          : { tab: null, year: null },
      queryParamsHandling: 'merge',
    });
  }

  protected selectLayout(calendar: boolean): void {
    void this.router.navigate([], {
      queryParams: { layout: calendar ? 'calendar' : null },
      queryParamsHandling: 'merge',
    });
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

  protected openSettleDialog(row: IncomeRow): void {
    const data: SettleOccurrenceDialogData = row.oneOff
      ? { oneOffIncome: row.item }
      : { occurrence: row.item };
    this.openSettle(data);
  }

  protected isOneOffOverdue(item: OverdueItem): item is OverdueOneOffIncome {
    return isOverdueOneOff(item);
  }

  protected openEditOneOffIncomeDialog(oneOff: OneOffIncomeResponse): void {
    const data: RecurringIncomeFormDialogData = { oneOff };
    const ref = this.dialog.open(RecurringIncomeFormDialog, { width: '520px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openEditOneOffExpenseDialog(oneOff: OneOffExpenseResponse): void {
    const data: RecurringExpenseFormDialogData = { oneOff };
    const ref = this.dialog.open(RecurringExpenseFormDialog, { width: '520px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected async removeOneOffIncome(oneOff: OneOffIncomeResponse): Promise<void> {
    await this.removeOneOff(oneOff.name, () =>
      deleteApiPortfolioOneOffIncomesById({ path: { id: oneOff.id } }),
    );
  }

  protected async removeOneOffExpense(oneOff: OneOffExpenseResponse): Promise<void> {
    await this.removeOneOff(oneOff.name, () =>
      deleteApiPortfolioOneOffExpensesById({ path: { id: oneOff.id } }),
    );
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

  protected openSettleOverdueDialog(item: OverdueItem): void {
    const common = {
      accountId: item.accountId,
      accountName: item.accountName,
      accountArchived: item.accountArchived,
      name: item.name,
      date: item.date,
      amount: item.amount,
      currency: item.currency,
      amountPln: null,
      estimated: false,
      status: 'Pending' as const,
      actualAmount: null,
      actualDate: null,
      difference: null,
      actualAmountPln: null,
      depositCreated: false,
      overdue: true,
    };
    if (isOverdueOneOff(item)) {
      this.openSettle({ oneOffIncome: { ...common, id: item.oneOffIncomeId, inMonth: true } });
      return;
    }

    this.openSettle({
      occurrence: {
        ...common,
        recurringIncomeId: item.recurringIncomeId,
        nominalMonth: item.nominalMonth,
        outOfRule: false,
        plannedAccountId: item.accountId,
      },
    });
  }

  protected async undoOccurrence(row: IncomeRow): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.occurrence.undoTitle'),
            message: translate('planning.occurrence.undoMessage', {
              name: row.item.name,
              date: formatDate(row.item.date),
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

    const result = row.oneOff
      ? await deleteApiPortfolioOneOffIncomesByIdSettlement({ path: { id: row.item.id } })
      : await deleteApiPortfolioRecurringIncomesByIdOccurrencesByMonth({
          path: { id: row.item.recurringIncomeId, month: row.item.nominalMonth },
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
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openEditExpenseDialog(definition: ExpenseDefinitionResponse): void {
    const data: RecurringExpenseFormDialogData = { definition };
    const ref = this.dialog.open(RecurringExpenseFormDialog, { width: '520px', data });
    this.reloadWhenSaved(ref.afterClosed());
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

    this.reloadAll();
  }

  protected openEndExpenseDialog(definition: ExpenseDefinitionResponse): void {
    const data: EndRecurringExpenseDialogData = { id: definition.id, name: definition.name };
    const ref = this.dialog.open(EndRecurringExpenseDialog, { width: '420px', data });
    this.reloadWhenSaved(ref.afterClosed());
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

    this.reloadAll();
  }

  protected openAllocationDialog(allocation: AllocationResponse): void {
    const data: AllocationPlanDialogData = { month: this.selectedMonth(), allocation };
    const ref = this.dialog.open(AllocationPlanDialog, {
      width: '960px',
      maxWidth: '95vw',
      data,
    });
    this.reloadWhenSaved(ref.afterClosed());
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

    this.reloadAll();
  }

  protected transferMarkerKey(marker: TransferMarker): string | null {
    return TRANSFER_MARKER_KEYS[marker];
  }

  protected transferMarkerTestId(marker: TransferMarker): string | null {
    return TRANSFER_MARKER_TEST_IDS[marker];
  }

  protected transferFallbackKey(reason: TransferPlanFallbackReason): string {
    return TRANSFER_FALLBACK_KEYS[reason];
  }

  protected transferStatusKey(status: TransferPlanStatus): string | null {
    return TRANSFER_STATUS_KEYS[status];
  }

  protected transferBatches(rows: TransferRowResponse[]): TransferBatchGroup[] {
    const groups: TransferBatchGroup[] = [];
    for (const row of rows) {
      const number = row.batchNumber === null ? null : Number(row.batchNumber);
      let group = groups.find((candidate) => candidate.number === number);
      if (!group) {
        group = {
          number,
          title:
            number === null
              ? null
              : number === 1
                ? { key: 'planning.transferPlan.batch.plan', params: {} }
                : { key: 'planning.transferPlan.batch.topUp', params: { number: number - 1 } },
          rows: [],
        };
        groups.push(group);
      }
      group.rows.push(row);
    }
    return groups;
  }

  protected async acceptTransferTopUp(): Promise<void> {
    await this.runTransferAction(
      () =>
        postApiPortfolioTransferPlansByMonthTopUpAccept({ path: { month: this.selectedMonth() } }),
      'planning.transferPlan.topUp.acceptFailed',
    );
  }

  protected async acceptTransferPlan(): Promise<void> {
    await this.runTransferAction(
      () => postApiPortfolioTransferPlansByMonthAccept({ path: { month: this.selectedMonth() } }),
      'planning.transferPlan.acceptFailed',
    );
  }

  protected async skipTransferPlan(): Promise<void> {
    await this.runTransferAction(
      () => postApiPortfolioTransferPlansByMonthSkip({ path: { month: this.selectedMonth() } }),
      'planning.transferPlan.skipFailed',
    );
  }

  protected async unskipTransferPlan(): Promise<void> {
    await this.runTransferAction(
      () => deleteApiPortfolioTransferPlansByMonthSkip({ path: { month: this.selectedMonth() } }),
      'planning.transferPlan.unskipFailed',
    );
  }

  protected async undoTransferPlan(): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.transferPlan.undoTitle'),
            message: translate('planning.transferPlan.undoMessage'),
            confirmLabel: translate('planning.transferPlan.undo'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    await this.runTransferAction(
      () => postApiPortfolioTransferPlansByMonthUndo({ path: { month: this.selectedMonth() } }),
      'planning.transferPlan.undoFailed',
    );
  }

  protected async checkTransferRow(rowId: string, event: Event): Promise<void> {
    const box = event.target as HTMLInputElement;
    const checked = box.checked;
    const failed = await this.runTransferAction(
      () =>
        putApiPortfolioTransferPlansRowsByRowIdChecked({
          path: { rowId },
          body: { checked },
        }),
      'planning.transferPlan.checkFailed',
    );
    if (failed) {
      box.checked = !checked;
    }
  }

  protected async saveTransferMinimum(event: Event): Promise<void> {
    const raw = (event.target as HTMLInputElement).value.trim();
    if (raw === '') {
      this.monthResource.reload();
      return;
    }

    await this.savePlanningSettings(
      Number(raw),
      this.planningState.paymentLeadDays(),
      'planning.transferPlan.minimumFailed',
    );
  }

  protected async savePaymentLeadDays(event: Event): Promise<void> {
    const raw = (event.target as HTMLInputElement).value.trim();
    if (raw === '') {
      this.planningState.reload();
      return;
    }

    const minimum = this.settingsResource.hasValue()
      ? Number(this.settingsResource.value().minimumTransferPln)
      : Number(this.transferPlan()?.minimumTransferPln ?? 100);
    await this.savePlanningSettings(
      minimum,
      Number(raw),
      'planning.transferPlan.paymentLeadDaysFailed',
    );
  }

  protected hasBalance(bucket: AllocationBucketResponse): boolean {
    return Number(bucket.kind) === BUCKET_KIND.Cushion || isLimitedKind(bucket.kind);
  }

  protected openBucketBalanceDialog(bucket: AllocationBucketResponse): void {
    const data: BucketBalanceDialogData = { month: this.selectedMonth(), bucket };
    const ref = this.dialog.open(BucketBalanceDialog, { width: '420px', data });
    this.reloadWhenSaved(ref.afterClosed());
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

    this.reloadAll();
  }

  private async runTransferAction(
    call: () => Promise<{ error?: unknown }>,
    failedKey: string,
  ): Promise<boolean> {
    const result = await call();
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate(failedKey),
        translate('common.dismiss'),
      );
    }

    this.reloadAll();
    return !!result.error;
  }

  private async savePlanningSettings(
    minimumTransferPln: number,
    paymentLeadDays: number,
    failedKey: string,
  ): Promise<void> {
    const result = await putApiPortfolioPlanningSettings({
      body: { minimumTransferPln, paymentLeadDays },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate(failedKey),
        translate('common.dismiss'),
      );
    }

    this.settingsResource.reload();
    this.reloadAll();
  }

  private openSettle(data: SettleOccurrenceDialogData): void {
    const ref = this.dialog.open(SettleOccurrenceDialog, { width: '420px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  private async removeOneOff(
    name: string,
    call: () => Promise<{ error?: unknown }>,
  ): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.oneOff.delete.title'),
            message: translate('planning.oneOff.delete.message', { name }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await call();
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.oneOff.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAll();
  }

  private reloadAll(): void {
    this.monthResource.reload();
    this.planningState.reload();
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
