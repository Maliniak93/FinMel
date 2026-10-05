import { Component, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioCashAccounts, type CashAccountResponse } from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatMoney } from '../../shared/format';

@Component({
  selector: 'app-cash',
  imports: [MatButtonModule, MatProgressSpinnerModule, MatTableModule, RouterLink, TranslocoPipe],
  templateUrl: './cash.html',
  styleUrl: './cash.scss',
})
export class Cash {
  protected readonly displayedColumns = ['name', 'portfolio', 'currency', 'balance'];

  protected readonly cashResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioCashAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('cash.loadFailed'));
      }
      return result.data ?? { accounts: [], totals: [] };
    },
  });

  protected readonly formatMoney = formatMoney;

  protected transactionsLink(account: CashAccountResponse): string[] {
    return ['/portfolios', account.portfolioId, 'assets', account.assetId, 'transactions'];
  }
}
