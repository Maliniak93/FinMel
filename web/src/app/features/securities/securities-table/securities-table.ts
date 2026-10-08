import { Component, computed, inject, input, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioSecurities,
  type AssetClass,
  type SecurityHoldingResponse,
  type TransactionType,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatDate, formatMoney, formatPercent, formatQuantity } from '../../../shared/format';
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
const PRICE_UNAVAILABLE_REASON_LABELS: readonly string[] = [
  'securities.priceUnavailable.noQuote',
  'securities.priceUnavailable.fxRateMissing',
  'securities.priceUnavailable.marketDataUnavailable',
];

@Component({
  selector: 'app-securities-table',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './securities-table.html',
  styleUrl: './securities-table.scss',
})
export class SecuritiesTable {
  private readonly dialog = inject(MatDialog);

  readonly assetClass = input.required<AssetClass>();

  protected readonly displayedColumns = [
    'instrument',
    'portfolio',
    'quantity',
    'averageBuyPrice',
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

  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatPercent = formatPercent;
  protected readonly formatDate = formatDate;
  protected readonly buyType = TRANSACTION_TYPE_BUY;
  protected readonly sellType = TRANSACTION_TYPE_SELL;
  protected readonly dividendType = TRANSACTION_TYPE_DIVIDEND;

  protected instrumentLabel(holding: SecurityHoldingResponse): string {
    return [holding.ticker, holding.exchange].filter(Boolean).join(' · ');
  }

  protected unavailableLabel(holding: SecurityHoldingResponse): string | null {
    const reason = holding.priceUnavailableReason;
    return reason === null ? null : (PRICE_UNAVAILABLE_REASON_LABELS[reason] ?? null);
  }

  protected gainAmount(
    holding: SecurityHoldingResponse,
  ): { value: number | string; currency: string } | null {
    if (holding.unrealizedPlPln !== null) {
      return { value: holding.unrealizedPlPln, currency: 'PLN' };
    }
    return holding.unrealizedPl === null
      ? null
      : { value: holding.unrealizedPl, currency: holding.currency };
  }

  protected gainSign(value: number | string): 'positive' | 'negative' | '' {
    const amount = Number(value);
    return amount > 0 ? 'positive' : amount < 0 ? 'negative' : '';
  }

  protected transactionsLink(holding: SecurityHoldingResponse): string[] {
    return ['/portfolios', holding.portfolioId, 'assets', holding.assetId, 'transactions'];
  }

  protected openAddDialog(): void {
    const data: AssetFormDialogData = { assetClass: this.assetClass() };
    this.reloadWhenDone(this.dialog.open(AssetFormDialog, { width: '560px', data }));
  }

  protected openTransactionDialog(holding: SecurityHoldingResponse, type: TransactionType): void {
    const data: TransactionFormDialogData = {
      portfolioId: holding.portfolioId,
      assetId: holding.assetId,
      assetClass: this.assetClass(),
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
