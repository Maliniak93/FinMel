import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioAssetGroups,
  getApiPortfolioPlanningMonthsByMonth,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { activeLocale } from '../../../core/i18n/language';
import { groupColor } from '../../../shared/colors';
import { formatMoney, formatMonth, formatPercent } from '../../../shared/format';
import { SectionError } from '../../../shared/section-error/section-error';
import { SplitBar, type SplitBarSegment } from '../../../shared/split-bar/split-bar';
import { currentWarsawMonth } from '../../planning/recurrence';

const FULL_PERCENT = 100;

interface SplitRow {
  key: string;
  label: string;
  share: string | null;
  amountPln: number;
}

interface CascadeBar {
  percent: number;
}

function toGrosze(amount: number | string): number {
  return Math.round(Number(amount) * 100);
}

@Component({
  selector: 'app-month-card',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
    RouterLink,
    SectionError,
    SplitBar,
    TranslocoPipe,
  ],
  templateUrl: './month-card.html',
  styleUrl: './month-card.scss',
})
export class MonthCard {
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

  private readonly groupsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAssetGroups({ signal: abortSignal });
      return result.error ? [] : (result.data ?? []);
    },
  });

  protected readonly month = computed(() =>
    this.monthResource.hasValue() ? this.monthResource.value() : null,
  );

  protected readonly monthLabel = computed(() => {
    activeLocale();
    return formatMonth(`${currentWarsawMonth()}-01`);
  });

  protected readonly hasDefinitions = computed(() => {
    const month = this.month();
    return (
      !!month && (month.incomes.definitions.length > 0 || month.expenses.definitions.length > 0)
    );
  });

  protected readonly incomesPln = computed(() =>
    Number(this.month()?.incomes.balance.planPln ?? 0),
  );

  private readonly contributionsGrosze = computed(() =>
    (this.month()?.expenses.groupContributions ?? []).reduce(
      (sum, contribution) => sum + toGrosze(contribution.amountPln),
      0,
    ),
  );

  protected readonly contributionsPln = computed(() => this.contributionsGrosze() / 100);

  protected readonly expensesPln = computed(
    () => (toGrosze(this.month()?.expenses.costPln ?? 0) - this.contributionsGrosze()) / 100,
  );

  protected readonly freePln = computed(() => Number(this.month()?.freeFunds.planPln ?? 0));

  protected readonly forecastPln = computed(() => Number(this.month()?.freeFunds.forecastPln ?? 0));

  protected readonly deficitPln = computed(() => Number(this.month()?.split?.deficitPln ?? 0));

  protected readonly isDeficit = computed(() => this.deficitPln() > 0 || this.freePln() < 0);

  protected readonly contributionSegments = computed<SplitBarSegment[]>(() => {
    const groups = this.groupsResource.hasValue() ? this.groupsResource.value() : [];
    return (this.month()?.expenses.groupContributions ?? []).map((contribution) => ({
      key: contribution.groupId,
      label: contribution.name,
      value: Number(contribution.amountPln),
      color: groupColor(groups.find((group) => group.id === contribution.groupId)?.color),
    }));
  });

  protected readonly axisPln = computed(() =>
    Math.max(
      this.incomesPln(),
      this.expensesPln(),
      this.contributionsPln(),
      Math.abs(this.freePln()),
    ),
  );

  protected readonly bars = computed(() => {
    const axis = this.axisPln();
    const of = (amount: number): CascadeBar => ({
      percent: axis > 0 ? Math.min(FULL_PERCENT, (Math.abs(amount) / axis) * FULL_PERCENT) : 0,
    });
    return {
      incomes: of(this.incomesPln()),
      expenses: of(this.expensesPln()),
      contributions: of(this.contributionsPln()),
      free: of(this.freePln()),
    };
  });

  protected readonly splitRows = computed<SplitRow[]>(() => {
    activeLocale();
    const split = this.month()?.split;
    if (!split || split.valuationMissing || this.deficitPln() > 0) {
      return [];
    }
    const rows: SplitRow[] = [];
    if (split.cushion) {
      rows.push({
        key: split.cushion.groupId,
        label: split.cushion.name,
        share: null,
        amountPln: Number(split.cushion.allocationPln),
      });
    }
    for (const group of split.groups) {
      rows.push({
        key: group.groupId,
        label: group.name,
        share: formatPercent(Number(group.sharePercent)),
        amountPln: Number(group.allocationPln),
      });
    }
    if (Number(split.unassignedPln) > 0) {
      rows.push({
        key: 'ungrouped',
        label: translate('dashboard.groups.ungrouped'),
        share: null,
        amountPln: Number(split.unassignedPln),
      });
    }
    return rows;
  });

  protected readonly settled = computed(() => {
    const occurrences = this.month()?.incomes.occurrences ?? [];
    return {
      done: occurrences.filter((o) => o.status === 'Settled' || o.status === 'Skipped').length,
      total: occurrences.length,
    };
  });

  protected readonly formatMoney = formatMoney;
}
