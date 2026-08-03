import { DatePipe } from '@angular/common';
import { Component, inject, input, resource, signal } from '@angular/core';
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
  getApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions,
  type TransactionResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { assetClassLabel } from '../assets/asset-class';
import { TransactionFormDialog } from './transaction-form-dialog/transaction-form-dialog';
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

  protected readonly displayedColumns = [
    'date',
    'type',
    'quantity',
    'unitPrice',
    'fee',
    'value',
    'actions',
  ];

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

  // PagedResponse.TotalCount is a server-side int, but the generated client types every numeric
  // DTO property as `number | string` (same as Asset.Quantity/ManualValue) — coerce for
  // mat-paginator's `[length]` input, which requires a real number.
  protected asNumber(value: number | string): number {
    return Number(value);
  }

  // Buy/Sell price against a real unit price; every other type submits 1 for unit price
  // (transaction-form-dialog.ts), so quantity * unitPrice reduces to "the amount itself" for those
  // rows without a second formula.
  protected transactionValue(transaction: TransactionResponse): number {
    return Number(transaction.quantity) * Number(transaction.unitPrice);
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

  protected openCreateDialog(): void {
    const ref = this.dialog.open(TransactionFormDialog, {
      width: '480px',
      data: { portfolioId: this.portfolioId(), assetId: this.assetId() },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reload();
      }
    });
  }

  protected openEditDialog(transaction: TransactionResponse): void {
    const ref = this.dialog.open(TransactionFormDialog, {
      width: '480px',
      data: { portfolioId: this.portfolioId(), assetId: this.assetId(), transaction },
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
