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
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioDeposits,
  type DepositResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney, formatPercent } from '../../shared/format';
import {
  DepositFormDialog,
  type DepositFormDialogData,
} from './deposit-form-dialog/deposit-form-dialog';
import { DEPOSIT_STATUS, depositStatusLabel, settlementAmounts } from './deposit-terms';
import {
  PayOutDepositDialog,
  type PayOutDepositDialogData,
} from './pay-out-deposit-dialog/pay-out-deposit-dialog';
import {
  RollOverDepositDialog,
  type RollOverDepositDialogData,
} from './roll-over-deposit-dialog/roll-over-deposit-dialog';
import {
  SettleDepositDialog,
  type SettleDepositDialogData,
} from './settle-deposit-dialog/settle-deposit-dialog';

// Every term deposit of the user across portfolios, with the server's projection and Active / Due /
// Settled / Paid out status (term-deposits, term-deposits-settlement, deposit-payout-to-cash,
// deposit-rollover).
// MatDialog/MatSnackBar are injected as
// services only — see assets.ts for why MatDialogModule/MatSnackBarModule are deliberately not in
// `imports`.
@Component({
  selector: 'app-deposits',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    TranslocoPipe,
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
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('deposits.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly formatDate = formatDate;
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

  protected isPaidOut(deposit: DepositResponse): boolean {
    return Number(deposit.status) === DEPOSIT_STATUS.PaidOut;
  }

  // deposit-rollover: a Due or a Settled (not paid-out) deposit can start its next term.
  protected canRollOver(deposit: DepositResponse): boolean {
    return this.isDue(deposit) || this.isSettled(deposit);
  }

  // Settled or paid out: the terms are immutable and the row shows what the bank actually paid.
  protected hasSettlement(deposit: DepositResponse): boolean {
    return this.isSettled(deposit) || this.isPaidOut(deposit);
  }

  // A Settled or PaidOut row shows what the bank actually paid; any other row the server's projection.
  protected netInterest(deposit: DepositResponse): number | string {
    return this.hasSettlement(deposit)
      ? this.settledAmounts(deposit).netInterest
      : deposit.projection.netInterest;
  }

  protected finalAmount(deposit: DepositResponse): number | string {
    return this.hasSettlement(deposit)
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
            title: translate('deposits.delete.title'),
            message: translate('deposits.delete.message', { name: deposit.name }),
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
      path: { portfolioId: deposit.portfolioId, id: deposit.assetId },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('deposits.delete.failed'),
        translate('common.dismiss'),
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

  protected openPayOutDialog(deposit: DepositResponse): void {
    const data: PayOutDepositDialogData = { deposit };
    const ref = this.dialog.open(PayOutDepositDialog, { width: '480px', data });
    ref.afterClosed().subscribe((paidOut: boolean | undefined) => {
      if (paidOut) {
        this.depositsResource.reload();
      }
    });
  }

  protected openRollOverDialog(deposit: DepositResponse): void {
    const data: RollOverDepositDialogData = { deposit };
    const ref = this.dialog.open(RollOverDepositDialog, { width: '480px', data });
    ref.afterClosed().subscribe((rolledOver: boolean | undefined) => {
      if (rolledOver) {
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
