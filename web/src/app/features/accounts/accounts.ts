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
import { firstValueFrom } from 'rxjs';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  deleteApiPortfolioAccountsById,
  getApiPortfolioAccounts,
  postApiPortfolioAccountsByIdArchive,
  postApiPortfolioAccountsByIdRestore,
  type AccountResponse,
} from '../../api/portfolio';
import { getApiReportingDashboard, type DashboardAccountValue } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatMoney } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { accountTypeLabel, taxWrapperLabel } from './account-type';
import { AccountFormDialog } from './account-form-dialog/account-form-dialog';

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-accounts',
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
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly includeArchived = signal(false);

  protected readonly displayedColumns = computed(() =>
    this.includeArchived()
      ? ['name', 'type', 'wrapper', 'cash', 'securities', 'value', 'status', 'actions']
      : ['name', 'type', 'wrapper', 'cash', 'securities', 'value', 'actions'],
  );

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('accounts.loadFailed'),
        );
      }
      return result.data ?? [];
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

  protected readonly visibleAccounts = computed(() =>
    this.accountsResource.hasValue()
      ? this.accountsResource
          .value()
          .filter((account) => this.includeArchived() || !account.isArchived)
      : [],
  );

  protected readonly accountValues = computed(() => {
    const map = new Map<string, DashboardAccountValue>();
    if (this.dashboardResource.hasValue()) {
      for (const entry of this.dashboardResource.value().byAccount) {
        if (entry.accountId) {
          map.set(entry.accountId, entry);
        }
      }
    }
    return map;
  });

  protected readonly formatMoney = formatMoney;
  protected readonly accountTypeLabel = accountTypeLabel;
  protected readonly taxWrapperLabel = taxWrapperLabel;

  protected valueEntryFor(accountId: string): DashboardAccountValue | undefined {
    return this.accountValues().get(accountId);
  }

  protected securitiesOf(account: AccountResponse): number | null {
    const entry = this.valueEntryFor(account.id);
    return entry
      ? Math.round((Number(entry.valuePln) - Number(account.cashBalance)) * 100) / 100
      : null;
  }

  protected openCreateDialog(): void {
    this.openFormDialog({});
  }

  protected openEditDialog(account: AccountResponse): void {
    this.openFormDialog({ account });
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

  private openFormDialog(data: { account?: AccountResponse }): void {
    const ref = this.dialog.open(AccountFormDialog, { width: '480px', data });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.reloadAfterChange();
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
      this.snackBar.open(
        readProblemDetails(error).detail ?? translate(failedKey),
        translate('common.dismiss'),
      );
      return;
    }

    this.reloadAfterChange();
  }

  private reloadAfterChange(): void {
    this.accountsResource.reload();
    this.dashboardResource.reload();
  }
}
