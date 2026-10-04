import { Component, computed, inject, resource, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map, merge, of, startWith, switchMap } from 'rxjs';

import {
  postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlements,
  postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlementsPreview,
  type BondResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatDate, formatMoney, formatPercent } from '../../../shared/format';
import {
  buildSettleSection,
  isCouponBond,
  isSectionValid,
  loadCashCandidates,
  loadSeriesRates,
  settleRequest,
  usesTermsRate,
  type BondSettleSection,
} from '../bond-interest';

export interface SettleBondInterestDialogData {
  bond: BondResponse;
}

@Component({
  selector: 'app-settle-bond-interest-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './settle-bond-interest-dialog.html',
  styleUrl: './settle-bond-interest-dialog.scss',
})
export class SettleBondInterestDialog {
  private readonly dialogRef = inject(MatDialogRef<SettleBondInterestDialog>);
  protected readonly bond = inject<SettleBondInterestDialogData>(MAT_DIALOG_DATA).bond;
  private readonly coupon = isCouponBond(this.bond);

  protected readonly formatDate = formatDate;
  protected readonly formatMoney = formatMoney;
  protected readonly formatPercent = formatPercent;
  protected readonly usesTermsRate = usesTermsRate;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly seriesResource = resource({
    loader: ({ abortSignal }) => loadSeriesRates(this.bond.seriesCode, abortSignal),
  });

  protected readonly candidatesResource = resource({
    params: () => (this.coupon ? { coupon: true } : undefined),
    loader: ({ abortSignal }) => loadCashCandidates(abortSignal),
  });

  // Built once both lookups have settled, so the prefilled rates and the default Cash are in place when the form shows.
  protected readonly section = computed<BondSettleSection | null>(() => {
    if (this.seriesResource.isLoading() || this.candidatesResource.isLoading()) {
      return null;
    }
    return buildSettleSection(
      this.bond,
      this.seriesResource.hasValue() ? this.seriesResource.value() : null,
      this.candidatesResource.hasValue() ? (this.candidatesResource.value() ?? []) : [],
    );
  });

  private readonly previewRequest = toSignal(
    toObservable(this.section).pipe(
      switchMap((section) =>
        section
          ? merge(section.rates.valueChanges, section.destination.valueChanges).pipe(
              startWith(null),
              map(() => (isSectionValid(section) ? settleRequest(section) : undefined)),
            )
          : of(undefined),
      ),
    ),
    { initialValue: undefined },
  );

  protected readonly previewResource = resource({
    params: () => this.previewRequest(),
    loader: async ({ params, abortSignal }) => {
      const result =
        await postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlementsPreview({
          path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
          body: params,
          signal: abortSignal,
        });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.settle.previewFailed'),
        );
      }
      return result.data ?? null;
    },
  });

  protected async onSubmit(): Promise<void> {
    const section = this.section();
    if (this.submitting() || !section) {
      return;
    }

    if (!isSectionValid(section)) {
      section.rates.markAllAsTouched();
      section.destination.markAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const result = await postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlements({
      path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
      body: settleRequest(section),
    });

    this.submitting.set(false);

    if (result.error) {
      this.formError.set(
        readProblemDetails(result.error).detail ?? translate('bonds.settle.failed'),
      );
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}
