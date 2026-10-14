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
import { firstValueFrom, type Observable } from 'rxjs';

import {
  deleteApiPortfolioAccountsById,
  deleteApiPortfolioAccountsByIdInterestSettlementsBySettlementId,
  getApiPortfolioAccounts,
  postApiPortfolioAccountsByIdArchive,
  postApiPortfolioAccountsByIdRestore,
  type AccountResponse,
  type BankVariant,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney, formatMonth, formatPercent } from '../../shared/format';
import { AccountFormDialog } from '../accounts/account-form-dialog/account-form-dialog';
import { AccountTransferDialog } from '../accounts/account-transfer-dialog/account-transfer-dialog';
import { ACCOUNT_TYPE, BANK_VARIANTS, bankVariantLabel } from '../accounts/account-type';
import { CashOperationDialog } from '../accounts/cash-operation-dialog/cash-operation-dialog';
import { SettleInterestDialog } from './settle-interest-dialog/settle-interest-dialog';

const ALL_VARIANTS = 'all';

type VariantFilter = BankVariant | typeof ALL_VARIANTS;

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-bank-accounts',
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
  templateUrl: './bank-accounts.html',
  styleUrl: './bank-accounts.scss',
})
export class BankAccounts {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly allVariants = ALL_VARIANTS;
  protected readonly variants = BANK_VARIANTS;
  protected readonly includeArchived = signal(false);
  protected readonly variantFilter = signal<VariantFilter>(ALL_VARIANTS);

  protected readonly displayedColumns = [
    'name',
    'variant',
    'bankName',
    'currency',
    'rate',
    'balance',
    'interestCredited',
    'status',
    'actions',
  ];

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({
        query: { type: ACCOUNT_TYPE.Bank },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bankAccounts.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly visibleAccounts = computed(() => {
    if (!this.accountsResource.hasValue()) {
      return [];
    }
    const variant = this.variantFilter();
    return this.accountsResource
      .value()
      .filter(
        (account) =>
          (this.includeArchived() || !account.isArchived) &&
          (variant === ALL_VARIANTS || Number(account.bank?.variant) === variant),
      );
  });

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly bankVariantLabel = bankVariantLabel;

  protected onVariantChange(value: VariantFilter | undefined): void {
    this.variantFilter.set(value ?? ALL_VARIANTS);
  }

  // The generated client types the server's int as `number | string`.
  protected monthsDue(account: AccountResponse): number {
    return Number(account.bank?.duePeriodCount ?? 0);
  }

  protected openCreateDialog(): void {
    this.openFormDialog({ type: ACCOUNT_TYPE.Bank });
  }

  protected openEditDialog(account: AccountResponse): void {
    this.openFormDialog({ account });
  }

  protected openCashOperation(account: AccountResponse, kind: 'deposit' | 'withdraw'): void {
    const ref = this.dialog.open(CashOperationDialog, {
      width: '480px',
      data: { cashAssetId: account.cashAssetId, kind },
    });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openTransferDialog(account: AccountResponse): void {
    const ref = this.dialog.open(AccountTransferDialog, { width: '480px', data: { account } });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openSettleDialog(account: AccountResponse): void {
    const ref = this.dialog.open(SettleInterestDialog, { width: '520px', data: { account } });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected async undoLastSettlement(account: AccountResponse): Promise<void> {
    const settlement = account.bank?.lastSettlement;
    if (!settlement) {
      return;
    }

    const confirmed = await this.confirm(
      'bankAccounts.interest.undoTitle',
      translate('bankAccounts.interest.undoMessage', {
        month: formatMonth(settlement.periodEnd),
        name: account.name,
      }),
      'bankAccounts.interest.undoConfirm',
      true,
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioAccountsByIdInterestSettlementsBySettlementId({
      path: { id: account.id, settlementId: settlement.settlementId },
    });
    this.finishChange(result.error, 'bankAccounts.interest.undoFailed');
  }

  protected async archive(account: AccountResponse): Promise<void> {
    const confirmed = await this.confirm(
      'accounts.archive.title',
      translate('accounts.archive.message', { name: account.name }),
      'common.archive',
    );
    if (!confirmed) {
      return;
    }

    const result = await postApiPortfolioAccountsByIdArchive({ path: { id: account.id } });
    this.finishChange(result.error, 'accounts.archive.failed');
  }

  protected async restore(account: AccountResponse): Promise<void> {
    const confirmed = await this.confirm(
      'accounts.restore.title',
      translate('accounts.restore.message', { name: account.name }),
      'common.restore',
    );
    if (!confirmed) {
      return;
    }

    const result = await postApiPortfolioAccountsByIdRestore({ path: { id: account.id } });
    this.finishChange(result.error, 'accounts.restore.failed');
  }

  protected async remove(account: AccountResponse): Promise<void> {
    const confirmed = await this.confirm(
      'accounts.delete.title',
      translate('accounts.delete.message', { name: account.name }),
      'common.delete',
      true,
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioAccountsById({ path: { id: account.id } });
    this.finishChange(result.error, 'accounts.delete.failed');
  }

  private openFormDialog(data: { account?: AccountResponse; type?: number }): void {
    const ref = this.dialog.open(AccountFormDialog, { width: '480px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  private reloadWhenSaved(closed: Observable<boolean | undefined>): void {
    closed.subscribe((saved) => {
      if (saved) {
        this.accountsResource.reload();
      }
    });
  }

  private async confirm(
    titleKey: string,
    message: string,
    confirmLabelKey: string,
    destructive = false,
  ): Promise<boolean> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate(titleKey),
            message,
            confirmLabel: translate(confirmLabelKey),
            destructive,
          },
        })
        .afterClosed(),
    );
    return !!confirmed;
  }

  private finishChange(error: unknown, failedKey: string): void {
    if (error) {
      const problem = readProblemDetails(error);
      const fallback = translate(failedKey);
      const message =
        problem.errorCode === 'Conflict.AccountHasOpenDeposit'
          ? translate('bankAccounts.hasOpenDeposit')
          : (problem.detail ?? fallback);
      this.snackBar.open(message, translate('common.dismiss'));
      return;
    }

    this.accountsResource.reload();
  }
}
