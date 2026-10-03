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
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  getApiPortfolioBonds,
  type BondResponse,
  type BondStatus,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney } from '../../../shared/format';
import {
  BondPurchaseDialog,
  type BondPurchaseDialogData,
} from '../bond-purchase-dialog/bond-purchase-dialog';
import { bondTypeInfo } from '../bond-types';

// Mirrors BondStatus in declaration order: the enum travels as an int.
const BOND_STATUS = { Active: 0, InterestDue: 1, Matured: 2 } as const satisfies Record<
  string,
  BondStatus
>;

const BOND_STATUS_LABELS: Record<number, string> = {
  [BOND_STATUS.Active]: 'bonds.status.active',
  [BOND_STATUS.InterestDue]: 'bonds.status.interestDue',
  [BOND_STATUS.Matured]: 'bonds.status.matured',
};

@Component({
  selector: 'app-my-bonds',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatTableModule,
    TranslocoPipe,
  ],
  templateUrl: './my-bonds.html',
  styleUrl: './my-bonds.scss',
})
export class MyBonds {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly displayedColumns = [
    'name',
    'seriesCode',
    'type',
    'portfolio',
    'purchaseDate',
    'bondCount',
    'bookValue',
    'maturityDate',
    'status',
    'actions',
  ];

  protected readonly bondsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioBonds({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.my.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly showArchived = signal(false);

  protected readonly visibleBonds = computed(() =>
    this.bondsResource.hasValue()
      ? this.bondsResource.value().filter((bond) => this.showArchived() || !bond.isArchived)
      : [],
  );

  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;

  protected typeLabel(bond: BondResponse): string {
    return bondTypeInfo(bond.type)?.label ?? '';
  }

  protected statusLabel(bond: BondResponse): string {
    return BOND_STATUS_LABELS[Number(bond.status)] ?? '';
  }

  protected needsAttention(bond: BondResponse): boolean {
    return Number(bond.status) !== BOND_STATUS.Active;
  }

  protected openCreateDialog(): void {
    this.openDialog({});
  }

  protected openEditDialog(bond: BondResponse): void {
    this.openDialog({ bond });
  }

  protected async remove(bond: BondResponse): Promise<void> {
    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('bonds.delete.title'),
            message: translate('bonds.delete.message', { name: bond.name }),
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
      path: { portfolioId: bond.portfolioId, id: bond.assetId },
    });
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('bonds.delete.failed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.bondsResource.reload();
  }

  private openDialog(data: BondPurchaseDialogData): void {
    const ref = this.dialog.open(BondPurchaseDialog, { width: '560px', data });
    ref.afterClosed().subscribe((saved: boolean | undefined) => {
      if (saved) {
        this.bondsResource.reload();
      }
    });
  }
}
