import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiReportingDashboard } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatDate, formatMoney } from '../../shared/format';
import { PieChart, type PieChartSegment } from '../../shared/pie-chart/pie-chart';
import { assetClassLabel } from '../assets/asset-class';
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

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;

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
