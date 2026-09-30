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
  getApiPortfolioPortfoliosByPortfolioIdDepositsByAssetId,
  getApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetId,
  type AssetResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { confirmSetAssetArchived } from '../../shared/asset-archive';
import { toDateOnly } from '../../shared/date-only';
import { formatDate, formatMoney, formatQuantity } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { DepositFormDialog } from '../deposits/deposit-form-dialog/deposit-form-dialog';
import { SavingsAccountFormDialog } from '../deposits/savings-account-form-dialog/savings-account-form-dialog';
import { ASSET_CLASS, assetClassLabel } from './asset-class';
import { AssetFormDialog } from './asset-form/asset-form-dialog/asset-form-dialog';
import { VALUATION_MODE } from './asset-valuation-mode';

// A manual valuation older than this is flagged as stale, prompting a refresh (no ADR/backlog
// number given — domain-model.md just says "every N months").
const STALE_MANUAL_VALUE_MONTHS = 6;

// Market prices older than this are stale (domain.md, E4 [S]) — a separate, much tighter, rule
// than manual valuations above since a synced price is expected daily, not entered by hand.
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

// MatDialog/MatSnackBar are injected as services only (never referenced as template directives),
// so MatDialogModule/MatSnackBarModule are deliberately NOT in `imports` below — see
// portfolios.ts for why importing them here would shadow a TestBed-level override in specs.
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

  // An archived portfolio is read-only: Portfolio rejects every asset write into it with 409
  // (archived-portfolio-out-of-net-worth), so the page offers no add / edit / remove action and
  // shows a notice instead. The assets themselves stay listed.
  protected readonly isArchived = computed(
    () => this.portfolioResource.hasValue() && this.portfolioResource.value().isArchived,
  );

  // The actions column holds only Edit / Delete, so an archived portfolio drops it entirely.
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

  // asset-archive: an asset archived on its own is hidden until "Show archived" is on. Filtered here,
  // because ListAssets returns every asset — other callers (the transactions view) must still resolve
  // an archived one.
  protected readonly showArchived = signal(false);

  protected readonly visibleAssets = computed(() =>
    this.assetsResource.hasValue()
      ? this.assetsResource.value().filter((asset) => this.showArchived() || !asset.isArchived)
      : [],
  );

  // AssetResponse only carries InstrumentId (ADR-003, no FK) — one lookup per distinct instrument
  // used by this portfolio's market assets fills in ticker/last price/date/source for display.
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

  // Only called once the template has confirmed instrument.lastPrice != null.
  protected marketValue(asset: AssetResponse, instrument: InstrumentDetailsResponse): number {
    return Number(asset.quantity) * Number(instrument.lastPrice);
  }

  // A term deposit is Due once its maturity date is today or earlier — the same rule as the
  // Deposits page's server-side status, on the viewer's local calendar date — until it is settled.
  // An archived deposit is read-only, so it is never flagged Due.
  protected isDepositDue(asset: AssetResponse): boolean {
    return (
      !asset.isArchived &&
      !asset.depositSettled &&
      !!asset.depositMaturityDate &&
      asset.depositMaturityDate <= toDateOnly(new Date())
    );
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

  // A term deposit's terms live on the deposit endpoints, so its row edits through DepositFormDialog
  // with the loaded terms, never through the generic asset form.
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

  // Likewise a savings account's terms live on the savings-account endpoints (savings-accounts).
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

  // asset-archive: Archive / Restore sit in the row menu, behind a confirmation, and reload the list.
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

  // The delete cascades to the asset's transactions (spec-08), so the confirmation names them
  // whenever there are any.
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
