import { Component, inject, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioBondsByAssetIdRedemptionPreview,
  postApiPortfolioBondsByAssetIdRedemption,
  type BondResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatDate, formatMoney, formatQuantity } from '../../../shared/format';

export interface RedeemBondDialogData {
  bond: BondResponse;
}

// 'settle' asks the opener to settle the bond's periods first.
export type RedeemBondDialogResult = boolean | 'settle';

const BOND_CURRENCY = 'PLN';

@Component({
  selector: 'app-redeem-bond-dialog',
  imports: [MatButtonModule, MatDialogModule, MatProgressSpinnerModule, TranslocoPipe],
  templateUrl: './redeem-bond-dialog.html',
  styleUrl: './redeem-bond-dialog.scss',
})
export class RedeemBondDialog {
  private readonly dialogRef =
    inject<MatDialogRef<RedeemBondDialog, RedeemBondDialogResult>>(MatDialogRef);
  protected readonly bond = inject<RedeemBondDialogData>(MAT_DIALOG_DATA).bond;

  protected readonly unsettled = Number(this.bond.duePeriodCount) > 0;
  protected readonly currency = BOND_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatDate = formatDate;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly previewResource = resource({
    params: () => (this.unsettled ? undefined : {}),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioBondsByAssetIdRedemptionPreview({
        path: { assetId: this.bond.assetId },
        signal: abortSignal,
      });
      if (result.error || !result.data) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.redeem.previewFailed'),
        );
      }
      return result.data;
    },
  });

  protected settleFirst(): void {
    this.dialogRef.close('settle');
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const result = await postApiPortfolioBondsByAssetIdRedemption({
      path: { assetId: this.bond.assetId },
      body: {},
    });

    this.submitting.set(false);

    if (result.error) {
      this.formError.set(
        readProblemDetails(result.error).detail ?? translate('bonds.redeem.failed'),
      );
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}
