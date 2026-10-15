import { Component, computed, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioCashPlacesById,
  getApiPortfolioCashPlaces,
  postApiPortfolioCashPlacesByIdArchive,
  postApiPortfolioCashPlacesByIdRestore,
  type CashBalanceResponse,
  type CashPlaceResponse,
} from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { formatMoney } from '../../shared/format';
import { CashOperationDialog } from '../accounts/cash-operation-dialog/cash-operation-dialog';
import {
  CashBankTransferDialog,
  type CashBankTransferDirection,
} from './cash-bank-transfer-dialog/cash-bank-transfer-dialog';
import {
  CashPlaceFormDialog,
  type CashPlaceFormDialogData,
} from './cash-place-form-dialog/cash-place-form-dialog';

const CONFLICT_MESSAGES: Record<string, string> = {
  'Conflict.CashPlaceNotEmpty': 'cash.errors.notEmpty',
  'Conflict.CashPlaceHasHistory': 'cash.errors.hasHistory',
  'Conflict.CashPlaceArchived': 'cash.errors.archived',
  'Conflict.DuplicateName': 'cash.errors.duplicateName',
  'Conflict.DuplicateCurrency': 'cash.errors.duplicateCurrency',
};

// MatDialogModule/MatSnackBarModule stay out of imports: they would shadow the TestBed provider override.
@Component({
  selector: 'app-cash',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './cash.html',
  styleUrl: './cash.scss',
})
export class Cash {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly includeArchived = signal(false);

  protected readonly placesResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioCashPlaces({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('cash.loadFailed'));
      }
      return result.data;
    },
  });

  protected readonly visiblePlaces = computed(() =>
    this.placesResource.hasValue()
      ? this.placesResource
          .value()
          .places.filter((place) => this.includeArchived() || !place.isArchived)
      : [],
  );

  protected readonly formatMoney = formatMoney;

  protected transactionsLink(balance: CashBalanceResponse): string[] {
    return ['/assets', balance.cashAssetId, 'transactions'];
  }

  protected openCreateDialog(): void {
    this.openFormDialog({ mode: 'create' });
  }

  protected openRenameDialog(place: CashPlaceResponse): void {
    this.openFormDialog({ mode: 'rename', place });
  }

  protected openAddCurrencyDialog(place: CashPlaceResponse): void {
    this.openFormDialog({ mode: 'addCurrency', place });
  }

  protected openOperationDialog(balance: CashBalanceResponse, kind: 'deposit' | 'withdraw'): void {
    const ref = this.dialog.open(CashOperationDialog, {
      width: '480px',
      data: { cashAssetId: balance.cashAssetId, kind },
    });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected openBankTransferDialog(
    place: CashPlaceResponse,
    balance: CashBalanceResponse,
    direction: CashBankTransferDirection,
  ): void {
    const ref = this.dialog.open(CashBankTransferDialog, {
      width: '480px',
      data: {
        direction,
        anchor: 'place',
        anchorAssetId: balance.cashAssetId,
        anchorName: `${place.name} · ${balance.currency}`,
        currency: balance.currency,
        anchorBalance: balance.balance,
      },
    });
    this.reloadWhenSaved(ref.afterClosed());
  }

  protected async archive(place: CashPlaceResponse): Promise<void> {
    const confirmed = await this.confirm(
      'cash.archive.title',
      translate('cash.archive.message', { name: place.name }),
      'common.archive',
    );
    if (!confirmed) {
      return;
    }

    const result = await postApiPortfolioCashPlacesByIdArchive({ path: { id: place.id } });
    this.finishChange(result.error, 'cash.archive.failed');
  }

  protected async restore(place: CashPlaceResponse): Promise<void> {
    const confirmed = await this.confirm(
      'cash.restore.title',
      translate('cash.restore.message', { name: place.name }),
      'common.restore',
    );
    if (!confirmed) {
      return;
    }

    const result = await postApiPortfolioCashPlacesByIdRestore({ path: { id: place.id } });
    this.finishChange(result.error, 'cash.restore.failed');
  }

  protected async remove(place: CashPlaceResponse): Promise<void> {
    const confirmed = await this.confirm(
      'cash.delete.title',
      translate('cash.delete.message', { name: place.name }),
      'common.delete',
      true,
    );
    if (!confirmed) {
      return;
    }

    const result = await deleteApiPortfolioCashPlacesById({ path: { id: place.id } });
    this.finishChange(result.error, 'cash.delete.failed');
  }

  private openFormDialog(data: CashPlaceFormDialogData): void {
    const ref = this.dialog.open(CashPlaceFormDialog, { width: '480px', data });
    this.reloadWhenSaved(ref.afterClosed());
  }

  private reloadWhenSaved(closed: {
    subscribe(next: (saved: boolean | undefined) => void): unknown;
  }): void {
    closed.subscribe((saved) => {
      if (saved) {
        this.placesResource.reload();
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
      const conflictKey = problem.errorCode ? CONFLICT_MESSAGES[problem.errorCode] : undefined;
      this.snackBar.open(
        conflictKey ? translate(conflictKey) : (problem.detail ?? translate(failedKey)),
        translate('common.dismiss'),
      );
      return;
    }

    this.placesResource.reload();
  }
}
