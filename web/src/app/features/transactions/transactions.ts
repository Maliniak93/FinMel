import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, type PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById,
  getApiPortfolioPortfoliosById,
  getApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions,
  type TransactionResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format-money';
import { assetClassLabel } from '../assets/asset-class';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from './transaction-form-dialog/transaction-form-dialog';
import { transactionTypeLabel } from './transaction-type';

const DEFAULT_PAGE_SIZE = 20;

// MatDialog/MatSnackBar are injected as services only (never referenced as template directives) —
// see assets.ts for why importing MatDialogModule/MatSnackBarModule here would shadow a
// TestBed-level override in specs.
@Component({
  selector: 'app-transactions',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    RouterLink,
  ],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class Transactions {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly portfolioId = input.required<string>();
  readonly assetId = input.required<string>();

  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  protected readonly assetResource = resource({
    params: () => ({ portfolioId: this.portfolioId(), assetId: this.assetId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosByPortfolioIdAssetsById({
        path: { portfolioId: params.portfolioId, id: params.assetId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load asset.');
      }
      return result.data;
    },
  });

  // AssetResponse carries no archived flag, so the owning portfolio is loaded for it alone. Loaded
  // once: nothing on this page can archive or restore it, so reload() leaves it alone.
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

  // An archived portfolio is read-only: Portfolio rejects every transaction write on its assets
  // with 409 (archived-portfolio-out-of-net-worth), so the page offers no record / edit / delete
  // action and shows a notice instead. The history stays listed. If the portfolio fails to load,
  // the asset load right next to it fails the same way and the page shows that error.
  protected readonly isArchived = computed(
    () => this.portfolioResource.hasValue() && this.portfolioResource.value().isArchived,
  );

  // The actions column holds only Edit / Delete, so an archived portfolio drops it entirely.
  protected readonly displayedColumns = computed(() => [
    'date',
    'type',
    'quantity',
    'currency',
    'value',
    ...(this.isArchived() ? [] : ['actions']),
  ]);

  protected readonly transactionsResource = resource({
    params: () => ({
      portfolioId: this.portfolioId(),
      assetId: this.assetId(),
      page: this.pageIndex() + 1,
      pageSize: this.pageSize(),
    }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions({
        path: { portfolioId: params.portfolioId, assetId: params.assetId },
        query: { page: params.page, pageSize: params.pageSize },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load transactions.');
      }
      return result.data;
    },
  });

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly transactionTypeLabel = transactionTypeLabel;
  protected readonly formatMoney = formatMoney;

  protected formatQuantity(quantity: number | string): string {
    return new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 8 }).format(Number(quantity));
  }

  // PagedResponse.TotalCount is a server-side int, but the generated client types every numeric
  // DTO property as `number | string` (same as Asset.Quantity/ManualValue) — coerce for
  // mat-paginator's `[length]` input, which requires a real number.
  protected asNumber(value: number | string): number {
    return Number(value);
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  private reload(): void {
    this.transactionsResource.reload();
    // Recording/editing/deleting a transaction changes the asset's derived quantity (ADR-009) —
    // refresh the header so it never shows a stale number after a mutation.
    this.assetResource.reload();
  }

  // The dialog filters its transaction types by the asset's class (cash-transaction-types), so it
  // opens only once the asset has loaded; the buttons that open it are gated the same way.
  private dialogData(): TransactionFormDialogData | null {
    if (!this.assetResource.hasValue()) {
      return null;
    }
    return {
      portfolioId: this.portfolioId(),
      assetId: this.assetId(),
      assetClass: this.assetResource.value().assetClass,
    };
  }

  protected openCreateDialog(): void {
    const data = this.dialogData();
    if (!data) {
      return;
    }
    const ref = this.dialog.open(TransactionFormDialog, { width: '480px', data });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reload();
      }
    });
  }

  protected openEditDialog(transaction: TransactionResponse): void {
    const data = this.dialogData();
    if (!data) {
      return;
    }
    const ref = this.dialog.open(TransactionFormDialog, {
      width: '480px',
      data: { ...data, transaction },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reload();
      }
    });
  }

  protected async remove(transaction: TransactionResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: 'Delete this transaction?',
            message: `This ${transactionTypeLabel(transaction.type)} of ${this.formatQuantity(transaction.quantity)} will be permanently deleted. This can't be undone.`,
            confirmLabel: 'Delete',
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById({
      path: { portfolioId: this.portfolioId(), assetId: this.assetId(), id: transaction.id },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? 'Failed to delete transaction.',
        'Dismiss',
      );
      return;
    }

    this.reload();
  }
}
