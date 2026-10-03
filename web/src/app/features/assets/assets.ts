import { Component, computed, inject, input, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import {
  getApiMarketdataInstrumentsById,
  type InstrumentDetailsResponse,
} from '../../api/marketdata';
import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioPortfoliosById,
  getApiPortfolioPortfoliosByPortfolioIdAssets,
  getApiPortfolioPortfoliosByPortfolioIdBondsByAssetId,
  getApiPortfolioPortfoliosByPortfolioIdDepositsByAssetId,
  getApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetId,
  type AssetResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { confirmSetAssetArchived } from '../../shared/asset-archive';
import { toDateOnly } from '../../shared/date-only';
import { formatDate, formatMoney, formatQuantity } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { BondPurchaseDialog } from '../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { DepositFormDialog } from '../deposits/deposit-form-dialog/deposit-form-dialog';
import { SavingsAccountFormDialog } from '../deposits/savings-account-form-dialog/savings-account-form-dialog';
import { ASSET_CLASS, assetClassLabel } from './asset-class';
import { AssetFormDialog } from './asset-form/asset-form-dialog/asset-form-dialog';
import { VALUATION_MODE } from './asset-valuation-mode';

const STALE_MANUAL_VALUE_MONTHS = 6;

const STALE_PRICE_DAYS = 7;

function isStale(manualValueDate: string): boolean {
  const threshold = new Date();
  threshold.setMonth(threshold.getMonth() - STALE_MANUAL_VALUE_MONTHS);
  return new Date(manualValueDate) < threshold;
}

function isPriceStale(lastPriceDate: string | null | undefined): boolean {
  if (!lastPriceDate) {
    return true;
  }
  const threshold = new Date();
  threshold.setDate(threshold.getDate() - STALE_PRICE_DAYS);
  return new Date(lastPriceDate) < threshold;
}

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-assets',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatTableModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './assets.html',
  styleUrl: './assets.scss',
})
export class Assets {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly portfolioId = input.required<string>();

  protected readonly portfolioResource = resource({
    params: () => ({ portfolioId: this.portfolioId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosById({
        path: { id: params.portfolioId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('assets.portfolioLoadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly isArchived = computed(
    () => this.portfolioResource.hasValue() && this.portfolioResource.value().isArchived,
  );

  protected readonly displayedColumns = computed(() => [
    'assetClass',
    'name',
    'quantity',
    'currency',
    'value',
    'manualValueDate',
    ...(this.isArchived() ? [] : ['actions']),
  ]);

  protected readonly assetsResource = resource({
    params: () => ({ portfolioId: this.portfolioId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosByPortfolioIdAssets({
        path: { portfolioId: params.portfolioId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('assets.loadFailed'));
      }
      return result.data ?? [];
    },
  });

  protected readonly showArchived = signal(false);

  protected readonly visibleAssets = computed(() =>
    this.assetsResource.hasValue()
      ? this.assetsResource.value().filter((asset) => this.showArchived() || !asset.isArchived)
      : [],
  );

  protected readonly instrumentDetailsResource = resource({
    params: () => {
      const instrumentIds = [
        ...new Set(
          (this.assetsResource.value() ?? [])
            .map((asset) => asset.instrumentId)
            .filter((id): id is string => !!id),
        ),
      ];
      return instrumentIds.length > 0 ? { instrumentIds } : undefined;
    },
    loader: async ({ params, abortSignal }) => {
      const details = await Promise.all(
        params.instrumentIds.map(async (id) => {
          const result = await getApiMarketdataInstrumentsById({
            path: { id },
            signal: abortSignal,
          });
          return result.error ? null : (result.data ?? null);
        }),
      );
      return new Map(
        details
          .filter((detail): detail is InstrumentDetailsResponse => detail !== null)
          .map((detail) => [detail.id, detail]),
      );
    },
  });

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly isStale = isStale;
  protected readonly isPriceStale = isPriceStale;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatDate = formatDate;
  protected readonly VALUATION_MODE = VALUATION_MODE;

  protected instrumentFor(asset: AssetResponse): InstrumentDetailsResponse | undefined {
    return asset.instrumentId
      ? this.instrumentDetailsResource.value()?.get(asset.instrumentId)
      : undefined;
  }

  protected marketValue(asset: AssetResponse, instrument: InstrumentDetailsResponse): number {
    return Number(asset.quantity) * Number(instrument.lastPrice);
  }

  protected isDepositDue(asset: AssetResponse): boolean {
    return (
      !asset.isArchived &&
      !asset.depositSettled &&
      !!asset.depositMaturityDate &&
      asset.depositMaturityDate <= toDateOnly(new Date())
    );
  }

  protected isSavingsInterestDue(asset: AssetResponse): boolean {
    return !asset.isArchived && asset.savingsInterestDue === true;
  }

  protected openCreateDialog(): void {
    const ref = this.dialog.open(AssetFormDialog, {
      width: '560px',
      data: { portfolioId: this.portfolioId() },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  protected openEditDialog(asset: AssetResponse): void {
    if (Number(asset.assetClass) === ASSET_CLASS.Deposit) {
      void this.openDepositEditDialog(asset);
      return;
    }

    if (Number(asset.assetClass) === ASSET_CLASS.Savings) {
      void this.openSavingsAccountEditDialog(asset);
      return;
    }

    if (Number(asset.assetClass) === ASSET_CLASS.Bond) {
      void this.openBondEditDialog(asset);
      return;
    }

    const ref = this.dialog.open(AssetFormDialog, {
      width: '560px',
      data: { portfolioId: this.portfolioId(), asset },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  private async openDepositEditDialog(asset: AssetResponse): Promise<void> {
    const result = await getApiPortfolioPortfoliosByPortfolioIdDepositsByAssetId({
      path: { portfolioId: this.portfolioId(), assetId: asset.id },
    });
    if (result.error || !result.data) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('assets.depositLoadFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    const ref = this.dialog.open(DepositFormDialog, {
      width: '560px',
      data: { deposit: result.data },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  private async openBondEditDialog(asset: AssetResponse): Promise<void> {
    const result = await getApiPortfolioPortfoliosByPortfolioIdBondsByAssetId({
      path: { portfolioId: this.portfolioId(), assetId: asset.id },
    });
    if (result.error || !result.data) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('assets.bondLoadFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    const ref = this.dialog.open(BondPurchaseDialog, {
      width: '560px',
      data: { bond: result.data },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  private async openSavingsAccountEditDialog(asset: AssetResponse): Promise<void> {
    const result = await getApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetId({
      path: { portfolioId: this.portfolioId(), assetId: asset.id },
    });
    if (result.error || !result.data) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('assets.savingsAccountLoadFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    const ref = this.dialog.open(SavingsAccountFormDialog, {
      width: '560px',
      data: { account: result.data },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  protected async setArchived(asset: AssetResponse, archive: boolean): Promise<void> {
    const done = await confirmSetAssetArchived(
      this.dialog,
      this.snackBar,
      { portfolioId: this.portfolioId(), assetId: asset.id, name: asset.name },
      archive,
    );
    if (done) {
      this.assetsResource.reload();
    }
  }

  protected async remove(asset: AssetResponse): Promise<void> {
    const transactionCount = Number(asset.transactionCount);
    const message =
      transactionCount > 0
        ? translate('assets.delete.messageWithTransactions', {
            name: asset.name,
            count: transactionCount,
          })
        : translate('assets.delete.message', { name: asset.name });
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('assets.delete.title'),
            message,
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioPortfoliosByPortfolioIdAssetsById({
      path: { portfolioId: this.portfolioId(), id: asset.id },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('assets.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.assetsResource.reload();
  }
}
