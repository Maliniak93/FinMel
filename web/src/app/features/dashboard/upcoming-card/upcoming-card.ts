import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioBonds,
  getApiPortfolioDeposits,
  getApiPortfolioSavingsAccounts,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { toDateOnly } from '../../../shared/date-only';
import { formatDate, formatMoney } from '../../../shared/format';
import { upcomingItems } from '../upcoming';

@Component({
  selector: 'app-upcoming-card',
  imports: [MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink, TranslocoPipe],
  templateUrl: './upcoming-card.html',
  styleUrl: './upcoming-card.scss',
})
export class UpcomingCard {
  private readonly depositsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioDeposits({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.upcoming.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly bondsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioBonds({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.upcoming.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly savingsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioSavingsAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.upcoming.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly loadError = computed(
    () =>
      this.depositsResource.error() ?? this.bondsResource.error() ?? this.savingsResource.error(),
  );

  protected readonly loading = computed(
    () =>
      this.depositsResource.isLoading() ||
      this.bondsResource.isLoading() ||
      this.savingsResource.isLoading(),
  );

  protected readonly ready = computed(
    () =>
      this.depositsResource.hasValue() &&
      this.bondsResource.hasValue() &&
      this.savingsResource.hasValue(),
  );

  protected readonly items = computed(() =>
    this.ready()
      ? upcomingItems(
          this.depositsResource.value() ?? [],
          this.bondsResource.value() ?? [],
          this.savingsResource.value() ?? [],
          toDateOnly(new Date()),
        )
      : [],
  );

  protected readonly formatDate = formatDate;
  protected readonly formatMoney = formatMoney;

  protected reload(): void {
    this.depositsResource.reload();
    this.bondsResource.reload();
    this.savingsResource.reload();
  }
}
