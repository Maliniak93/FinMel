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
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById,
  deleteApiPortfolioTransfersByTransferId,
  getApiPortfolioPortfoliosById,
  getApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions,
  type TransactionResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney, formatMonth, formatQuantity } from '../../shared/format';
import { ASSET_CLASS, assetClassLabel } from '../assets/asset-class';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from './transaction-form-dialog/transaction-form-dialog';
import { transactionTypeLabel } from './transaction-type';
import { transferLabel } from './transfer-direction';

const DEFAULT_PAGE_SIZE = 20;

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-transactions',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    RouterLink,
    TranslocoPipe,
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
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('transactions.assetLoadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly portfolioResource = resource({
    params: () => ({ portfolioId: this.portfolioId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioPortfoliosById({
        path: { id: params.portfolioId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('transactions.portfolioLoadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly isArchived = computed(
    () => this.portfolioResource.hasValue() && this.portfolioResource.value().isArchived,
  );

  protected readonly isTermDeposit = computed(
    () =>
      this.assetResource.hasValue() &&
      Number(this.assetResource.value().assetClass) === ASSET_CLASS.Deposit,
  );

  protected readonly isAssetArchived = computed(
    () => this.assetResource.hasValue() && this.assetResource.value().isArchived,
  );

  protected readonly isReadOnly = computed(
    () => this.isArchived() || this.isAssetArchived() || this.isTermDeposit(),
  );

  protected readonly displayedColumns = computed(() => [
    'date',
    'type',
    'quantity',
    'currency',
    'value',
    ...(this.isReadOnly() ? [] : ['actions']),
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
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('transactions.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly assetClassLabel = assetClassLabel;
  protected readonly transactionTypeLabel = transactionTypeLabel;
  protected readonly transferLabel = transferLabel;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatDate = formatDate;
  protected readonly formatMonth = formatMonth;

  // The generated client types every numeric property as `number | string`.
  protected asNumber(value: number | string): number {
    return Number(value);
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  private reload(): void {
    this.transactionsResource.reload();
    this.assetResource.reload();
  }

  private dialogData(): TransactionFormDialogData | null {
    if (!this.assetResource.hasValue()) {
      return null;
    }
    return {
      portfolioId: this.portfolioId(),
      assetId: this.assetId(),
      assetClass: this.assetResource.value().assetClass,
      currency: this.assetResource.value().currency,
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
            title: translate('transactions.delete.title'),
            message: translate('transactions.delete.message', {
              type: translate(transactionTypeLabel(transaction.type)),
              quantity: formatQuantity(transaction.quantity),
            }),
            confirmLabel: translate('common.delete'),
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
        readProblemDetails(result.error).detail ?? translate('transactions.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reload();
  }

  protected async removeTransfer(transaction: TransactionResponse): Promise<void> {
    const transfer = transaction.transfer;
    if (!transfer) {
      return;
    }

    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('transactions.deleteTransfer.title'),
            message: translate('transactions.deleteTransfer.message', {
              quantity: formatMoney(transaction.quantity, transaction.currency),
              counterpart: transfer.counterpartAssetName,
            }),
            confirmLabel: translate('common.delete'),
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioTransfersByTransferId({
      path: { transferId: transfer.transferId },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('transactions.deleteTransfer.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.reload();
  }
}
