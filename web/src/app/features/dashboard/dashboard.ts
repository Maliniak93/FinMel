import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';

import { getApiPortfolioWealthSummary } from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { PieChart, type PieChartSegment } from '../../shared/pie-chart/pie-chart';
import { assetClassLabel } from '../assets/asset-class';

// Colors keyed 1:1 to AssetClass's declaration order (asset-class.ts) so a given class keeps the
// same color across reloads instead of shifting with whichever classes happen to be present.
const ASSET_CLASS_COLORS: readonly string[] = [
  '#4C6EF5', // Cash
  '#22B8CF', // Deposit
  '#12B886', // Stock
  '#82C91E', // Etf
  '#FAB005', // Bond
  '#FA5252', // Crypto
  '#F76707', // PreciousMetal
  '#7048E8', // RealEstate
  '#868E96', // Other
];

@Component({
  selector: 'app-dashboard',
  imports: [MatButtonModule, MatProgressSpinnerModule, RouterLink, PieChart],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  // Phase 2: replaced by Reporting (T2.12/T2.13) — this reads Portfolio's temporary
  // GetWealthSummary aggregate instead of a dedicated read model.
  protected readonly summaryResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioWealthSummary({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? 'Failed to load wealth summary.',
        );
      }
      return result.data;
    },
  });

  protected readonly assetClassLabel = assetClassLabel;

  protected readonly pieSegments = computed<PieChartSegment[]>(() => {
    const summary = this.summaryResource.value();
    if (!summary) {
      return [];
    }
    return summary.byAssetClass.map((entry) => ({
      label: assetClassLabel(entry.assetClass),
      percentage: Number(entry.percentage),
      color: this.assetClassColor(entry.assetClass),
    }));
  });

  protected assetClassColor(assetClass: number): string {
    return ASSET_CLASS_COLORS[Number(assetClass) % ASSET_CLASS_COLORS.length];
  }

  protected isZero(amount: number | string): boolean {
    return Number(amount) === 0;
  }

  protected formatMoney(amount: number | string): string {
    try {
      return new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN' }).format(
        Number(amount),
      );
    } catch {
      return `${amount} PLN`;
    }
  }
}
