import { Component, computed, input, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioAccounts } from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatMoney } from '../../../shared/format';
import { TAX_WRAPPER } from '../../accounts/account-type';

interface LimitRow {
  id: string;
  name: string;
  exceeded: boolean;
  amountPln: number;
}

@Component({
  selector: 'app-contribution-limits-card',
  imports: [MatButtonModule, MatCardModule, RouterLink, TranslocoPipe],
  templateUrl: './contribution-limits-card.html',
  styleUrl: './contribution-limits-card.scss',
})
export class ContributionLimitsCard {
  readonly version = input(0);

  private readonly accountsResource = resource({
    params: () => this.version(),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('dashboard.contributionLimits.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly loadError = computed(() => this.accountsResource.error());

  protected readonly rows = computed<LimitRow[]>(() => {
    if (!this.accountsResource.hasValue()) {
      return [];
    }
    return this.accountsResource.value().flatMap((account): LimitRow[] => {
      const wrapper = Number(account.wrapper);
      const contribution = account.contribution;
      if (
        account.isArchived ||
        (wrapper !== TAX_WRAPPER.Ike && wrapper !== TAX_WRAPPER.Ikze) ||
        contribution?.limitPln == null
      ) {
        return [];
      }
      const remainingGrosze =
        Math.round(Number(contribution.limitPln) * 100) -
        Math.round(Number(contribution.contributedPln) * 100);
      return [
        {
          id: account.id,
          name: account.name,
          exceeded: remainingGrosze < 0,
          amountPln: Math.abs(remainingGrosze) / 100,
        },
      ];
    });
  });

  protected readonly formatMoney = formatMoney;

  protected reload(): void {
    this.accountsResource.reload();
  }
}
