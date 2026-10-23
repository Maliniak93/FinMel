import { Component, computed, linkedSignal, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioAssetGroups } from '../../api/portfolio';
import {
  getApiReportingDashboard,
  getApiReportingNetWorthHistory,
  type AssetClass,
} from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { activeLocale } from '../../core/i18n/language';
import { assetClassColor, groupColor } from '../../shared/colors';
import { formatAmount, formatDate, formatMoney, formatPercent } from '../../shared/format';
import { PieChart, type PieChartSegment } from '../../shared/pie-chart/pie-chart';
import { assetClassLabel } from '../assets/asset-class';
import { GROUP_KIND } from '../groups/group-kind';
import { CashCard } from './cash-card/cash-card';
import { AccountsCard } from './accounts-card/accounts-card';
import { MonthCard } from './month-card/month-card';
import { UpcomingCard } from './upcoming-card/upcoming-card';
import type { ChartRange } from './net-worth-chart/chart-scale';
import { NetWorthChart, type NetWorthChartGroup } from './net-worth-chart/net-worth-chart';

const UNGROUPED_KEY = 'ungrouped';
const MAX_BAR_PERCENT = 100;

interface GroupProgress {
  readonly percent: number;
  readonly bar: number;
}

interface GroupClassShare {
  readonly assetClass: number;
  readonly label: string;
  readonly currentPercent: number;
  readonly targetPercent: number;
}

interface GroupCushion {
  readonly valuePln: number;
  readonly targetPln: number;
  readonly missingPln: number;
}

interface GroupRow {
  readonly key: string;
  readonly label: string;
  readonly color: string;
  readonly valuePln: number;
  readonly percentage: number;
  readonly targetPln: number | null;
  readonly progress: GroupProgress | null;
  readonly classShares: readonly GroupClassShare[];
  readonly cushion: GroupCushion | null;
}

function toGrosze(amount: number | string): number {
  return Math.round(Number(amount) * 100);
}

