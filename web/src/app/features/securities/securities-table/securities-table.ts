import { Component, computed, inject, input, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioSecurities,
  type AssetClass,
  type SecurityHoldingResponse,
  type TransactionType,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { fromDateOnly } from '../../../shared/date-only';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { formatDate, formatMoney, formatPercent, formatQuantity } from '../../../shared/format';
import { Money } from '../../../shared/money/money';
import { SectionError } from '../../../shared/section-error/section-error';
import { Skeleton } from '../../../shared/skeleton/skeleton';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { ASSET_CLASS } from '../../assets/asset-class';
import {
  AssetFormDialog,
  type AssetFormDialogData,
} from '../../assets/asset-form/asset-form-dialog/asset-form-dialog';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from '../../transactions/transaction-form-dialog/transaction-form-dialog';
import {
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_DIVIDEND,
  TRANSACTION_TYPE_SELL,
} from '../../transactions/transaction-type';

// Mirrors Skarbiec.Portfolio.Features.Securities.SecurityPriceUnavailableReason in declaration order: the enum travels as its int.
export const PRICE_UNAVAILABLE_REASON_LABELS: readonly string[] = [
  'securities.priceUnavailable.noQuote',
  'securities.priceUnavailable.fxRateMissing',
  'securities.priceUnavailable.marketDataUnavailable',
];

const STALE_AFTER_MS = 7 * 24 * 60 * 60 * 1000;

@Component({
  selector: 'app-securities-table',
  imports: [
    EmptyState,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatTableModule,
    Money,
    RouterLink,
    SectionError,
    Skeleton,
    StatusChip,
    TranslocoPipe,
  ],
  templateUrl: './securities-table.html',
  styleUrl: './securities-table.scss',
})
export class SecuritiesTable {
  private readonly dialog = inject(MatDialog);

  readonly assetClass = input.required<AssetClass>();
  readonly account = input<string>();

  protected readonly displayedColumns = [
    'instrument',
    'account',
    'quantity',
    'averageBuyPrice',
    'averageBuyPricePln',
    'price',
    'value',
    'gain',
    'actions',
  ];

  protected readonly emptyKey = computed(() =>
    this.assetClass() === ASSET_CLASS.Etf ? 'securities.empty.etf' : 'securities.empty.stock',
  );

  protected readonly holdingsResource = resource({
    params: () => ({ assetClass: this.assetClass() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioSecurities({
        query: { assetClass: params.assetClass },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('securities.loadFailed'),
        );
      }
      return (
        result.data ?? { holdings: [], totals: { valuePln: 0, costPln: 0, unrealizedPlPln: 0 } }
      );
    },
  });

  protected readonly footerColumns = computed(() => (this.account() ? [] : this.displayedColumns));

  protected readonly holdings = computed(() => {
    if (!this.holdingsResource.hasValue()) {
      return [];
    }
    const account = this.account();
    const holdings = this.holdingsResource.value().holdings;
    return account ? holdings.filter((holding) => holding.accountId === account) : holdings;
  });

  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatPercent = formatPercent;
  protected readonly formatDate = formatDate;
  protected readonly buyType = TRANSACTION_TYPE_BUY;
  protected readonly sellType = TRANSACTION_TYPE_SELL;
  protected readonly dividendType = TRANSACTION_TYPE_DIVIDEND;

  protected readonly classKey = computed(() =>
    this.assetClass() === ASSET_CLASS.Etf ? 'securities.tabs.etfs' : 'securities.tabs.stocks',
  );

  protected subtitleParams(
    holding: SecurityHoldingResponse,
    className: string,
  ): { class: string; qty: string; price: string } {
    return {
      class: className,
      qty: formatQuantity(holding.quantity),
      price: holding.lastPrice === null ? '—' : formatMoney(holding.lastPrice, holding.currency),
    };
  }

  protected toNumber(value: number | string | null | undefined): number | null {
    return value === null || value === undefined ? null : Number(value);
  }

  protected isStale(date: string): boolean {
    return Date.now() - fromDateOnly(date).getTime() > STALE_AFTER_MS;
  }

  protected unavailableLabel(holding: SecurityHoldingResponse): string | null {
    const reason = holding.priceUnavailableReason;
    return reason === null ? null : (PRICE_UNAVAILABLE_REASON_LABELS[reason] ?? null);
  }

  protected gainAmount(
    holding: SecurityHoldingResponse,
  ): { value: number; currency: string } | null {
    if (holding.unrealizedPlPln !== null) {
      return { value: Number(holding.unrealizedPlPln), currency: 'PLN' };
    }
    return holding.unrealizedPl === null
      ? null
      : { value: Number(holding.unrealizedPl), currency: holding.currency };
  }

  protected transactionsLink(holding: SecurityHoldingResponse): string[] {
    return ['/wealth/assets', holding.assetId, 'transactions'];
  }

  reload(): void {
    this.holdingsResource.reload();
  }

  protected openAddDialog(): void {
    const data: AssetFormDialogData = { assetClass: this.assetClass() };
    this.reloadWhenDone(this.dialog.open(AssetFormDialog, { width: '560px', data }));
  }

  protected openTransactionDialog(holding: SecurityHoldingResponse, type: TransactionType): void {
    const data: TransactionFormDialogData = {
      assetId: holding.assetId,
      assetClass: this.assetClass(),
      currency: holding.currency,
      accountId: holding.accountId,
      type,
    };
    this.reloadWhenDone(this.dialog.open(TransactionFormDialog, { width: '480px', data }));
  }

  private reloadWhenDone(ref: MatDialogRef<unknown, boolean>): void {
    ref.afterClosed().subscribe((done) => {
      if (done) {
        this.holdingsResource.reload();
      }
    });
  }
}
