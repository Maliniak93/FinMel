import { BreakpointObserver } from '@angular/cdk/layout';
import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import {
  getApiPortfolioPlanningYearsByYear,
  type PlanningYearMonthResponse,
  type PlanningYearTotalsResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { PHONE_QUERY } from '../../../shared/breakpoints';
import { Collapse } from '../../../shared/collapse/collapse';
import { fromDateOnly } from '../../../shared/date-only';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { formatMoney, formatMonth } from '../../../shared/format';
import { SectionError } from '../../../shared/section-error/section-error';
import { currentWarsawMonth } from '../recurrence';

const YEAR_PATTERN = /^\d{4}$/;

type ValueColumn = keyof PlanningYearTotalsResponse;

interface ColumnDefinition {
  key: ValueColumn;
  label: string;
  phone: boolean;
}

const VALUE_COLUMNS: ColumnDefinition[] = [
  { key: 'incomePlanPln', label: 'planning.year.columns.incomePlan', phone: false },
  { key: 'incomeActualPln', label: 'planning.year.columns.incomeActual', phone: false },
  { key: 'differencePln', label: 'planning.year.columns.difference', phone: false },
  { key: 'costPln', label: 'planning.year.columns.cost', phone: true },
  { key: 'freeFundsPlanPln', label: 'planning.year.columns.freeFundsPlan', phone: false },
  { key: 'freeFundsActualPln', label: 'planning.year.columns.freeFundsActual', phone: true },
];

const FREE_FUNDS_KEYS: ValueColumn[] = ['freeFundsPlanPln', 'freeFundsActualPln'];

interface YearCell {
  key: ValueColumn;
  label: string;
  phone: boolean;
  text: string;
  deficit: boolean;
  actual: boolean;
}

interface YearRow {
  month: string;
  label: string;
  current: boolean;
  overdue: boolean;
  incomplete: boolean;
  cells: YearCell[];
}

@Component({
  selector: 'app-planning-year',
  imports: [
    Collapse,
    EmptyState,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    NgTemplateOutlet,
    SectionError,
    TranslocoPipe,
  ],
  templateUrl: './planning-year.html',
  styleUrl: './planning-year.scss',
})
export class PlanningYear {
  private readonly router = inject(Router);

  readonly year = input<string | number>();

  protected readonly columns = VALUE_COLUMNS;

  protected readonly phone = toSignal(
    inject(BreakpointObserver)
      .observe(PHONE_QUERY)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly expanded = signal<readonly string[]>([]);

  protected readonly selectedYear = computed(() => {
    const requested = String(this.year() ?? '');
    return YEAR_PATTERN.test(requested)
      ? Number(requested)
      : Number(currentWarsawMonth().slice(0, 4));
  });

  protected readonly yearResource = resource({
    params: () => ({ year: this.selectedYear() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPlanningYearsByYear({
        path: { year: params.year },
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

  protected readonly months = computed(() =>
    this.yearResource.hasValue() ? this.yearResource.value().months : [],
  );

  protected readonly hasDefinitions = computed(() =>
    this.months().some((month) => month.hasDefinitions),
  );

  protected readonly rows = computed<YearRow[]>(() => {
    const now = currentWarsawMonth();
    return this.months().map((month) => ({
      month: month.month,
      label: formatMonth(fromDateOnly(`${month.month}-01`)),
      current: month.month === now,
      overdue: month.hasOverdue,
      incomplete: month.incomplete,
      cells: VALUE_COLUMNS.map((column) => this.cell(month, column, month.month < now)),
    }));
  });

  protected readonly totals = computed<YearCell[]>(() => {
    if (!this.yearResource.hasValue()) {
      return [];
    }

    const { totals } = this.yearResource.value();
    return VALUE_COLUMNS.map((column) => ({
      ...column,
      text: formatMoney(totals[column.key], 'PLN'),
      deficit: false,
      actual: false,
    }));
  });

  protected readonly hasPrevious = computed(
    () =>
      this.yearResource.hasValue() &&
      this.selectedYear() > Number(this.yearResource.value().firstYear),
  );

  protected readonly hasNext = computed(
    () =>
      this.yearResource.hasValue() &&
      this.selectedYear() < Number(this.yearResource.value().lastYear),
  );

  protected goToYear(delta: number): void {
    void this.router.navigate([], {
      queryParams: { tab: 'year', year: this.selectedYear() + delta },
      queryParamsHandling: 'merge',
    });
  }

  protected openMonth(month: string): void {
    void this.router.navigate(['/planning'], { queryParams: { month } });
  }

  protected isExpanded(month: string): boolean {
    return this.expanded().includes(month);
  }

  protected toggleLabel(month: string): string {
    return this.isExpanded(month) ? 'planning.year.collapse' : 'planning.year.expand';
  }

  protected toggle(month: string, event: Event): void {
    event.stopPropagation();
    this.expanded.update((open) =>
      open.includes(month) ? open.filter((item) => item !== month) : [...open, month],
    );
  }

  private cell(
    month: PlanningYearMonthResponse,
    column: ColumnDefinition,
    past: boolean,
  ): YearCell {
    const base = { key: column.key, label: column.label, phone: column.phone };
    if (!month.hasDefinitions) {
      return { ...base, text: '—', deficit: false, actual: false };
    }

    const value = month[column.key];
    if (value === null || value === undefined) {
      return { ...base, text: '', deficit: false, actual: false };
    }

    return {
      ...base,
      text: formatMoney(value, 'PLN'),
      deficit: FREE_FUNDS_KEYS.includes(column.key) && Number(value) < 0,
      actual: past && column.key === 'freeFundsActualPln',
    };
  }
}
