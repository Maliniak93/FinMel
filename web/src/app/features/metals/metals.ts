import { Component, computed, inject, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiMarketdataMetalPrices, type MetalPriceResponse } from '../../api/marketdata';
import {
  getApiPortfolioMetals,
  type MetalResponse,
  type TransactionType,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { formatDate, formatQuantity } from '../../shared/format';
import { Money } from '../../shared/money/money';
import { SectionError } from '../../shared/section-error/section-error';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { StatusChip } from '../../shared/status-chip/status-chip';
import { ASSET_CLASS } from '../assets/asset-class';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from '../transactions/transaction-form-dialog/transaction-form-dialog';
import { TRANSACTION_TYPE_BUY, TRANSACTION_TYPE_SELL } from '../transactions/transaction-type';
import { METALS, metalLabel, wholeTroyOunces } from './metal';
import { MetalFormDialog, type MetalFormDialogData } from './metal-form-dialog/metal-form-dialog';

@Component({
  selector: 'app-metals',
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
  templateUrl: './metals.html',
  styleUrl: './metals.scss',
})
export class Metals {
  private readonly dialog = inject(MatDialog);

  protected readonly displayedColumns = [
    'name',
    'metal',
    'weightPerPiece',
    'pieces',
    'totalWeight',
    'valueToday',
    'actions',
  ];

  protected readonly holdingsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioMetals({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('metals.loadFailed'));
      }
      return result.data ?? [];
    },
  });

  protected readonly pricesResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiMarketdataMetalPrices({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('metals.pricesLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  private readonly pricesByMetal = computed(
    () =>
      new Map(
        (this.pricesResource.hasValue() ? this.pricesResource.value() : []).map((price) => [
          Number(price.metal),
          price,
        ]),
      ),
  );

  protected readonly priceRows = computed(() =>
    METALS.map((metal) => ({
      label: metal.label,
      price: this.pricesByMetal().get(Number(metal.value)),
    })),
  );

  protected readonly metalLabel = metalLabel;
  protected readonly wholeTroyOunces = wholeTroyOunces;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatDate = formatDate;
  protected readonly buyType = TRANSACTION_TYPE_BUY;
  protected readonly sellType = TRANSACTION_TYPE_SELL;

  protected toNumber(value: number | string | null | undefined): number | null {
    return value === null || value === undefined ? null : Number(value);
  }

  protected hasPrice(
    price: MetalPriceResponse | undefined,
  ): price is MetalPriceResponse & { pricePerGramPln: number | string } {
    return price?.pricePerGramPln !== null && price?.pricePerGramPln !== undefined;
  }

  protected valueToday(holding: MetalResponse): number | null {
    const price = this.pricesByMetal().get(Number(holding.metal));
    if (!this.hasPrice(price)) {
      return null;
    }
    return (
      Number(holding.pieces) *
      Number(holding.fineWeightGramsPerPiece) *
      Number(price.pricePerGramPln)
    );
  }

  protected isReadOnly(holding: MetalResponse): boolean {
    return holding.isArchived;
  }

  protected transactionsLink(holding: MetalResponse): string[] {
    return ['/wealth/assets', holding.assetId, 'transactions'];
  }

  protected openAddDialog(): void {
    const data: MetalFormDialogData = {};
    this.reloadWhenDone(this.dialog.open(MetalFormDialog, { width: '560px', data }));
  }

  protected openEditDialog(holding: MetalResponse): void {
    const data: MetalFormDialogData = { metal: holding };
    this.reloadWhenDone(this.dialog.open(MetalFormDialog, { width: '560px', data }));
  }

  protected openTransactionDialog(holding: MetalResponse, type: TransactionType): void {
    const data: TransactionFormDialogData = {
      assetId: holding.assetId,
      assetClass: ASSET_CLASS.PreciousMetal,
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
