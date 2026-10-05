import { Component, computed, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, type MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  deleteApiPortfolioPortfoliosByPortfolioIdAssetsById,
  deleteApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlementsBySettlementId,
  getApiPortfolioBonds,
  type BondEstimateUnavailableReason,
  type BondPeriodResponse,
  type BondResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney, formatPercent } from '../../../shared/format';
import {
  BondPurchaseDialog,
  type BondPurchaseDialogData,
} from '../bond-purchase-dialog/bond-purchase-dialog';
import {
  BOND_PERIOD_STATE,
  BOND_STATUS,
  canEarlyRedeem,
  canRedeem,
  canSettle,
  canSwap,
  isPartiallyRedeemed,
  isRedeemed,
  usesTermsRate,
} from '../bond-interest';
import { bondTypeInfo } from '../bond-types';
import {
  EarlyRedeemBondDialog,
  type EarlyRedeemBondDialogData,
  type EarlyRedeemBondDialogResult,
} from '../early-redeem-bond-dialog/early-redeem-bond-dialog';
import {
  RedeemBondDialog,
  type RedeemBondDialogData,
  type RedeemBondDialogResult,
} from '../redeem-bond-dialog/redeem-bond-dialog';
import {
  SettleAllBondsDialog,
  type SettleAllBondsDialogData,
} from '../settle-all-bonds-dialog/settle-all-bonds-dialog';
import {
  SettleBondInterestDialog,
  type SettleBondInterestDialogData,
} from '../settle-bond-interest-dialog/settle-bond-interest-dialog';
import { SwapBondDialog, type SwapBondDialogData } from '../swap-bond-dialog/swap-bond-dialog';

const BOND_STATUS_LABELS: Record<number, string> = {
  [BOND_STATUS.Active]: 'bonds.status.active',
  [BOND_STATUS.InterestDue]: 'bonds.status.interestDue',
  [BOND_STATUS.Matured]: 'bonds.status.matured',
  [BOND_STATUS.Redeemed]: 'bonds.status.redeemed',
};

const BOND_PERIOD_STATE_LABELS: Record<number, string> = {
  [BOND_PERIOD_STATE.Upcoming]: 'bonds.periods.state.upcoming',
  [BOND_PERIOD_STATE.Due]: 'bonds.periods.state.due',
  [BOND_PERIOD_STATE.Settled]: 'bonds.periods.state.settled',
};

// Mirrors BondEstimateUnavailableReason in declaration order: the enum travels as an int.
const ESTIMATE_UNAVAILABLE_LABELS = {
  0: 'bonds.my.estimateUnavailable.rateMissing',
  1: 'bonds.my.estimateUnavailable.marketDataUnavailable',
} as const satisfies Record<BondEstimateUnavailableReason, string>;

// Display-only sum of the server's amounts, in whole grosze.
function sumInGrosze(amounts: readonly (number | string)[]): number {
  return amounts.reduce<number>((sum, amount) => sum + Math.round(Number(amount) * 100), 0) / 100;
}

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
    MatTooltipModule,
    TranslocoPipe,
  ],
  templateUrl: './my-bonds.html',
  styleUrl: './my-bonds.scss',
})
export class MyBonds {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly displayedColumns = [
    'expand',
    'name',
    'seriesCode',
    'type',
    'portfolio',
    'purchaseDate',
    'bondCount',
    'bookValue',
    'grossToday',
    'netToday',
    'maturityDate',
    'status',
    'actions',
  ];

  protected readonly footerColumns = [
    'totalLabel',
    'grossTodayTotal',
    'netTodayTotal',
    'totalRest',
  ];
  protected readonly totalLabelSpan = this.displayedColumns.indexOf('grossToday');
  protected readonly totalRestSpan =
    this.displayedColumns.length - this.displayedColumns.indexOf('netToday') - 1;

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
  protected readonly showRedeemed = signal(false);

  protected readonly hasRedeemed = computed(
    () => this.bondsResource.hasValue() && this.bondsResource.value().some(isRedeemed),
  );

  protected readonly visibleBonds = computed(() =>
    this.bondsResource.hasValue()
      ? this.bondsResource
          .value()
          .filter(
            (bond) =>
              (this.showArchived() || !bond.isArchived) &&
              (this.showRedeemed() || !isRedeemed(bond)),
          )
      : [],
  );

  protected readonly estimateTotals = computed(() => {
    const estimates = this.visibleBonds().flatMap((bond) => (bond.estimate ? [bond.estimate] : []));
    return estimates.length === 0
      ? null
      : {
          gross: sumInGrosze(estimates.map((estimate) => estimate.grossValue)),
          net: sumInGrosze(estimates.map((estimate) => estimate.netValue)),
        };
  });

  protected readonly expandedId = signal<string | null>(null);

  // A fresh array on every expand/collapse makes the table re-evaluate which bond gets its period row.
  protected readonly tableRows = computed(() => {
    this.expandedId();
    return [...this.visibleBonds()];
  });

  protected readonly isExpanded = (_: number, bond: BondResponse): boolean =>
    this.expandedId() === bond.assetId;

