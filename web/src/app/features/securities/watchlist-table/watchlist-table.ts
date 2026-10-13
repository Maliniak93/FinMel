import { Component, inject, input, output, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioWatchlistById,
  getApiPortfolioWatchlist,
  type AssetClass,
  type WatchlistItemResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney, formatPercent, formatQuantity } from '../../../shared/format';
import {
  AssetFormDialog,
  type AssetFormDialogData,
} from '../../assets/asset-form/asset-form-dialog/asset-form-dialog';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from '../../transactions/transaction-form-dialog/transaction-form-dialog';
import { TRANSACTION_TYPE_BUY } from '../../transactions/transaction-type';
import { PRICE_UNAVAILABLE_REASON_LABELS } from '../securities-table/securities-table';
import {
  WatchlistBuyDialog,
  type WatchlistBuyDialogData,
  type WatchlistBuyTarget,
} from '../watchlist-buy-dialog/watchlist-buy-dialog';
import {
  WatchlistItemDialog,
  type WatchlistItemDialogData,
} from '../watchlist-item-dialog/watchlist-item-dialog';

@Component({
  selector: 'app-watchlist-table',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    TranslocoPipe,
  ],
  templateUrl: './watchlist-table.html',
  styleUrl: './watchlist-table.scss',
})
export class WatchlistTable {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly assetClass = input.required<AssetClass>();
  readonly changed = output<void>();

  protected readonly displayedColumns = [
    'instrument',
    'price',
    'dailyChange',
    'pricePln',
    'target',
    'held',
    'note',
    'actions',
  ];

  protected readonly watchlistResource = resource({
    params: () => ({ assetClass: this.assetClass() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioWatchlist({
        query: { assetClass: params.assetClass },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('securities.watchlist.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatPercent = formatPercent;
  protected readonly formatDate = formatDate;

  protected instrumentLabel(item: WatchlistItemResponse): string {
    return [item.ticker, item.exchange].filter(Boolean).join(' · ');
  }

  protected unavailableLabel(item: WatchlistItemResponse): string | null {
    const reason = item.priceUnavailableReason;
    return reason === null ? null : (PRICE_UNAVAILABLE_REASON_LABELS[reason] ?? null);
  }

  protected sign(value: number | string | null): 'positive' | 'negative' | '' {
    const amount = Number(value);
    return amount > 0 ? 'positive' : amount < 0 ? 'negative' : '';
  }

  protected isHeld(item: WatchlistItemResponse): boolean {
    return Number(item.heldQuantity) > 0;
  }

  protected heldTooltip(item: WatchlistItemResponse): string {
    return item.holdings
      .map((holding) => `${holding.accountName} · ${formatQuantity(holding.quantity)}`)
      .join(', ');
  }

  protected openAddDialog(): void {
    const data: WatchlistItemDialogData = { assetClass: this.assetClass() };
    this.reloadWhenDone(this.dialog.open(WatchlistItemDialog, { width: '480px', data }));
  }

  protected openEditDialog(item: WatchlistItemResponse): void {
    const data: WatchlistItemDialogData = { assetClass: this.assetClass(), item };
    this.reloadWhenDone(this.dialog.open(WatchlistItemDialog, { width: '480px', data }));
  }

  protected async remove(item: WatchlistItemResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('securities.watchlist.remove.title', { name: item.ticker }),
            message: translate('securities.watchlist.remove.message'),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioWatchlistById({ path: { id: item.id } });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('securities.watchlist.remove.failed'),
        translate('common.dismiss'),
      );
      return;
    }
    this.watchlistResource.reload();
  }

  protected async buy(item: WatchlistItemResponse): Promise<void> {
    const pickData: WatchlistBuyDialogData = { item };
    const target: WatchlistBuyTarget | null | undefined = await firstValueFrom(
      this.dialog.open(WatchlistBuyDialog, { width: '480px', data: pickData }).afterClosed(),
    );
    if (!target) {
      return;
    }

    let assetId = target.assetId;
    let saved = false;
    if (!assetId) {
      const assetData: AssetFormDialogData = {
        assetClass: this.assetClass(),
        accountId: target.accountId,
        instrumentId: item.instrumentId,
      };
      const created: unknown = await firstValueFrom(
        this.dialog.open(AssetFormDialog, { width: '560px', data: assetData }).afterClosed(),
      );
      if (typeof created !== 'string') {
        return;
      }
      assetId = created;
      saved = true;
    }

    const transactionData: TransactionFormDialogData = {
      assetId,
      assetClass: this.assetClass(),
      currency: item.quoteCurrency,
      accountId: target.accountId,
      type: TRANSACTION_TYPE_BUY,
    };
    const bought = await firstValueFrom(
      this.dialog
        .open(TransactionFormDialog, { width: '480px', data: transactionData })
        .afterClosed(),
    );
    if (saved || bought) {
      this.reloadAll();
    }
  }

  private reloadWhenDone(ref: MatDialogRef<unknown, unknown>): void {
    ref.afterClosed().subscribe((done) => {
      if (done) {
        this.watchlistResource.reload();
      }
    });
  }

  private reloadAll(): void {
    this.watchlistResource.reload();
    this.changed.emit();
  }
}
