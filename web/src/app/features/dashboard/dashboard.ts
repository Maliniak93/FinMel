import { Component, computed, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiReportingDashboard,
  getApiReportingNetWorthHistory,
  type AssetClass,
} from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatDate, formatMoney, formatPercent } from '../../shared/format';
import { PieChart, type PieChartSegment } from '../../shared/pie-chart/pie-chart';
import { assetClassLabel } from '../assets/asset-class';
import { assetClassColor } from './asset-class-color';
import { CashCard } from './cash-card/cash-card';
import { PortfoliosCard } from './portfolios-card/portfolios-card';
import { UpcomingCard } from './upcoming-card/upcoming-card';
import type { ChartRange } from './net-worth-chart/chart-scale';
import { NetWorthChart } from './net-worth-chart/net-worth-chart';

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
    PortfoliosCard,
    CashCard,
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

  protected readonly range = signal<ChartRange>('1Y');

  protected readonly assetClass = signal<AssetClass | null>(null);

  protected readonly historyResource = resource({
    params: () => ({ range: this.range(), assetClass: this.assetClass() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiReportingNetWorthHistory({
        query:
          params.assetClass === null
            ? { range: params.range }
            : { range: params.range, assetClass: params.assetClass },
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

  protected selectClass(assetClass: AssetClass): void {
    this.assetClass.update((current) => (current === assetClass ? null : assetClass));
  }

  protected onSegmentClick(key: string): void {
    this.selectClass(Number(key));
  }
}
