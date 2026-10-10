import { Component, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioCashAccounts } from '../../../api/portfolio';
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
      const result = await getApiPortfolioCashAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.cash.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly formatMoney = formatMoney;
}
