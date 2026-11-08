import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, linkedSignal, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
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
import { PlanningState } from '../../core/planning/planning-state';
import { assetClassColor, groupColor } from '../../shared/colors';
import { changeDirection } from '../../shared/change-direction';
import { formatDate, formatMoney, formatPercent, formatSignedMoney } from '../../shared/format';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { PageHeader } from '../../shared/page-header/page-header';
import { ALLOCATION_DEVIATION_PP } from '../../shared/allocation-bar/allocation-bar';
import { GoalProgress } from '../../shared/goal-progress/goal-progress';
import { SectionError } from '../../shared/section-error/section-error';
import { SplitBar, type SplitBarSegment } from '../../shared/split-bar/split-bar';
import { assetClassLabel, assetClassPage } from '../assets/asset-class';
import { GROUP_KIND } from '../groups/group-kind';
import { CashCard } from './cash-card/cash-card';
import { AccountsCard } from './accounts-card/accounts-card';
import { ContributionLimitsCard } from './contribution-limits-card/contribution-limits-card';
import { MonthCard } from './month-card/month-card';
import { TodoCard } from './todo-card/todo-card';
import { UpcomingCard } from './upcoming-card/upcoming-card';
import type { ChartRange } from './net-worth-chart/chart-scale';
import { NetWorthChart, type NetWorthChartGroup } from './net-worth-chart/net-worth-chart';
import {
  RangeDialog,
  type RangeDialogData,
  type RangeDialogResult,
} from './range-dialog/range-dialog';
import { customRangeLabel, type CustomRange } from './range-label';

const OVERDUE_KINDS: ReadonlySet<string> = new Set(['OverdueIncome', 'OverdueExpense']);
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

interface StructureRow {
  readonly assetClass: AssetClass;
  readonly valuePln: number;
  readonly percentage: number;
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
  readonly isCushionKind: boolean;
  readonly isUngrouped: boolean;
}

function toGrosze(amount: number | string): number {
  return Math.round(Number(amount) * 100);
}

@Component({
  selector: 'app-dashboard',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    NgTemplateOutlet,
    RouterLink,
    TranslocoPipe,
    EmptyState,
    PageHeader,
    GoalProgress,
    NetWorthChart,
    SectionError,
    SplitBar,
    AccountsCard,
    ContributionLimitsCard,
    CashCard,
    MonthCard,
    TodoCard,
    UpcomingCard,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly planningState = inject(PlanningState);

  protected readonly hasOverdue = computed(() =>
    this.planningState.todo().some((item) => OVERDUE_KINDS.has(item.kind)),
  );

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

  private readonly dialog = inject(MatDialog);

  protected readonly balancesVersion = signal(0);

  protected reloadAfterSettlement(): void {
    this.dashboardResource.reload();
    this.historyResource.reload();
    this.balancesVersion.update((version) => version + 1);
  }

  protected readonly range = signal<ChartRange>('1Y');

  private readonly customRange = signal<CustomRange | null>(null);

  protected readonly rangeLabel = computed(() => {
    activeLocale();
    const custom = this.customRange();
    return this.range() === 'CUSTOM' && custom !== null ? customRangeLabel(custom) : this.range();
  });

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
      custom: this.range() === 'CUSTOM' ? this.customRange() : null,
      assetClass: this.assetClass(),
      group: this.group(),
    }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiReportingNetWorthHistory({
        query: {
          ...(params.custom === null
            ? { range: params.range }
            : { from: params.custom.from, to: params.custom.to }),
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

  protected readonly historyBounds = linkedSignal<
    { first: string; last: string } | null,
    { first: string; last: string } | null
  >({
    source: () => {
      const history = this.historyResource.hasValue() ? this.historyResource.value() : null;
      return history?.firstDate && history.lastDate
        ? { first: history.firstDate, last: history.lastDate }
        : null;
    },
    computation: (bounds, previous) => bounds ?? previous?.value ?? null,
  });

  protected openRangeDialog(): void {
    const bounds = this.historyBounds();
    if (bounds === null) {
      return;
    }
    this.dialog
      .open<RangeDialog, RangeDialogData, RangeDialogResult>(RangeDialog, {
        data: { firstDate: bounds.first, lastDate: bounds.last, current: this.customRange() },
        width: '420px',
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.customRange.set(result);
          this.range.set('CUSTOM');
        }
      });
  }

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly formatDate = formatDate;
  protected readonly formatSignedMoney = formatSignedMoney;
  protected readonly assetClassPage = assetClassPage;

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
    const direction = changeDirection(amount, percent, 4);
    return {
      amount,
      percent,
      sign: direction === 'neutral' || (percent ?? 0) <= 0 ? '' : '+',
      direction,
    };
  });

  protected readonly structureRows = computed<StructureRow[]>(() =>
    this.dashboardResource.hasValue()
      ? this.dashboardResource
          .value()
          .byAssetClass.filter((entry) => Number(entry.valuePln) > 0)
          .map((entry) => ({
            assetClass: entry.assetClass,
            valuePln: Number(entry.valuePln),
            percentage: Number(entry.percentage),
          }))
          .sort((a, b) => b.valuePln - a.valuePln)
      : [],
  );

  protected readonly structureSegments = computed<SplitBarSegment[]>(() => {
    activeLocale();
    return this.structureRows().map((row) => ({
      key: String(row.assetClass),
      label: translate(assetClassLabel(row.assetClass)),
      value: row.valuePln,
      color: assetClassColor(row.assetClass),
    }));
  });

  protected readonly staleCount = computed(() =>
    this.dashboardResource.hasValue()
      ? this.dashboardResource.value().byAsset.filter((line) => line.isStale).length
      : 0,
  );

  protected readonly classes = computed<readonly AssetClass[]>(() =>
    this.dashboardResource.hasValue()
      ? this.dashboardResource.value().byAssetClass.map((entry) => entry.assetClass)
      : [],
  );

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
            ].filter(
              (item) =>
                Math.abs(
                  Math.round(item.currentPercent * 10) - Math.round(item.targetPercent * 10),
                ) >=
                ALLOCATION_DEVIATION_PP * 10,
            );
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
        isCushionKind: Number(group.kind) === GROUP_KIND.Cushion,
        isUngrouped: false,
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
        isCushionKind: false,
        isUngrouped: true,
      });
    }
    return rows;
  });

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
}