@Component({
  selector: 'app-dashboard',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
    NetWorthChart,
    PieChart,
    AccountsCard,
    CashCard,
    MonthCard,
    UpcomingCard,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  protected readonly dashboardResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingDashboard({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly groupsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAssetGroups({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.groups.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly range = signal<ChartRange>('1Y');

  protected readonly assetClass = signal<AssetClass | null>(null);

  private readonly validGroupKeys = computed(() =>
    this.groupsResource.hasValue()
      ? new Set([...this.groupsResource.value().map((group) => group.id), UNGROUPED_KEY])
      : null,
  );

  protected readonly group = linkedSignal<Set<string> | null, string | null>({
    source: this.validGroupKeys,
    computation: (keys, previous) => {
      const current = previous?.value ?? null;
      return keys === null || current === null || keys.has(current) ? current : null;
    },
  });

  protected readonly historyResource = resource({
    params: () => ({
      range: this.range(),
      assetClass: this.assetClass(),
      group: this.group(),
    }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiReportingNetWorthHistory({
        query: {
          range: params.range,
          ...(params.assetClass === null ? {} : { assetClass: params.assetClass }),
          ...(params.group === null
            ? {}
            : params.group === UNGROUPED_KEY
              ? { ungrouped: true }
              : { groupId: params.group }),
        },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('netWorthChart.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly formatMoney = formatMoney;
  protected readonly formatAmount = formatAmount;
  protected readonly formatPercent = formatPercent;
  protected readonly formatDate = formatDate;

  protected readonly historyPoints = computed(() =>
    this.historyResource.hasValue() ? this.historyResource.value().points : [],
  );

  protected readonly change = computed(() => {
    if (!this.historyResource.hasValue()) {
      return null;
    }
    const history = this.historyResource.value();
    if (history.changePln === null || history.changePln === undefined) {
      return null;
    }
    const amount = Number(history.changePln);
    const percent =
      history.changePercent === null || history.changePercent === undefined
        ? null
        : Number(history.changePercent);
    return {
      amount,
      percent,
      sign: amount > 0 ? '+' : '',
      direction: amount > 0 ? 'positive' : amount < 0 ? 'negative' : 'neutral',
    };
  });

  protected readonly pieSegments = computed<PieChartSegment[]>(() => {
    const dashboard = this.dashboardResource.value();
    if (!dashboard) {
      return [];
    }
    return dashboard.byAssetClass.map((entry) => ({
      key: String(entry.assetClass),
      label: assetClassLabel(entry.assetClass),
      percentage: Number(entry.percentage),
      color: assetClassColor(entry.assetClass),
    }));
  });

  protected readonly classes = computed<readonly AssetClass[]>(() =>
    this.dashboardResource.hasValue()
      ? this.dashboardResource.value().byAssetClass.map((entry) => entry.assetClass)
      : [],
  );

  protected readonly selectedKey = computed(() => {
    const selected = this.assetClass();
    return selected === null ? null : String(selected);
  });

  protected readonly assetClassColor = assetClassColor;

  private readonly ungroupedLabel = computed(() => {
    activeLocale();
    return translate('dashboard.groups.ungrouped');
  });

  protected readonly groupRows = computed<GroupRow[]>(() => {
    if (!this.dashboardResource.hasValue() || !this.groupsResource.hasValue()) {
      return [];
    }
    const groups = this.groupsResource.value();
    if (groups.length === 0) {
      return [];
    }
    const entries = this.dashboardResource.value().byGroup;
    const known = new Set(groups.map((group) => group.id));
    const rows = groups.map<GroupRow>((group) => {
      const entry = entries.find((candidate) => candidate.groupId === group.id);
      const valuePln = entry ? Number(entry.valuePln) : 0;
      const targetPln =
        group.targetPln === null || group.targetPln === undefined ? null : Number(group.targetPln);
      const percent =
        targetPln === null || targetPln <= 0
          ? null
          : Math.round((toGrosze(valuePln) * 100) / toGrosze(targetPln));
      const groupGrosze = toGrosze(valuePln);
      const held = new Map(
        (entry?.byAssetClass ?? []).map((value) => [
          Number(value.assetClass),
          toGrosze(value.valuePln),
        ]),
      );
      const share = (assetClass: number, targetPercent: number): GroupClassShare => ({
        assetClass,
        label: assetClassLabel(assetClass),
        currentPercent:
          groupGrosze > 0 ? Math.round(((held.get(assetClass) ?? 0) * 1000) / groupGrosze) / 10 : 0,
        targetPercent,
      });
      const allocation = group.classAllocation.map((item) => ({
        assetClass: Number(item.assetClass),
        percent: Number(item.percent),
      }));
      const classShares =
        allocation.length === 0
          ? []
          : [
              ...allocation.map((item) => share(item.assetClass, item.percent)),
              ...[...held]
                .filter(
                  ([assetClass, grosze]) =>
                    grosze > 0 && !allocation.some((item) => item.assetClass === assetClass),
                )
                .map(([assetClass]) => share(assetClass, 0)),
            ];
      return {
        key: group.id,
        label: group.name,
        color: groupColor(group.color),
        valuePln,
        percentage: entry ? Number(entry.percentage) : 0,
        targetPln,
        progress:
          percent === null
            ? null
            : { percent, bar: Math.min(MAX_BAR_PERCENT, Math.max(0, percent)) },
        classShares,
        cushion:
          Number(group.kind) === GROUP_KIND.Cushion && targetPln !== null && targetPln > 0
            ? {
                valuePln,
                targetPln,
                missingPln: Math.max(0, toGrosze(targetPln) - groupGrosze) / 100,
              }
            : null,
      };
    });
    const ungrouped = entries.filter((entry) => !entry.groupId || !known.has(entry.groupId));
    if (ungrouped.length > 0) {
      const grosze = ungrouped.reduce((sum, entry) => sum + toGrosze(entry.valuePln), 0);
      const hundredths = ungrouped.reduce((sum, entry) => sum + toGrosze(entry.percentage), 0);
      rows.push({
        key: UNGROUPED_KEY,
        label: this.ungroupedLabel(),
        color: groupColor(null),
        valuePln: grosze / 100,
        percentage: hundredths / 100,
        targetPln: null,
        progress: null,
        classShares: [],
        cushion: null,
      });
    }
    return rows;
  });

  protected readonly groupSegments = computed<PieChartSegment[]>(() =>
    this.groupRows()
      .filter((row) => row.valuePln > 0)
      .map((row) => ({
        key: row.key,
        label: row.label,
        percentage: row.percentage,
        color: row.color,
      })),
  );

  protected readonly groupOptions = computed<NetWorthChartGroup[]>(() => {
    if (!this.groupsResource.hasValue() || this.groupsResource.value().length === 0) {
      return [];
    }
    return [
      ...this.groupsResource.value().map((group) => ({ key: group.id, label: group.name })),
      { key: UNGROUPED_KEY, label: this.ungroupedLabel() },
    ];
  });

  protected readonly selectedGroupLabel = computed(
    () => this.groupOptions().find((option) => option.key === this.group())?.label ?? null,
  );

  protected selectClass(assetClass: AssetClass): void {
    this.assetClass.update((current) => (current === assetClass ? null : assetClass));
  }

  protected onSegmentClick(key: string): void {
    this.selectClass(Number(key));
  }

  protected selectGroup(key: string): void {
    this.group.update((current) => (current === key ? null : key));
  }
}
