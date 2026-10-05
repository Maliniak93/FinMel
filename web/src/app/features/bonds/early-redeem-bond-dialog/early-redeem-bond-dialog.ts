import { Component, computed, effect, inject, resource, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import {
  getApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdEarlyRedemptionPreview,
  postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdEarlyRedemption,
  type BondPeriodResponse,
  type BondResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatDate, formatMoney, formatPercent } from '../../../shared/format';
import {
  BOND_PERIOD_STATE,
  isCouponBond,
  loadCashCandidates,
  loadSeriesRates,
  usesTermsRate,
} from '../bond-interest';

export interface EarlyRedeemBondDialogData {
  bond: BondResponse;
}

// 'settle' asks the opener to settle the bond's earlier periods first.
export type EarlyRedeemBondDialogResult = boolean | 'settle';

interface EarlyRedemptionQuery {
  date: string;
  bondCount: number;
  runningPeriodRatePercent?: number;
}

const BOND_CURRENCY = 'PLN';

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

function wholeNumber(control: AbstractControl): ValidationErrors | null {
  const value = control.value as number | string | null;
  return value === null || value === '' || Number.isInteger(Number(value))
    ? null
    : { wholeNumber: true };
}

function sameQuery(a: EarlyRedemptionQuery | undefined, b: EarlyRedemptionQuery | undefined) {
  return JSON.stringify(a) === JSON.stringify(b);
}

@Component({
  selector: 'app-early-redeem-bond-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './early-redeem-bond-dialog.html',
  styleUrl: './early-redeem-bond-dialog.scss',
})
export class EarlyRedeemBondDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef =
    inject<MatDialogRef<EarlyRedeemBondDialog, EarlyRedeemBondDialogResult>>(MatDialogRef);
  protected readonly bond = inject<EarlyRedeemBondDialogData>(MAT_DIALOG_DATA).bond;

  protected readonly currency = BOND_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;
  protected readonly formatPercent = formatPercent;
  protected readonly maxCount = Number(this.bond.bondCount);
  protected readonly minDate = addDays(fromDateOnly(this.bond.purchaseDate), 1);
  protected readonly maxDate = new Date(
    Math.min(today().getTime(), addDays(fromDateOnly(this.bond.maturityDate), -1).getTime()),
  );

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    date: [
      today() as Date | null,
      [
        Validators.required,
        (control: AbstractControl): ValidationErrors | null =>
          this.dateWithinRange(control.value as Date | null),
      ],
    ],
    bondCount: [
      this.maxCount as number | null,
      [Validators.required, Validators.min(1), Validators.max(this.maxCount), wholeNumber],
    ],
    ratePercent: [
      null as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    destinationAssetId: [null as string | null, [Validators.required]],
  });

  private readonly formState = toSignal(this.form.valueChanges.pipe(map(() => this.snapshot())), {
    initialValue: this.snapshot(),
  });

  protected readonly runningPeriod = computed<BondPeriodResponse | null>(() => {
    const date = this.formState().date;
    if (!date) {
      return null;
    }
    const day = toDateOnly(date);
    return this.bond.periods.find((period) => period.start <= day && day < period.end) ?? null;
  });

  protected readonly termsRate = computed(() => {
    const period = this.runningPeriod();
    return !period || usesTermsRate(this.bond, period.index);
  });

  protected readonly unsettledCount = computed(() => {
    const period = this.runningPeriod();
    if (!period) {
      return 0;
    }
    return this.bond.periods.filter(
      (earlier) =>
        Number(earlier.index) < Number(period.index) &&
        Number(earlier.state) !== BOND_PERIOD_STATE.Settled,
    ).length;
  });

  // The issue letters cap the fee at the interest, except for a coupon type past period 1, whose coupons already left.
  protected readonly feeNote = computed(() => {
    const period = this.runningPeriod();
    if (!period) {
      return null;
    }
    return isCouponBond(this.bond) && Number(period.index) >= 2
      ? 'bonds.earlyRedeem.feeFull'
      : 'bonds.earlyRedeem.feeCapped';
  });

  private readonly previewQuery = computed<EarlyRedemptionQuery | undefined>(
    () => {
      const state = this.formState();
      if (!this.runningPeriod() || !state.dateValid || !state.countValid) {
        return undefined;
      }
      if (this.unsettledCount() > 0) {
        return undefined;
      }
      const query: EarlyRedemptionQuery = {
        date: toDateOnly(state.date!),
        bondCount: Number(state.bondCount),
      };
      if (this.termsRate()) {
        return query;
      }
      return state.rateValid
        ? { ...query, runningPeriodRatePercent: Number(state.ratePercent) }
        : undefined;
    },
    { equal: sameQuery },
  );

  protected readonly seriesResource = resource({
    loader: ({ abortSignal }) => loadSeriesRates(this.bond.seriesCode, abortSignal),
  });

  protected readonly candidatesResource = resource({
    loader: ({ abortSignal }) => loadCashCandidates(abortSignal),
  });

  protected readonly previewResource = resource({
    params: () => this.previewQuery(),
    loader: async ({ params, abortSignal }) => {
      const result =
        await getApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdEarlyRedemptionPreview({
          path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
          query: params,
          signal: abortSignal,
        });
      if (result.error || !result.data) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.earlyRedeem.previewFailed'),
        );
      }
      return result.data;
    },
  });

  // A fixed rate and period 1 come from the terms; a later variable period starts from the catalog's published rate.
  private readonly ratePrefill = effect(() => {
    const period = this.runningPeriod();
    if (!period || this.seriesResource.isLoading()) {
      return;
    }
    const seriesRates = this.seriesResource.hasValue() ? this.seriesResource.value() : null;
    untracked(() => {
      const rate = this.form.controls.ratePercent;
      if (usesTermsRate(this.bond, period.index)) {
        rate.disable({ emitEvent: false });
        rate.setValue(Number(this.bond.firstPeriodRatePercent));
      } else {
        rate.enable({ emitEvent: false });
        rate.setValue(seriesRates?.get(Number(period.index)) ?? null);
      }
    });
  });

  // The Cash the purchase was paid from is where the money most likely returns.
  private readonly destinationDefault = effect(() => {
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

  protected settleFirst(): void {
    this.dialogRef.close('settle');
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    const query = this.previewQuery();
    if (this.form.invalid || !query) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const result = await postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdEarlyRedemption({
      path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
      body: { ...query, destinationAssetId: this.form.getRawValue().destinationAssetId! },
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

  private snapshot() {
    const controls = this.form.controls;
    return {
      date: controls.date.value,
      bondCount: controls.bondCount.value,
      ratePercent: controls.ratePercent.value,
      dateValid: controls.date.valid,
      countValid: controls.bondCount.valid,
      rateValid: controls.ratePercent.enabled && controls.ratePercent.valid,
    };
  }

  private dateWithinRange(date: Date | null): ValidationErrors | null {
    if (!date) {
      return null;
    }
    return date < this.minDate || date > this.maxDate ? { dateOutOfRange: true } : null;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('bonds.earlyRedeem.failed'));
  }
}
