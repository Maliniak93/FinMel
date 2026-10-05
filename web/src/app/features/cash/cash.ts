import { Component, inject, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioCashAccounts,
  type CashAccountResponse,
  type TransactionType,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatMoney } from '../../shared/format';
import { ASSET_CLASS } from '../assets/asset-class';
import {
  AssetFormDialog,
  type AssetFormDialogData,
} from '../assets/asset-form/asset-form-dialog/asset-form-dialog';
import {
  SavingsTransferDialog,
  type SavingsTransferDialogData,
} from '../deposits/savings-transfer-dialog/savings-transfer-dialog';
import {
  TransactionFormDialog,
  type TransactionFormDialogData,
} from '../transactions/transaction-form-dialog/transaction-form-dialog';
import {
  TRANSACTION_TYPE_DEPOSIT,
  TRANSACTION_TYPE_WITHDRAW,
} from '../transactions/transaction-type';

@Component({
  selector: 'app-cash',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTableModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './cash.html',
  styleUrl: './cash.scss',
})
export class Cash {
  private readonly dialog = inject(MatDialog);

  protected readonly displayedColumns = ['name', 'portfolio', 'currency', 'balance', 'actions'];

  protected readonly cashResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioCashAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('cash.loadFailed'));
      }
      return result.data ?? { accounts: [], totals: [] };
    },
  });

  protected readonly formatMoney = formatMoney;
  protected readonly depositType = TRANSACTION_TYPE_DEPOSIT;
  protected readonly withdrawType = TRANSACTION_TYPE_WITHDRAW;

  protected transactionsLink(account: CashAccountResponse): string[] {
    return ['/portfolios', account.portfolioId, 'assets', account.assetId, 'transactions'];
  }

  protected openAddDialog(): void {
    const data: AssetFormDialogData = { assetClass: ASSET_CLASS.Cash };
    this.reloadWhenDone(this.dialog.open(AssetFormDialog, { width: '560px', data }));
  }

  protected openTransactionDialog(account: CashAccountResponse, type: TransactionType): void {
    const data: TransactionFormDialogData = {
      portfolioId: account.portfolioId,
      assetId: account.assetId,
      assetClass: ASSET_CLASS.Cash,
      type,
    };
    this.reloadWhenDone(this.dialog.open(TransactionFormDialog, { width: '480px', data }));
  }

  protected openTransferDialog(account: CashAccountResponse): void {
    const data: SavingsTransferDialogData = { cash: account };
    this.reloadWhenDone(this.dialog.open(SavingsTransferDialog, { width: '520px', data }));
  }

  private reloadWhenDone(ref: MatDialogRef<unknown, boolean>): void {
    ref.afterClosed().subscribe((done) => {
      if (done) {
        this.cashResource.reload();
      }
    });
  }
}
