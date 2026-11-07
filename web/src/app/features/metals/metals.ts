import { Component, computed, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
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
import { confirmSetAssetArchived } from '../../shared/asset-archive';
import { confirmRemoveAsset } from '../../shared/confirm-remove-asset';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { formatDate, formatQuantity } from '../../shared/format';
import { Gain } from '../../shared/gain/gain';
import { gainOf, sumGains } from '../../shared/gain/gain-math';
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

// Display-only sum of the row values, in whole grosze.
function sumInGrosze(amounts: readonly number[]): number {
  return amounts.reduce((sum, amount) => sum + Math.round(amount * 100), 0) / 100;
}

@Component({
  selector: 'app-metals',
  imports: [
    EmptyState,
    Gain,
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatMenuModule,
    MatSlideToggleModule,
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
  private readonly snackBar = inject(MatSnackBar);

  protected readonly displayedColumns = [
    'name',
    'metal',
    'weightPerPiece',
    'pieces',
    'totalWeight',
    'valueToday',
    'gain',
    'actions',
  ];

  protected readonly footerColumns = ['totalLabel', 'valueTodayTotal', 'gainTotal', 'totalRest'];
  protected readonly totalLabelSpan = this.displayedColumns.indexOf('valueToday');

  protected readonly holdingsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioMetals({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('metals.loadFailed'));
      }
      return result.data ?? [];
    },
  });

  protected readonly showArchived = signal(false);

  protected readonly visibleHoldings = computed(() =>
    this.holdingsResource.hasValue()
      ? this.holdingsResource.value().filter((h) => this.showArchived() || !h.isArchived)
      : [],
  );

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

  protected gain(holding: MetalResponse) {
    return gainOf({ value: this.valueToday(holding), cost: holding.costPln });
  }

  protected readonly totals = computed(() => {
    if (!this.holdingsResource.hasValue()) {
      return null;
    }
    const rows = this.visibleHoldings();
    const values = rows.flatMap((holding) => {
      const value = this.valueToday(holding);
      return value === null ? [] : [value];
    });
    return {
      value: values.length === 0 ? null : sumInGrosze(values),
      gain: sumGains(
        rows.map((holding) => ({ value: this.valueToday(holding), cost: holding.costPln })),
      ),
    };
  });

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

  protected async setArchived(holding: MetalResponse, archive: boolean): Promise<void> {
    const done = await confirmSetAssetArchived(
      this.dialog,
      this.snackBar,
      { assetId: holding.assetId, name: holding.name },
      archive,
    );
    if (done) {
      this.holdingsResource.reload();
    }
  }

  protected async remove(holding: MetalResponse): Promise<void> {
    const grams = Number(holding.totalFineGrams);
    const done = await confirmRemoveAsset(this.dialog, this.snackBar, {
      assetId: holding.assetId,
      name: holding.name,
      transactionCount: Number(holding.transactionCount),
      held: grams > 0 ? { quantity: grams, unit: 'grams' } : null,
    });
    if (done) {
      this.holdingsResource.reload();
    }
  }

  private reloadWhenDone(ref: MatDialogRef<unknown, boolean>): void {
    ref.afterClosed().subscribe((done) => {
      if (done) {
        this.holdingsResource.reload();
      }
    });
  }
}
