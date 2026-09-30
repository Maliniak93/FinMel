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
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioSavingsAccounts,
  type SavingsAccountResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { confirmSetAssetArchived } from '../../../shared/asset-archive';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatMoney, formatPercent } from '../../../shared/format';
import {
  SavingsAccountFormDialog,
  type SavingsAccountFormDialogData,
} from '../savings-account-form-dialog/savings-account-form-dialog';

// The "Savings accounts" tab of the Deposits & savings page (savings-accounts): every savings account
// of the user across portfolios, its balance in its own currency. Money moves in and out through the
// asset's ordinary transactions, which the account name links to.
// MatDialog/MatSnackBar are injected as services only — see assets.ts for why
// MatDialogModule/MatSnackBarModule are deliberately not in `imports`.
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

  // asset-archive: an account archived on its own is hidden until "Show archived" is on — filtered
  // here, since the list endpoint returns every account with its flag.
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

  protected transactionsLink(account: SavingsAccountResponse): string[] {
    return ['/portfolios', account.portfolioId, 'assets', account.assetId, 'transactions'];
  }

  protected openCreateDialog(): void {
    this.openDialog({});
  }

  protected openEditDialog(account: SavingsAccountResponse): void {
    this.openDialog({ account });
  }

  // A savings account is an asset: deleting it goes through the ordinary asset delete, which also
  // removes its terms and its transactions.
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

  // asset-archive: Archive / Restore sit in the row menu, behind a confirmation, and reload the list.
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
