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

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioPortfoliosById,
  getApiPortfolioPortfoliosByPortfolioIdAssets,
  type AssetResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { assetClassLabel } from './asset-class';
import { AssetFormDialog } from './asset-form-dialog/asset-form-dialog';

// A manual valuation older than this is flagged as stale, prompting a refresh (no ADR/backlog
// number given — domain-model.md just says "every N months").
const STALE_MANUAL_VALUE_MONTHS = 6;

function isStale(manualValueDate: string): boolean {
  const threshold = new Date();
  threshold.setMonth(threshold.getMonth() - STALE_MANUAL_VALUE_MONTHS);
  return new Date(manualValueDate) < threshold;
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

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly isStale = isStale;

  protected formatMoney(amount: number | string, currency: string): string {
    try {
      return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(Number(amount));
    } catch {
      return `${amount} ${currency}`;
    }
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
