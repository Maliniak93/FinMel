import { BreakpointObserver } from '@angular/cdk/layout';
import { DOCUMENT } from '@angular/common';
import {
  Component,
  afterRenderEffect,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom, map } from 'rxjs';

import {
  deleteApiPortfolioOneOffExpensesById,
  deleteApiPortfolioOneOffExpensesByIdSettlement,
  deleteApiPortfolioOneOffIncomesById,
  deleteApiPortfolioOneOffIncomesByIdSettlement,
  deleteApiPortfolioRecurringExpensesById,
  deleteApiPortfolioRecurringExpensesByIdChangesByMonth,
  deleteApiPortfolioRecurringExpensesByIdPaymentsByMonthSettlement,
  deleteApiPortfolioRecurringIncomesById,
  deleteApiPortfolioRecurringIncomesByIdChangesByMonth,
  deleteApiPortfolioRecurringIncomesByIdOccurrencesByMonth,
  getApiPortfolioAssetGroups,
  getApiPortfolioBonds,
  getApiPortfolioDeposits,
  getApiPortfolioPlanningMonthsByMonth,
  putApiPortfolioAssetGroupsShares,
  putApiPortfolioPlanningSettings,
  putApiPortfolioRecurringIncomesByIdOccurrencesByMonthSkip,
  type AssetClass,
  type ExpenseDefinitionResponse,
  type IncomeDefinitionResponse,
  type IncomeOccurrenceResponse,
  type OneOffExpenseResponse,
  type OneOffIncomeResponse,
  type RecurringIncomeStatus,
  type SplitGroupResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import {
  type OverdueItem,
  type OverdueOneOffIncome,
  PlanningState,
  isOverdueOneOff,
} from '../../core/planning/planning-state';
import { PHONE_QUERY } from '../../shared/breakpoints';
import { Collapse } from '../../shared/collapse/collapse';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { PageHeader } from '../../shared/page-header/page-header';
import { fromDateOnly } from '../../shared/date-only';
import { activeLocale } from '../../core/i18n/language';
import {
  type CascadeLabels,
  type CascadeSplitRow,
  FreeFundsCascade,
} from '../../shared/free-funds-cascade/free-funds-cascade';
import { groupColor } from '../../shared/colors';
import { formatDate, formatMoney, formatMonth } from '../../shared/format';
import { SectionError } from '../../shared/section-error/section-error';
import { SummaryStrip, type SummaryItem } from '../../shared/summary-strip/summary-strip';
import { assetClassLabel } from '../assets/asset-class';
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
import { ContributionLimitWarning } from './contribution-limit-warning/contribution-limit-warning';
import {
  PlanningDayList,
  type ExpenseRow,
  type IncomeRow,
} from './planning-day-list/planning-day-list';
import { PlanningYear } from './planning-year/planning-year';
import { currentWarsawMonth, ruleParts } from './recurrence';
import {
  SettleExpenseDialog,
  type SettleExpenseDialogData,
} from './settle-expense-dialog/settle-expense-dialog';
import {
  SettleOccurrenceDialog,
  type SettleOccurrenceDialogData,
} from './settle-occurrence-dialog/settle-occurrence-dialog';

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

function toGrosze(amount: number | string): number {
  return Math.round(Number(amount) * 100);
}

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
    Collapse,
    PageHeader,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    FreeFundsCascade,
    MatTabsModule,
    ContributionLimitWarning,
    PlanningCalendar,
    PlanningDayList,
    PlanningYear,
    SectionError,
    SummaryStrip,
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

  protected readonly phone = toSignal(
    inject(BreakpointObserver)
      .observe(PHONE_QUERY)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly showCalendar = computed(() => this.layout() === 'calendar' && !this.phone());

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

  protected readonly monthData = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value() : null,
  );

  private readonly depositsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioDeposits({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.calendar.deadlinesFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly bondsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioBonds({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.calendar.deadlinesFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly groupsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAssetGroups({ signal: abortSignal });
      return result.error ? [] : (result.data ?? []);
    },
  });

  protected readonly deposits = computed(() =>
    this.depositsResource.hasValue() ? this.depositsResource.value() : [],
  );

  protected readonly bonds = computed(() =>
    this.bondsResource.hasValue() ? this.bondsResource.value() : [],
  );

  protected readonly groups = computed(() =>
    this.groupsResource.hasValue() ? this.groupsResource.value() : [],
  );

  protected readonly deadlinesFailed = computed(
    () => !!this.depositsResource.error() || !!this.bondsResource.error(),
  );

  protected readonly summaryItems = computed<SummaryItem[]>(() => {
    const data = this.monthData();
    return [
      {
        labelKey: 'planning.incomes.title',
        amount: data ? Number(data.incomes.balance.planPln) : null,
        testId: 'summary-incomes',
      },
      {
        labelKey: 'planning.balance.pending',
        amount: data ? Number(data.incomes.balance.pendingPln) : null,
        testId: 'summary-pending',
      },
      {
        labelKey: 'planning.freeFunds.title',
        amount: data ? Number(data.freeFunds.planPln) : null,
        testId: 'summary-free-funds',
      },
    ];
  });

  protected readonly freeFunds = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value().freeFunds : null,
  );

  protected readonly cascadeLabels = computed<CascadeLabels>(() => {
    activeLocale();
    return {
      incomes: translate('dashboard.monthCard.incomes'),
      expenses: translate('dashboard.monthCard.expenses'),
      contributions: translate('dashboard.monthCard.contributions'),
      free: translate('dashboard.monthCard.free'),
      deficit: translate('planning.freeFunds.deficit'),
      plan: translate('planning.freeFunds.plan'),
      actual: translate('planning.freeFunds.actual'),
    };
  });

  protected readonly cascadeTestIds = {
    'plan-free': 'free-funds-plan',
    'actual-free': 'free-funds-actual',
    'free-deficit': 'free-funds-deficit',
    'actual-free-deficit': 'free-funds-deficit',
  };

  protected readonly cascadeDeposits = computed<CascadeSplitRow[]>(() => {
    activeLocale();
    return (this.monthData()?.expenses.groupDeposits ?? []).map((deposit) => ({
      key: deposit.groupId,
      label: translate('planning.contribution.deposit', { name: deposit.name }),
      amountPln: Number(deposit.totalPln),
    }));
  });

  protected readonly cascade = computed(() => {
    const data = this.monthData();
    if (!data) {
      return null;
    }
    const contributionsGrosze = data.expenses.groupContributions.reduce(
      (sum, contribution) => sum + toGrosze(contribution.amountPln),
      0,
    );
    const expenses = (toGrosze(data.expenses.costPln) - contributionsGrosze) / 100;
    const contributions = contributionsGrosze / 100;
    const balance = data.incomes.balance;
    const fundedGrosze = data.expenses.groupDeposits.reduce(
      (sum, deposit) => sum + toGrosze(deposit.fromIncomePln),
      0,
    );
    const actualIncomesGrosze = toGrosze(balance.actualPln);
    return {
      plan: {
        incomes: Number(balance.planPln),
        expenses,
        contributions,
        free: Number(data.freeFunds.planPln),
      },
      actual: {
        incomes: actualIncomesGrosze / 100,
        expenses: (actualIncomesGrosze - fundedGrosze - toGrosze(data.freeFunds.actualPln)) / 100,
        contributions: fundedGrosze / 100,
        free: Number(data.freeFunds.actualPln),
      },
    };
  });

  protected readonly split = computed(() => this.monthData()?.split ?? null);

  private readonly expandedGroups = signal<ReadonlySet<string>>(new Set());
  private readonly shareEdits = signal<Readonly<Record<string, string>>>({});

  // Tenths of a percent, so the running sum never meets floating-point noise.
  private readonly shareTenths = computed(() =>
    (this.split()?.groups ?? []).map((group) => this.tenthsOf(this.shareValue(group))),
  );

  protected readonly shareSum = computed(() => {
    const tenths = this.shareTenths();
    return tenths.some((value) => value === null)
      ? null
      : tenths.reduce<number>((sum, value) => sum + (value ?? 0), 0) / 10;
  });

  protected readonly sharesSavable = computed(
    () =>
      this.shareSum() === 100 &&
      (this.split()?.groups ?? []).some(
        (group) =>
          this.tenthsOf(this.shareValue(group)) !== this.tenthsOf(String(group.sharePercent)),
      ),
  );

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
  protected readonly ruleParts = ruleParts;

  protected statusKey(status: RecurringIncomeStatus): string {
    return STATUS_KEYS[status];
  }

  protected isMultiMonth(definition: Pick<ExpenseDefinitionResponse, 'intervalMonths'>): boolean {
    return Number(definition.intervalMonths) > 1;
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
      this.openSettle({
        oneOffIncome: { ...common, id: item.oneOffIncomeId, inMonth: true, planned: true },
      });
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

  protected openSettleExpenseDialog(row: ExpenseRow): void {
    const data: SettleExpenseDialogData = row.oneOff
      ? { oneOffExpense: row.item }
      : { payment: row.item };
    const ref = this.dialog.open(SettleExpenseDialog, { width: '420px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected async undoExpense(row: ExpenseRow): Promise<void> {
    if (!(await this.confirmUndo('planning.expenses.undoMessage', row.item))) {
      return;
    }

    const result = row.oneOff
      ? await deleteApiPortfolioOneOffExpensesByIdSettlement({ path: { id: row.item.id } })
      : await deleteApiPortfolioRecurringExpensesByIdPaymentsByMonthSettlement({
          path: { id: row.item.recurringExpenseId, month: row.item.nominalMonth },
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

  protected async undoOccurrence(row: IncomeRow): Promise<void> {
    if (!(await this.confirmUndo('planning.occurrence.undoMessage', row.item))) {
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

  protected assetClassLabel(assetClass: AssetClass): string {
    return assetClassLabel(assetClass);
  }

  protected isExpanded(groupId: string): boolean {
    return this.expandedGroups().has(groupId);
  }

  protected toggleBreakdown(groupId: string): void {
    this.expandedGroups.update((expanded) => {
      const next = new Set(expanded);
      if (!next.delete(groupId)) {
        next.add(groupId);
      }
      return next;
    });
  }

  protected groupColorOf(groupId: string): string {
    return groupColor(this.groups().find((group) => group.id === groupId)?.color);
  }

  protected shareValue(group: SplitGroupResponse): string {
    return this.shareEdits()[group.groupId] ?? String(group.sharePercent);
  }

  protected editShare(group: SplitGroupResponse, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.shareEdits.update((edits) => ({ ...edits, [group.groupId]: value }));
  }

  protected async saveShares(): Promise<void> {
    const groups = this.split()?.groups ?? [];
    const result = await putApiPortfolioAssetGroupsShares({
      body: {
        shares: groups.map((group) => ({
          groupId: group.groupId,
          sharePercent: Number(this.shareValue(group)),
        })),
      },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.split.sharesFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.shareEdits.set({});
    this.reloadAll();
  }

  private tenthsOf(value: string): number | null {
    const number = Number(value);
    const tenths = Math.round(number * 10);
    return value.trim() === '' ||
      !Number.isFinite(number) ||
      number < 0 ||
      number > 100 ||
      Math.abs(number * 10 - tenths) > 1e-9
      ? null
      : tenths;
  }

  protected async savePaymentLeadDays(event: Event): Promise<void> {
    const raw = (event.target as HTMLInputElement).value.trim();
    if (raw === '') {
      this.planningState.reload();
      return;
    }

    await this.savePlanningSettings(Number(raw));
  }

  private async savePlanningSettings(paymentLeadDays: number): Promise<void> {
    const result = await putApiPortfolioPlanningSettings({ body: { paymentLeadDays } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('planning.paymentLeadDaysFailed'),
        translate('common.dismiss'),
      );
    }

    this.reloadAll();
  }

  private confirmUndo(messageKey: string, item: { name: string; date: string }): Promise<boolean> {
    return firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('planning.occurrence.undoTitle'),
            message: translate(messageKey, { name: item.name, date: formatDate(item.date) }),
            confirmLabel: translate('planning.occurrence.undo'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
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
