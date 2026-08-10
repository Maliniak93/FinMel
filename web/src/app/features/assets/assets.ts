import { DatePipe } from '@angular/common';
import { Component, inject, input, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { getApiMarketdataInstrumentsById, type InstrumentDetailsResponse } from '../../api/marketdata';
import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioPortfoliosById,
  getApiPortfolioPortfoliosByPortfolioIdAssets,
  type AssetResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatMoney } from '../../shared/format-money';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { assetClassLabel } from './asset-class';
import { AssetFormDialog } from './asset-form-dialog/asset-form-dialog';
import { priceSourceLabel } from './price-source';

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
    DatePipe,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    RouterLink,
  ],
  templateUrl: './assets.html',
  styleUrl: './assets.scss',
})
export class Assets {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly portfolioId = input.required<string>();

  protected readonly displayedColumns = [
    'assetClass',
    'name',
    'quantity',
    'currency',
    'value',
    'manualValueDate',
    'actions',
  ];

  protected readonly portfolioResource = resource({
    params: () => ({ portfolioId: this.portfolioId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosById({
        path: { id: params.portfolioId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load portfolio.');
      }
      return result.data;
    },
  });

  protected readonly assetsResource = resource({
    params: () => ({ portfolioId: this.portfolioId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosByPortfolioIdAssets({
        path: { portfolioId: params.portfolioId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load assets.');
      }
      return result.data ?? [];
    },
  });

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
          const result = await getApiMarketdataInstrumentsById({ path: { id }, signal: abortSignal });
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
  protected readonly priceSourceLabel = priceSourceLabel;
  protected readonly formatMoney = formatMoney;

  protected instrumentFor(asset: AssetResponse): InstrumentDetailsResponse | undefined {
    return asset.instrumentId
      ? this.instrumentDetailsResource.value()?.get(asset.instrumentId)
      : undefined;
  }

  // Only called once the template has confirmed instrument.lastPrice != null.
  protected marketValue(asset: AssetResponse, instrument: InstrumentDetailsResponse): number {
    return Number(asset.quantity) * Number(instrument.lastPrice);
  }

  protected formatQuantity(quantity: number | string): string {
    return new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 8 }).format(Number(quantity));
  }

  protected openCreateDialog(): void {
    const ref = this.dialog.open(AssetFormDialog, {
      width: '480px',
      data: { portfolioId: this.portfolioId() },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  protected openEditDialog(asset: AssetResponse): void {
    const ref = this.dialog.open(AssetFormDialog, {
      width: '480px',
      data: { portfolioId: this.portfolioId(), asset },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.assetsResource.reload();
      }
    });
  }

  protected async remove(asset: AssetResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: 'Delete this asset?',
            message: `"${asset.name}" will be permanently deleted. This can't be undone.`,
            confirmLabel: 'Delete',
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
        readProblemDetails(result.error).detail ?? 'Failed to delete asset.',
        'Dismiss',
      );
      return;
    }

    this.assetsResource.reload();
  }
}
