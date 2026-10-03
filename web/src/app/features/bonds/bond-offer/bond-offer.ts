import { Component, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiMarketdataBondSeries, type BondSeriesListItem } from '../../../api/marketdata';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatMoney, formatPercent } from '../../../shared/format';
import { bondTypeInfo, type BondTypeInfo } from '../bond-types';

interface FollowingRateLabel {
  key: string;
  params: Record<string, string>;
}

@Component({
  selector: 'app-bond-offer',
  imports: [MatButtonModule, MatProgressSpinnerModule, MatTableModule, TranslocoPipe],
  templateUrl: './bond-offer.html',
  styleUrl: './bond-offer.scss',
})
export class BondOffer {
  protected readonly displayedColumns = [
    'code',
    'type',
    'firstRate',
    'followingRate',
    'swapPrice',
    'earlyRedemptionFee',
  ];

  // No onSaleOn: the server picks today's offer in Europe/Warsaw.
  protected readonly offerResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiMarketdataBondSeries({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.offer.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly formatMoney = formatMoney;

  protected typeInfo(series: BondSeriesListItem): BondTypeInfo | undefined {
    return bondTypeInfo(series.type);
  }

  protected rate(value: number | string | null | undefined): string {
    return value === null || value === undefined ? '—' : formatPercent(value);
  }

  protected followingRate(series: BondSeriesListItem): FollowingRateLabel {
    const margin = this.rate(series.marginPercent);
    switch (this.typeInfo(series)?.followingRate) {
      case 'fixed':
        return { key: 'bonds.offer.following.fixed', params: {} };
      case 'nbpReference':
        return { key: 'bonds.offer.following.nbpReference', params: { margin } };
      default:
        return { key: 'bonds.offer.following.inflation', params: { margin } };
    }
  }
}
