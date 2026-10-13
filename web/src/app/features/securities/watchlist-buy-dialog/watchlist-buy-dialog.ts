import { Component, computed, inject, resource } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiPortfolioAccounts, type WatchlistItemResponse } from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatQuantity } from '../../../shared/format';
import { ACCOUNT_TYPE } from '../../accounts/account-type';

export interface WatchlistBuyDialogData {
  item: WatchlistItemResponse;
}

export interface WatchlistBuyTarget {
  accountId: string;
  assetId: string | null;
}

interface BuyOption {
  target: WatchlistBuyTarget;
  key: string;
  params: Record<string, string>;
}

@Component({
  selector: 'app-watchlist-buy-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    TranslocoPipe,
  ],
  templateUrl: './watchlist-buy-dialog.html',
  styleUrl: './watchlist-buy-dialog.scss',
})
export class WatchlistBuyDialog {
  private readonly dialogRef = inject(MatDialogRef<WatchlistBuyDialog>);
  private readonly data = inject<WatchlistBuyDialogData>(MAT_DIALOG_DATA);

  protected readonly choice = new FormControl<WatchlistBuyTarget | null>(null);

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('assets.form.accountsLoadFailed'),
        );
      }
      return (result.data ?? []).filter(
        (account) => !account.isArchived && Number(account.type) === ACCOUNT_TYPE.Brokerage,
      );
    },
  });

  protected readonly options = computed<BuyOption[]>(() => {
    if (!this.accountsResource.hasValue()) {
      return [];
    }
    const holdings = this.data.item.holdings;
    const held = new Set(holdings.map((holding) => holding.accountId));
    return [
      ...holdings.map((holding) => ({
        target: { accountId: holding.accountId, assetId: holding.assetId },
        key: 'securities.watchlist.buyHolding',
        params: {
          account: holding.accountName,
          asset: holding.assetName,
          quantity: formatQuantity(holding.quantity),
        },
      })),
      ...this.accountsResource
        .value()
        .filter((account) => !held.has(account.id))
        .map((account) => ({
          target: { accountId: account.id, assetId: null },
          key: 'securities.watchlist.buyNewPosition',
          params: { account: account.name },
        })),
    ];
  });

  protected confirm(): void {
    const target = this.choice.value;
    if (target) {
      this.dialogRef.close(target);
    }
  }

  protected cancel(): void {
    this.dialogRef.close(null);
  }
}
