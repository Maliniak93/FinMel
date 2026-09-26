import { DatePipe } from '@angular/common';
import { Component, inject, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioDeposits,
  type DepositResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format-money';
import {
  DepositFormDialog,
  type DepositFormDialogData,
} from './deposit-form-dialog/deposit-form-dialog';
import {
  DEPOSIT_STATUS,
  depositStatusLabel,
  formatPercent,
  settlementAmounts,
} from './deposit-terms';
import {
  SettleDepositDialog,
  type SettleDepositDialogData,
} from './settle-deposit-dialog/settle-deposit-dialog';

// Every term deposit of the user across portfolios, with the server's projection and Active / Due /
// Settled status (term-deposits, term-deposits-settlement). MatDialog/MatSnackBar are injected as
// services only — see assets.ts for why MatDialogModule/MatSnackBarModule are deliberately not in
// `imports`.
@Component({
  selector: 'app-deposits',
  imports: [
    DatePipe,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
  ],
  templateUrl: './deposits.html',
  styleUrl: './deposits.scss',
})
export class Deposits {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly displayedColumns = [
    'name',
    'bankName',
    'portfolio',
    'principal',
    'rate',
    'startDate',
    'maturityDate',
    'netInterest',
    'finalAmount',
    'status',
    'actions',
  ];

  protected readonly depositsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioDeposits({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load deposits.');
      }
      return result.data ?? [];
    },
  });

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly depositStatusLabel = depositStatusLabel;

  protected isDue(deposit: DepositResponse): boolean {
    return Number(deposit.status) === DEPOSIT_STATUS.Due;
  }

  protected nameCellId(deposit: DepositResponse): string {
    return `deposit-${deposit.assetId}-name`;
  }

  protected isSettled(deposit: DepositResponse): boolean {
    return Number(deposit.status) === DEPOSIT_STATUS.Settled;
  }

  // A Settled row shows what the bank actually paid; any other row the server's projection.
  protected netInterest(deposit: DepositResponse): number | string {
    return this.isSettled(deposit)
      ? this.settledAmounts(deposit).netInterest
      : deposit.projection.netInterest;
  }

  protected finalAmount(deposit: DepositResponse): number | string {
    return this.isSettled(deposit)
      ? this.settledAmounts(deposit).finalAmount
      : deposit.projection.finalAmount;
  }

  protected openCreateDialog(): void {
    this.openDialog({});
  }

  protected openEditDialog(deposit: DepositResponse): void {
    this.openDialog({ deposit });
  }

  // A deposit is an asset: deleting it goes through the ordinary asset delete, which also removes its
  // terms and its opening transaction.
  protected async remove(deposit: DepositResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: 'Delete this deposit?',
            message: `"${deposit.name}" and its terms will be permanently deleted. This can't be undone.`,
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
      path: { portfolioId: deposit.portfolioId, id: deposit.assetId },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? 'Failed to delete deposit.',
        'Dismiss',
      );
      return;
    }

    this.depositsResource.reload();
  }

  protected openSettleDialog(deposit: DepositResponse): void {
    const data: SettleDepositDialogData = { deposit };
    const ref = this.dialog.open(SettleDepositDialog, { width: '480px', data });
    ref.afterClosed().subscribe((settled: boolean | undefined) => {
      if (settled) {
        this.depositsResource.reload();
      }
    });
  }

  private settledAmounts(deposit: DepositResponse): { netInterest: number; finalAmount: number } {
    return settlementAmounts(
      deposit.principal,
      deposit.settledGrossInterest ?? 0,
      deposit.settledTax ?? 0,
    );
  }

  private openDialog(data: DepositFormDialogData): void {
    const ref = this.dialog.open(DepositFormDialog, { width: '560px', data });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.depositsResource.reload();
      }
    });
  }
}
