import { Component, effect, inject, resource, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdRedemptionPreview,
  postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdRedemption,
  type BondResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { formatDate, formatMoney } from '../../../shared/format';
import { loadCashCandidates } from '../bond-interest';

export interface RedeemBondDialogData {
  bond: BondResponse;
}

// 'settle' asks the opener to settle the bond's periods first.
export type RedeemBondDialogResult = boolean | 'settle';

const BOND_CURRENCY = 'PLN';

@Component({
  selector: 'app-redeem-bond-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './redeem-bond-dialog.html',
  styleUrl: './redeem-bond-dialog.scss',
})
export class RedeemBondDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef =
    inject<MatDialogRef<RedeemBondDialog, RedeemBondDialogResult>>(MatDialogRef);
  protected readonly bond = inject<RedeemBondDialogData>(MAT_DIALOG_DATA).bond;

  protected readonly unsettled = Number(this.bond.duePeriodCount) > 0;
  protected readonly currency = BOND_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    destinationAssetId: [null as string | null, [Validators.required]],
  });

  protected readonly previewResource = resource({
    params: () => (this.unsettled ? undefined : {}),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdRedemptionPreview({
        path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
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

  protected readonly candidatesResource = resource({
    params: () => (this.unsettled ? undefined : {}),
    loader: ({ abortSignal }) => loadCashCandidates(abortSignal),
  });

  constructor() {
    // The Cash the purchase was paid from is where the money most likely returns.
    effect(() => {
      if (!this.candidatesResource.hasValue()) {
        return;
      }
      const candidates = this.candidatesResource.value();
      untracked(() => {
        const destination = this.form.controls.destinationAssetId;
        if (
          destination.value === null &&
          candidates.some((candidate) => candidate.assetId === this.bond.fundingAssetId)
        ) {
          destination.setValue(this.bond.fundingAssetId ?? null);
        }
      });
    });
  }

  protected settleFirst(): void {
    this.dialogRef.close('settle');
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const result = await postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdRedemption({
      path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
      body: { destinationAssetId: this.form.getRawValue().destinationAssetId! },
    });

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(readProblemDetails(result.error));
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('bonds.redeem.failed'));
  }
}
