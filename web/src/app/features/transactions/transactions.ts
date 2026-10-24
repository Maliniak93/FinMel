import { Component, computed, inject, input, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, type PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioAssetsByAssetIdTransactionsById,
  deleteApiPortfolioTransfersByTransferId,
  getApiPortfolioAccountsById,
  getApiPortfolioAssetsById,
  getApiPortfolioAssetsByAssetIdTransactions,
  type TransactionResponse,
} from '../../api/portfolio';
import { getApiReportingDashboard } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { groupColor } from '../../shared/colors';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/empty-state/empty-state';
import {
  formatDate,
  formatMoney,
  formatMonth,
  formatPercent,
  formatQuantity,
} from '../../shared/format';
import { GroupList } from '../../shared/group-select/group-list';
import { Money } from '../../shared/money/money';
import { SectionError } from '../../shared/section-error/section-error';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { ASSET_CLASS, assetClassLabel, assetClassPage } from '../assets/asset-class';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from './transaction-form-dialog/transaction-form-dialog';
import {
  allowedTransactionTypes,
  isSecurityAssetClass,
  isSecurityTransactionType,
  TRANSACTION_TYPE_SELL,
  transactionTypeIcon,
  transactionTypeLabel,
  transactionTypeSign,
} from './transaction-type';
import { transferLabel } from './transfer-direction';

const DEFAULT_PAGE_SIZE = 20;
const MONEY_ASSET_CLASSES: readonly number[] = [
  ASSET_CLASS.Cash,
  ASSET_CLASS.Deposit,
  ASSET_CLASS.BankAccount,
];

interface KpiProfit {
  amountPln: number;
  percent: number | null;
}

function toNumber(value: number | string | null | undefined): number | null {
  return value === null || value === undefined ? null : Number(value);
}

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-transactions',
  imports: [
    EmptyState,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatPaginatorModule,
    MatTableModule,
    Money,
    RouterLink,
    SectionError,
    Skeleton,
    TranslocoPipe,
  ],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class Transactions {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly groupList = inject(GroupList);

  readonly assetId = input.required<string>();

  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  protected readonly assetResource = resource({
    params: () => ({ assetId: this.assetId() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAssetsById({
        path: { id: params.assetId },
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

  protected readonly accountResource = resource({
    params: () => {
      const accountId = this.assetResource.hasValue() ? this.assetResource.value().accountId : null;
      return accountId ? { accountId } : undefined;
    },
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAccountsById({
        path: { id: params.accountId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('transactions.accountLoadFailed'),
        );
      }
      return result.data;
    },
  });

  private readonly dashboardResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingDashboard({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('wealth.error'));
      }
      return result.data;
    },
  });

  private readonly valueLine = computed(() =>
    this.dashboardResource.hasValue()
      ? this.dashboardResource.value().byAsset.find((line) => line.assetId === this.assetId())
      : undefined,
  );

  protected readonly valuePln = computed(() => toNumber(this.valueLine()?.valuePln));

  protected readonly valueStale = computed(() => this.valueLine()?.isStale ?? false);

  private readonly isMoneyAsset = computed(
    () =>
      this.assetResource.hasValue() &&
      MONEY_ASSET_CLASSES.includes(Number(this.assetResource.value().assetClass)),
  );

  protected readonly quantityKpi = computed(() => {
    if (!this.assetResource.hasValue()) {
      return null;
    }
    const asset = this.assetResource.value();
    if (this.isMoneyAsset()) {
      return { amount: Number(asset.quantity), currency: asset.currency, text: null };
    }
    const unitCount = toNumber(asset.unitCount);
    return {
      amount: null,
      currency: asset.currency,
      text: unitCount === null ? null : formatQuantity(unitCount),
    };
  });

  protected readonly averagePricePln = computed(() =>
    this.assetResource.hasValue() ? toNumber(this.assetResource.value().averagePricePln) : null,
  );

  protected readonly dividendPln = computed(() =>
    this.assetResource.hasValue() ? toNumber(this.assetResource.value().dividendPln) : null,
  );

  protected readonly profit = computed<KpiProfit | null>(() => {
    const value = this.valuePln();
    const cost = this.assetResource.hasValue()
      ? toNumber(this.assetResource.value().costPln)
      : null;
    if (value === null || cost === null) {
      return null;
    }
    const gainGrosze = Math.round(value * 100) - Math.round(cost * 100);
    const costGrosze = Math.round(cost * 100);
    return {
      amountPln: gainGrosze / 100,
      percent: costGrosze > 0 ? Math.round((gainGrosze / costGrosze) * 10000) / 100 : null,
    };
  });

  protected readonly accountName = computed(() =>
    this.accountResource.hasValue() ? this.accountResource.value().name : null,
  );

  protected readonly group = computed(() => {
    const groupId = this.assetResource.hasValue() ? this.assetResource.value().groupId : null;
    return groupId ? this.groupList.groups().find((group) => group.id === groupId) : undefined;
  });

  protected readonly canSell = computed(
    () =>
      this.assetResource.hasValue() &&
      !this.isReadOnly() &&
      allowedTransactionTypes(this.assetResource.value().assetClass).some(
        (type) => Number(type.value) === Number(TRANSACTION_TYPE_SELL),
      ),
  );

  protected readonly isArchived = computed(
    () => this.accountResource.hasValue() && this.accountResource.value().isArchived,
  );

  protected readonly backPath = computed(() =>
    this.assetResource.hasValue()
      ? assetClassPage(this.assetResource.value().assetClass)
      : '/dashboard',
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

  protected readonly isSecurity = computed(
    () =>
      this.assetResource.hasValue() && isSecurityAssetClass(this.assetResource.value().assetClass),
  );

  private readonly isMetal = computed(
    () =>
      this.assetResource.hasValue() &&
      Number(this.assetResource.value().assetClass) === ASSET_CLASS.PreciousMetal,
  );

  protected hasEditMenu(transaction: TransactionResponse): boolean {
    if (transaction.bankInterestPeriodEnd) {
      return false;
    }
    return (
      !transaction.transfer ||
      (this.isSecurity() && isSecurityTransactionType(transaction.type)) ||
      this.isMetal()
    );
  }

  protected readonly displayedColumns = computed(() => [
    'date',
    'type',
    'quantity',
    'unitPrice',
    'currency',
    'value',
    ...(this.isSecurity() ? ['fee'] : []),
    ...(this.isReadOnly() ? [] : ['actions']),
  ]);

  protected readonly transactionsResource = resource({
    params: () => ({
      assetId: this.assetId(),
      page: this.pageIndex() + 1,
      pageSize: this.pageSize(),
    }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAssetsByAssetIdTransactions({
        path: { assetId: params.assetId },
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
  protected readonly transactionTypeIcon = transactionTypeIcon;
  protected readonly transactionTypeSign = transactionTypeSign;
  protected readonly formatPercent = formatPercent;
  protected readonly groupColor = groupColor;
  protected readonly sellType = TRANSACTION_TYPE_SELL;
  protected readonly separator = '·';
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
    const asset = this.assetResource.value();
    return {
      assetId: this.assetId(),
      assetClass: asset.assetClass,
      currency: asset.currency,
      ...(this.isSecurity() ? { accountId: asset.accountId } : {}),
    };
  }

  protected openCreateDialog(type?: TransactionFormDialogData['type']): void {
    const base = this.dialogData();
    if (!base) {
      return;
    }
    const data = type === undefined ? base : { ...base, type };
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

    const result = await deleteApiPortfolioAssetsByAssetIdTransactionsById({
      path: { assetId: this.assetId(), id: transaction.id },
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
