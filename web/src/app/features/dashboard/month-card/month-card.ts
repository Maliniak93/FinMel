import { Component, computed, input, resource } from '@angular/core';
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
import {
  type CascadeAmounts,
  type CascadeLabels,
  type CascadeSplitRow,
  FreeFundsCascade,
} from '../../../shared/free-funds-cascade/free-funds-cascade';
import { formatMoney, formatMonth, formatPercent } from '../../../shared/format';
import { SectionError } from '../../../shared/section-error/section-error';
import { currentWarsawMonth } from '../../planning/recurrence';

function toGrosze(amount: number | string): number {
  return Math.round(Number(amount) * 100);
}

@Component({
  selector: 'app-month-card',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
    FreeFundsCascade,
    RouterLink,
    SectionError,
    TranslocoPipe,
  ],
  templateUrl: './month-card.html',
  styleUrl: './month-card.scss',
})
export class MonthCard {
  readonly version = input(0);

  protected readonly monthResource = resource({
    params: () => this.version(),
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

  protected readonly contributions = computed(
    () => this.month()?.expenses.groupContributions ?? [],
  );

  protected readonly groups = computed(() =>
    this.groupsResource.hasValue() ? this.groupsResource.value() : [],
  );

  protected readonly cascadeLabels = computed<CascadeLabels>(() => {
    activeLocale();
    return {
      incomes: translate('dashboard.monthCard.incomes'),
      expenses: translate('dashboard.monthCard.expenses'),
      contributions: translate('dashboard.monthCard.contributions'),
      free: translate('dashboard.monthCard.free'),
      deficit: translate('dashboard.monthCard.deficit'),
    };
  });

  protected readonly testIds = {
    'plan-incomes': 'month-card-incomes',
    'plan-expenses': 'month-card-expenses',
    'plan-contributions': 'month-card-contributions',
    'plan-free': 'month-card-free',
    'free-deficit': 'month-card-free-deficit',
    'split-row': 'month-card-split-row',
  };

  protected readonly expensesPln = computed(
    () => (toGrosze(this.month()?.expenses.costPln ?? 0) - this.contributionsGrosze()) / 100,
  );

  protected readonly freePln = computed(() => Number(this.month()?.freeFunds.planPln ?? 0));

  protected readonly actualPln = computed(() => Number(this.month()?.freeFunds.actualPln ?? 0));

  protected readonly deficitPln = computed(() => Number(this.month()?.split?.deficitPln ?? 0));

  protected readonly isDeficit = computed(() => this.deficitPln() > 0 || this.freePln() < 0);

  protected readonly amounts = computed<CascadeAmounts>(() => ({
    incomes: this.incomesPln(),
    expenses: this.expensesPln(),
    contributions: this.contributionsPln(),
    free: this.freePln(),
  }));

  protected readonly splitRows = computed<CascadeSplitRow[]>(() => {
    activeLocale();
    const split = this.month()?.split;
    if (!split || split.valuationMissing || this.deficitPln() > 0) {
      return [];
    }
    const target = (key: string, name: string, amountPln: number): CascadeSplitRow => ({
      key,
      label: translate('dashboard.monthCard.splitTarget', { name }),
      amountPln,
    });
    const rows: CascadeSplitRow[] = [];
    if (split.cushion) {
      rows.push(
        target(split.cushion.groupId, split.cushion.name, Number(split.cushion.allocationPln)),
      );
    }
    for (const group of split.groups) {
      rows.push({
        key: group.groupId,
        label: translate('dashboard.monthCard.splitGroup', {
          name: group.name,
          share: formatPercent(Number(group.sharePercent)),
        }),
        amountPln: Number(group.allocationPln),
      });
    }
    if (Number(split.unassignedPln) > 0) {
      rows.push(
        target('ungrouped', translate('dashboard.groups.ungrouped'), Number(split.unassignedPln)),
      );
    }
    return rows;
  });

  protected readonly settled = computed(() => {
    const incomes = this.month()?.incomes;
    const occurrences = [
      ...(incomes?.occurrences ?? []),
      ...(incomes?.oneOffs ?? []).filter((o) => o.inMonth),
    ];
    return {
      done: occurrences.filter((o) => o.status === 'Settled' || o.status === 'Skipped').length,
      total: occurrences.length,
    };
  });

  protected readonly formatMoney = formatMoney;
}
