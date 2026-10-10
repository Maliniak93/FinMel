import { Component, computed, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiReportingDashboard, getApiReportingNetWorthHistory } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatDate, formatMoney, formatPercent } from '../../shared/format';
import { PieChart, type PieChartSegment } from '../../shared/pie-chart/pie-chart';
import { assetClassLabel } from '../assets/asset-class';
import type { ChartRange } from './net-worth-chart/chart-scale';
import { NetWorthChart } from './net-worth-chart/net-worth-chart';

// Indexed by AssetClass, so a class keeps its colour whichever classes are present.
const ASSET_CLASS_COLORS: readonly string[] = [
  '#4C6EF5',
  '#22B8CF',
  '#12B886',
  '#82C91E',
  '#FAB005',
  '#FA5252',
  '#F76707',
  '#7048E8',
  '#868E96',
  '#E64980',
];

@Component({
  selector: 'app-dashboard',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
    NetWorthChart,
    PieChart,
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

  protected readonly range = signal<ChartRange>('1Y');

  protected readonly historyResource = resource({
    params: () => ({ range: this.range() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiReportingNetWorthHistory({
        query: { range: params.range },
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
      label: assetClassLabel(entry.assetClass),
      percentage: Number(entry.percentage),
      color: this.assetClassColor(entry.assetClass),
    }));
  });

  protected assetClassColor(assetClass: number): string {
    return ASSET_CLASS_COLORS[Number(assetClass) % ASSET_CLASS_COLORS.length];
  }
}
