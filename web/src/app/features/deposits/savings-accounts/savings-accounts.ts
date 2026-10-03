import { Component, computed, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  deleteApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetIdInterestSettlementsBySettlementId,
  getApiPortfolioSavingsAccounts,
  type SavingsAccountResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { confirmSetAssetArchived } from '../../../shared/asset-archive';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatMoney, formatMonth, formatPercent } from '../../../shared/format';
import {
  SavingsAccountFormDialog,
  type SavingsAccountFormDialogData,
} from '../savings-account-form-dialog/savings-account-form-dialog';
import {
  SavingsTransferDialog,
  type SavingsTransferDialogData,
} from '../savings-transfer-dialog/savings-transfer-dialog';
import {
  SettleSavingsInterestDialog,
  type SettleSavingsInterestDialogData,
} from '../settle-savings-interest-dialog/settle-savings-interest-dialog';

@Component({
  selector: 'app-savings-accounts',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatTableModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './savings-accounts.html',
  styleUrl: './savings-accounts.scss',
})
export class SavingsAccounts {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly displayedColumns = [
    'name',
    'bankName',
    'portfolio',
    'balance',
    'rate',
    'lastInterest',
    'status',
    'actions',
  ];

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioSavingsAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('savings.loadFailed'));
      }
      return result.data ?? [];
    },
  });

  protected readonly showArchived = signal(false);

  protected readonly visibleAccounts = computed(() =>
    this.accountsResource.hasValue()
      ? this.accountsResource
          .value()
          .filter((account) => this.showArchived() || !account.isArchived)
      : [],
  );

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly formatMonth = formatMonth;

  protected isWritable(account: SavingsAccountResponse): boolean {
    return !account.portfolioIsArchived && !account.isArchived;
  }

  // The generated client types the server's int as `number | string`.
  protected monthsDue(account: SavingsAccountResponse): number {
    return Number(account.duePeriodCount);
  }

  protected openSettleDialog(account: SavingsAccountResponse): void {
    const data: SettleSavingsInterestDialogData = { account };
    const ref = this.dialog.open(SettleSavingsInterestDialog, { width: '520px', data });
    ref.afterClosed().subscribe((settled: boolean | undefined) => {
      if (settled) {
        this.accountsResource.reload();
      }
    });
  }

  protected openTransferDialog(account: SavingsAccountResponse): void {
    const data: SavingsTransferDialogData = { account };
    const ref = this.dialog.open(SavingsTransferDialog, { width: '520px', data });
    ref.afterClosed().subscribe((transferred: boolean | undefined) => {
      if (transferred) {
        this.accountsResource.reload();
      }
    });
  }

  protected async undoLastSettlement(account: SavingsAccountResponse): Promise<void> {
    const settlement = account.lastSettlement;
    if (!settlement) {
      return;
    }

    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('savings.interest.undoTitle'),
            message: translate('savings.interest.undoMessage', {
              month: formatMonth(settlement.periodEnd),
              name: account.name,
            }),
            confirmLabel: translate('savings.interest.undoConfirm'),
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result =
      await deleteApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetIdInterestSettlementsBySettlementId(
        {
          path: {
            portfolioId: account.portfolioId,
            assetId: account.assetId,
            settlementId: settlement.settlementId,
          },
        },
      );
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('savings.interest.undoFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.accountsResource.reload();
  }

  protected transactionsLink(account: SavingsAccountResponse): string[] {
    return ['/portfolios', account.portfolioId, 'assets', account.assetId, 'transactions'];
  }

  protected openCreateDialog(): void {
    this.openDialog({});
  }

  protected openEditDialog(account: SavingsAccountResponse): void {
    this.openDialog({ account });
  }

  protected async remove(account: SavingsAccountResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('savings.delete.title'),
            message: translate('savings.delete.message', { name: account.name }),
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
      path: { portfolioId: account.portfolioId, id: account.assetId },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('savings.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.accountsResource.reload();
  }

  protected async setArchived(account: SavingsAccountResponse, archive: boolean): Promise<void> {
    const done = await confirmSetAssetArchived(
      this.dialog,
      this.snackBar,
      { portfolioId: account.portfolioId, assetId: account.assetId, name: account.name },
      archive,
    );
    if (done) {
      this.accountsResource.reload();
    }
  }

  private openDialog(data: SavingsAccountFormDialogData): void {
    const ref = this.dialog.open(SavingsAccountFormDialog, { width: '560px', data });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.accountsResource.reload();
      }
    });
  }
}
