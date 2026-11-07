import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import type { PlanningYearMonthResponse } from '../../../api/portfolio';
import { activeLocale } from '../../../core/i18n/language';
import { fromDateOnly } from '../../../shared/date-only';
import { formatMoney, formatMonthShort } from '../../../shared/format';
import { currentWarsawMonth } from '../recurrence';

interface ChartColumn {
  month: string;
  label: string;
  current: boolean;
  empty: boolean;
  incomplete: boolean;
  deficit: boolean;
  planHeight: number;
  actualBottom: number;
  plan: number;
  actual: number;
}

@Component({
  selector: 'app-planning-year-chart',
  imports: [MatIconModule, TranslocoPipe],
  templateUrl: './planning-year-chart.html',
  styleUrl: './planning-year-chart.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanningYearChart {
  readonly months = input.required<readonly PlanningYearMonthResponse[]>();
  readonly year = input.required<number | string>();

  protected readonly columns = computed<ChartColumn[]>(() => {
    const months = this.months();
    const now = currentWarsawMonth();
    const values = months
      .filter((month) => month.hasDefinitions)
      .flatMap((month) => [
        Math.abs(Number(month.freeFundsPlanPln ?? 0)),
        Math.abs(Number(month.freeFundsActualPln ?? 0)),
      ]);
    const scale = Math.max(...values, 0) || 1;

    return months.map((month) => {
      const plan = Number(month.freeFundsPlanPln ?? 0);
      const actual = Number(month.freeFundsActualPln ?? 0);
      return {
        month: month.month,
        label: formatMonthShort(fromDateOnly(`${month.month}-01`)),
        current: month.month === now,
        empty: !month.hasDefinitions,
        incomplete: month.incomplete,
        deficit: plan < 0,
        planHeight: (Math.abs(plan) / scale) * 100,
        actualBottom: (Math.abs(actual) / scale) * 100,
        plan,
        actual,
      };
    });
  });

  protected readonly description = computed(() => {
    activeLocale();
    const planWord = translate('planning.freeFunds.plan');
    const actualWord = translate('planning.freeFunds.actual');
    const deficitWord = translate('planning.freeFunds.deficit');
    const incompleteWord = translate('planning.year.incomplete');
    const parts = this.columns().map((column) => {
      if (column.empty) {
        return `${column.label} —`;
      }

      const plan = `${planWord} ${formatMoney(column.plan, 'PLN')}${column.deficit ? ` ${deficitWord}` : ''}`;
      const actual = `${actualWord} ${formatMoney(column.actual, 'PLN')}`;
      const incomplete = column.incomplete ? `, ${incompleteWord}` : '';
      return `${column.label} ${plan}, ${actual}${incomplete}`;
    });

    return `${translate('planning.year.chart.label', { year: this.year() })}: ${parts.join('; ')}`;
  });
}
