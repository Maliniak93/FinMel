import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioAccounts } from '../../../api/portfolio';
import { getApiReportingDashboard, type DashboardAccountValue } from '../../../api/reporting';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatMoney, formatPercent } from '../../../shared/format';
import { accountHoldingsPath } from '../../accounts/account-type';

interface AccountRow {
  id: string | null;
  name: string | null;
  path: string | null;
  entry: DashboardAccountValue;
}

@Component({
  selector: 'app-accounts-card',
  imports: [MatButtonModule, MatCardModule, MatProgressSpinnerModule, RouterLink, TranslocoPipe],
  templateUrl: './accounts-card.html',
  styleUrl: './accounts-card.scss',
})
export class AccountsCard {
  private readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('dashboard.accounts.loadFailed'),
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
          readProblemDetails(result.error).detail ?? translate('dashboard.accounts.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly loadError = computed(
    () => this.accountsResource.error() ?? this.valuesResource.error(),
  );

  protected readonly loading = computed(
    () => this.accountsResource.isLoading() || this.valuesResource.isLoading(),
  );

  protected readonly rows = computed<AccountRow[]>(() => {
    if (!this.accountsResource.hasValue() || !this.valuesResource.hasValue()) {
      return [];
    }
    const accounts = new Map(this.accountsResource.value().map((a) => [a.id, a]));
    return this.valuesResource
      .value()
      .byAccount.flatMap((entry): AccountRow[] => {
        if (!entry.accountId) {
          return [{ id: null, name: null, path: null, entry }];
        }
        const account = accounts.get(entry.accountId);
        return account && !account.isArchived
          ? [{ id: account.id, name: account.name, path: accountHoldingsPath(account.type), entry }]
          : [];
      })
      .sort((a, b) => Number(b.entry.valuePln) - Number(a.entry.valuePln));
  });

  protected readonly ready = computed(
    () => this.accountsResource.hasValue() && this.valuesResource.hasValue(),
  );

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;

  protected reload(): void {
    this.accountsResource.reload();
    this.valuesResource.reload();
  }
}
