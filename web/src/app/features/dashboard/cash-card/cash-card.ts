import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioCashPlaces } from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatMoney } from '../../../shared/format';

@Component({
  selector: 'app-cash-card',
  imports: [MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink, TranslocoPipe],
  templateUrl: './cash-card.html',
  styleUrl: './cash-card.scss',
})
export class CashCard {
  protected readonly cashResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioCashPlaces({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.cash.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly formatMoney = formatMoney;

  protected readonly plnByCurrency = computed(() => {
    const grosze = new Map<string, number | null>();
    if (!this.cashResource.hasValue()) {
      return grosze;
    }
    for (const place of this.cashResource.value().places) {
      if (place.isArchived) {
        continue;
      }
      for (const balance of place.balances) {
        const current = grosze.get(balance.currency);
        if (current === null) {
          continue;
        }
        const next =
          balance.valuePln === null || balance.valuePln === undefined
            ? null
            : (current ?? 0) + Math.round(Number(balance.valuePln) * 100);
        grosze.set(balance.currency, next);
      }
    }
    return new Map([...grosze].map(([currency, value]) => [currency, value && value / 100]));
  });
}
