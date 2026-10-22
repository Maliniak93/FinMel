import { Component, computed, inject, input, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPlanningYearsByYear,
  type PlanningYearMonthResponse,
  type PlanningYearTotalsResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { fromDateOnly } from '../../../shared/date-only';
import { formatMoney, formatMonth } from '../../../shared/format';
import { currentWarsawMonth } from '../recurrence';

const YEAR_PATTERN = /^\d{4}$/;

type ValueColumn = Exclude<keyof PlanningYearTotalsResponse, never>;

const VALUE_COLUMNS: { key: ValueColumn; label: string }[] = [
  { key: 'incomePlanPln', label: 'planning.year.columns.incomePlan' },
  { key: 'incomeActualPln', label: 'planning.year.columns.incomeActual' },
  { key: 'differencePln', label: 'planning.year.columns.difference' },
  { key: 'costPln', label: 'planning.year.columns.cost' },
  { key: 'freeFundsPlanPln', label: 'planning.year.columns.freeFundsPlan' },
  { key: 'freeFundsForecastPln', label: 'planning.year.columns.freeFundsForecast' },
];

interface YearRow {
  month: string;
  label: string;
  current: boolean;
  overdue: boolean;
  cells: string[];
}

@Component({
  selector: 'app-planning-year',
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoPipe],
  templateUrl: './planning-year.html',
  styleUrl: './planning-year.scss',
})
export class PlanningYear {
  private readonly router = inject(Router);

  readonly year = input<string | number>();

  protected readonly columns = VALUE_COLUMNS;

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

  protected readonly rows = computed<YearRow[]>(() => {
    if (!this.yearResource.hasValue()) {
      return [];
    }

    const now = currentWarsawMonth();
    return this.yearResource.value().months.map((month) => ({
      month: month.month,
      label: formatMonth(fromDateOnly(`${month.month}-01`)),
      current: month.month === now,
      overdue: month.hasOverdue,
      cells: VALUE_COLUMNS.map((column) => this.cell(month, column.key)),
    }));
  });

  protected readonly totals = computed(() => {
    if (!this.yearResource.hasValue()) {
      return [];
    }

    const { totals } = this.yearResource.value();
    return VALUE_COLUMNS.map((column) => formatMoney(totals[column.key], 'PLN'));
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

  private cell(month: PlanningYearMonthResponse, key: ValueColumn): string {
    if (!month.hasDefinitions) {
      return '—';
    }

    const value = month[key];
    return value === null || value === undefined ? '' : formatMoney(value, 'PLN');
  }
}
