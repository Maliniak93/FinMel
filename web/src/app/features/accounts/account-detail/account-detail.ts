import { Component, computed, inject, input, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, type PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioAssetsByAssetIdTransactionsById,
  deleteApiPortfolioTransfersByTransferId,
  getApiPortfolioAccountsById,
  getApiPortfolioAssets,
  getApiPortfolioAssetsByAssetIdTransactions,
  type TransactionResponse,
} from '../../../api/portfolio';
import { getApiReportingDashboard } from '../../../api/reporting';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney, formatQuantity } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';
import { TransactionFormDialog } from '../../transactions/transaction-form-dialog/transaction-form-dialog';
import { transactionTypeLabel } from '../../transactions/transaction-type';
import { transferLabel } from '../../transactions/transfer-direction';
import { AccountTransferDialog } from '../account-transfer-dialog/account-transfer-dialog';
import { accountTypeLabel, taxWrapperLabel } from '../account-type';
import { CashOperationDialog } from '../cash-operation-dialog/cash-operation-dialog';

const DEFAULT_PAGE_SIZE = 20;

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-account-detail',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './account-detail.html',
  styleUrl: './account-detail.scss',
})
export class AccountDetail {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly id = input.required<string>();

  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  protected readonly accountResource = resource({
    params: () => ({ id: this.id() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAccountsById({
        path: { id: params.id },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('accounts.detail.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly dashboardResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingDashboard({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('accounts.valuesLoadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly holdingsResource = resource({
    params: () => ({ accountId: this.id() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAssets({
        query: { accountId: params.accountId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('accounts.detail.holdingsFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly historyResource = resource({
    params: () => {
      const cashAssetId = this.accountResource.hasValue()
        ? this.accountResource.value().cashAssetId
        : null;
      return cashAssetId
        ? { cashAssetId, page: this.pageIndex() + 1, pageSize: this.pageSize() }
        : undefined;
    },
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAssetsByAssetIdTransactions({
        path: { assetId: params.cashAssetId },
        query: { page: params.page, pageSize: params.pageSize },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('accounts.detail.historyFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly isArchived = computed(
    () => this.accountResource.hasValue() && this.accountResource.value().isArchived,
  );

  protected readonly totals = computed(() => {
    if (!this.accountResource.hasValue() || !this.dashboardResource.hasValue()) {
      return null;
    }
    const cash = Number(this.accountResource.value().cashBalance);
    const entry = this.dashboardResource
      .value()
      .byAccount.find((value) => value.accountId === this.id());
    if (!entry) {
      return { cash, securities: null, total: null };
    }
    const total = Number(entry.valuePln);
    return { cash, securities: Math.round((total - cash) * 100) / 100, total };
  });

  protected readonly displayedColumns = computed(() => [
    'date',
    'type',
    'amount',
    ...(this.isArchived() ? [] : ['actions']),
  ]);

  protected readonly holdingColumns = ['name', 'quantity'];

  protected readonly accountTypeLabel = accountTypeLabel;
  protected readonly taxWrapperLabel = taxWrapperLabel;
  protected readonly transactionTypeLabel = transactionTypeLabel;
  protected readonly transferLabel = transferLabel;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatDate = formatDate;

  protected asNumber(value: number | string): number {
    return Number(value);
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  protected openCashOperation(kind: 'deposit' | 'withdraw'): void {
    if (!this.accountResource.hasValue()) {
      return;
    }
    const ref = this.dialog.open(CashOperationDialog, {
      width: '480px',
      data: { cashAssetId: this.accountResource.value().cashAssetId, kind },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reload();
      }
    });
  }

  protected openTransfer(): void {
    if (!this.accountResource.hasValue()) {
      return;
    }
    const ref = this.dialog.open(AccountTransferDialog, {
      width: '480px',
      data: { account: this.accountResource.value() },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reload();
      }
    });
  }

  protected openEditDialog(transaction: TransactionResponse): void {
    if (!this.accountResource.hasValue()) {
      return;
    }
    const ref = this.dialog.open(TransactionFormDialog, {
      width: '480px',
      data: {
        assetId: this.accountResource.value().cashAssetId,
        assetClass: ASSET_CLASS.Cash,
        currency: 'PLN',
        transaction,
      },
    });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reload();
      }
    });
  }

  protected async remove(transaction: TransactionResponse): Promise<void> {
    const confirmed = await this.confirm(
      translate('transactions.delete.title'),
      translate('transactions.delete.message', {
        type: translate(transactionTypeLabel(transaction.type)),
        quantity: formatQuantity(transaction.quantity),
      }),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioAssetsByAssetIdTransactionsById({
      path: { assetId: transaction.assetId, id: transaction.id },
    });
    this.finishChange(result.error, 'transactions.delete.failed');
  }

  protected async removeTransfer(transaction: TransactionResponse): Promise<void> {
    const transfer = transaction.transfer;
    if (!transfer) {
      return;
    }

    const confirmed = await this.confirm(
      translate('transactions.deleteTransfer.title'),
      translate('transactions.deleteTransfer.message', {
        quantity: formatMoney(transaction.quantity, transaction.currency),
        counterpart: transfer.counterpartAssetName,
      }),
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioTransfersByTransferId({
      path: { transferId: transfer.transferId },
    });
    this.finishChange(result.error, 'transactions.deleteTransfer.failed');
  }

  private async confirm(title: string, message: string): Promise<boolean> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: { title, message, confirmLabel: translate('common.delete'), destructive: true },
        })
        .afterClosed(),
    );
    return !!confirmed;
  }

  private finishChange(error: unknown, failedKey: string): void {
    if (error) {
      this.snackBar.open(
        readProblemDetails(error).detail ?? translate(failedKey),
        translate('common.dismiss'),
      );
      return;
    }

    this.reload();
  }

  private reload(): void {
    this.accountResource.reload();
    this.dashboardResource.reload();
    this.holdingsResource.reload();
    this.historyResource.reload();
  }
}