  protected readonly settleableBonds = computed(() =>
    this.bondsResource.hasValue() ? this.bondsResource.value().filter(canSettle) : [],
  );

  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;
  protected readonly formatPercent = formatPercent;
  protected readonly canSettle = canSettle;
  protected readonly canEarlyRedeem = canEarlyRedeem;
  protected readonly canRedeem = canRedeem;
  protected readonly canSwap = canSwap;
  protected readonly isPartiallyRedeemed = isPartiallyRedeemed;

  protected typeLabel(bond: BondResponse): string {
    return bondTypeInfo(bond.type)?.label ?? '';
  }

  protected estimateUnavailableLabel(bond: BondResponse): string | null {
    const reason = bond.estimateUnavailableReason;
    return reason === null || reason === undefined
      ? null
      : (ESTIMATE_UNAVAILABLE_LABELS[Number(reason) as keyof typeof ESTIMATE_UNAVAILABLE_LABELS] ??
          null);
  }

  protected statusLabel(bond: BondResponse): string {
    return BOND_STATUS_LABELS[Number(bond.status)] ?? '';
  }

  protected needsAttention(bond: BondResponse): boolean {
    return Number(bond.status) !== BOND_STATUS.Active && !isRedeemed(bond);
  }

  protected periodStateLabel(period: BondPeriodResponse): string {
    return BOND_PERIOD_STATE_LABELS[Number(period.state)] ?? '';
  }

  protected periodRate(bond: BondResponse, period: BondPeriodResponse): number | string | null {
    if (period.settlement) {
      return period.settlement.ratePercent;
    }
    return usesTermsRate(bond, period.index) ? bond.firstPeriodRatePercent : null;
  }

  protected canUndo(bond: BondResponse, period: BondPeriodResponse): boolean {
    return (
      !!period.settlement &&
      period.settlement.settlementId === bond.lastSettlement?.settlementId &&
      !bond.isArchived &&
      !bond.portfolioIsArchived
    );
  }

  protected toggleExpanded(bond: BondResponse): void {
    this.expandedId.update((current) => (current === bond.assetId ? null : bond.assetId));
  }

  protected openSettleDialog(bond: BondResponse): void {
    const data: SettleBondInterestDialogData = { bond };
    this.reloadWhenSaved(this.dialog.open(SettleBondInterestDialog, { width: '640px', data }));
  }

  protected openRedeemDialog(bond: BondResponse): void {
    const data: RedeemBondDialogData = { bond };
    this.dialog
      .open<RedeemBondDialog, RedeemBondDialogData, RedeemBondDialogResult>(RedeemBondDialog, {
        width: '560px',
        data,
      })
      .afterClosed()
      .subscribe((result) => {
        if (result === 'settle') {
          this.openSettleDialog(bond);
        } else if (result) {
          this.bondsResource.reload();
        }
      });
  }

  protected openEarlyRedeemDialog(bond: BondResponse): void {
    const data: EarlyRedeemBondDialogData = { bond };
    this.dialog
      .open<EarlyRedeemBondDialog, EarlyRedeemBondDialogData, EarlyRedeemBondDialogResult>(
        EarlyRedeemBondDialog,
        { width: '640px', data },
      )
      .afterClosed()
      .subscribe((result) => {
        if (result === 'settle') {
          this.openSettleDialog(bond);
        } else if (result) {
          this.bondsResource.reload();
        }
      });
  }

  protected openSwapDialog(bond: BondResponse): void {
    const data: SwapBondDialogData = { bond };
    this.reloadWhenSaved(this.dialog.open(SwapBondDialog, { width: '640px', data }));
  }

  protected openSettleAllDialog(): void {
    const data: SettleAllBondsDialogData = { bonds: this.settleableBonds() };
    this.reloadWhenSaved(this.dialog.open(SettleAllBondsDialog, { width: '720px', data }));
  }

  protected async undoLastSettlement(
    bond: BondResponse,
    period: BondPeriodResponse,
  ): Promise<void> {
    const settlement = bond.lastSettlement;
    if (!settlement) {
      return;
    }

    const confirmed = await firstValueFrom(
      this.dialog
        .open(ConfirmDialog, {
          data: {
            title: translate('bonds.settle.undoTitle'),
            message: translate('bonds.settle.undoMessage', {
              index: period.index,
              name: bond.name,
            }),
            confirmLabel: translate('bonds.settle.undo'),
            destructive: true,
          },
        })
        .afterClosed(),
    );

    if (!confirmed) {
      return;
    }

    const result =
      await deleteApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlementsBySettlementId(
        {
          path: {
            portfolioId: bond.portfolioId,
            assetId: bond.assetId,
            settlementId: settlement.settlementId,
          },
        },
      );
    if (result.error) {
      this.snackBar.open(
        readProblemDetails(result.error).detail ?? translate('bonds.settle.undoFailed'),
        translate('common.dismiss'),
      );
      return;
    }

    this.bondsResource.reload();
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
    this.reloadWhenSaved(this.dialog.open(BondPurchaseDialog, { width: '560px', data }));
  }

  private reloadWhenSaved(ref: MatDialogRef<unknown, boolean | undefined>): void {
    ref.afterClosed().subscribe((saved) => {
      if (saved) {
        this.bondsResource.reload();
      }
    });
  }
}
