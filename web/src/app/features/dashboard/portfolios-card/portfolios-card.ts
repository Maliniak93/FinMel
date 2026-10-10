import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioPortfolios } from '../../../api/portfolio';
import { getApiReportingDashboard } from '../../../api/reporting';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatMoney, formatPercent } from '../../../shared/format';

@Component({
  selector: 'app-portfolios-card',
  imports: [MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink, TranslocoPipe],
  templateUrl: './portfolios-card.html',
  styleUrl: './portfolios-card.scss',
})
export class PortfoliosCard {
  private readonly portfoliosResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfolios({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.portfolios.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly valuesResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingDashboard({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.portfolios.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly loadError = computed(
    () => this.portfoliosResource.error() ?? this.valuesResource.error(),
  );

  protected readonly loading = computed(
    () => this.portfoliosResource.isLoading() || this.valuesResource.isLoading(),
  );

  protected readonly rows = computed(() => {
    if (!this.portfoliosResource.hasValue() || !this.valuesResource.hasValue()) {
      return [];
    }
    const names = new Map(this.portfoliosResource.value().map((p) => [p.id, p]));
    return this.valuesResource
      .value()
      .byPortfolio.flatMap((entry) => {
        const portfolio = names.get(entry.portfolioId);
        return portfolio && !portfolio.isArchived
          ? [{ id: portfolio.id, name: portfolio.name, entry }]
          : [];
      })
      .sort((a, b) => Number(b.entry.valuePln) - Number(a.entry.valuePln));
  });

  protected readonly ready = computed(
    () => this.portfoliosResource.hasValue() && this.valuesResource.hasValue(),
  );

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;

  protected reload(): void {
    this.portfoliosResource.reload();
    this.valuesResource.reload();
  }
}
