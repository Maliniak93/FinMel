import { Component, computed, effect, inject, resource, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
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
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import { getApiMarketdataBondSeries, type BondSeriesListItem } from '../../../api/marketdata';
import {
  getApiPortfolioPortfolios,
  getApiPortfolioTransferCandidates,
  postApiPortfolioPortfoliosByPortfolioIdBonds,
  putApiPortfolioPortfoliosByPortfolioIdBondsByAssetId,
  type BondResponse,
  type TransferCandidateResponse,
  type TreasuryBondType,
  type UpdateBondRequest,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';
import { BOND_TYPES, bondTypeInfo } from '../bond-types';

export interface BondPurchaseDialogData {
  portfolioId?: string;
  bond?: BondResponse;
  seriesCode?: string;
}

const BOND_CURRENCY = 'PLN';

// The series picker's "enter manually" entry; never a real series code, which is always 3 letters + 4 digits.
const MANUAL_SERIES = 'manual';

function emptyToNull(value: unknown): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

@Component({
  selector: 'app-bond-purchase-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslocoPipe,
  ],
  templateUrl: './bond-purchase-dialog.html',
  styleUrl: './bond-purchase-dialog.scss',
})
export class BondPurchaseDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<BondPurchaseDialog>);
  protected readonly data = inject<BondPurchaseDialogData>(MAT_DIALOG_DATA);

  private readonly bond = this.data.bond;
  protected readonly isEdit = !!this.bond;
  protected readonly choosesPortfolio = !this.bond && !this.data.portfolioId;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly manualSeries = MANUAL_SERIES;
  protected readonly bondTypes = BOND_TYPES;
  protected readonly currency = BOND_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly today = new Date();

  private pendingSeriesCode = this.data.seriesCode ?? null;
  private prefilledName: string | null = null;

  private readonly costWithinBalance = (control: AbstractControl): ValidationErrors | null => {
    const fundingAssetId = control.value as string | null;
    if (!fundingAssetId) {
      return null;
    }
    const source = this.fundingSource(fundingAssetId);
    const costInGrosze = this.costInGrosze(control.parent?.getRawValue());
    if (!source || costInGrosze === null) {
      return null;
    }
    return costInGrosze > Math.round(Number(source.balance) * 100)
      ? { exceedsBalance: { balance: source.balance } }
      : null;
  };

  protected readonly form = this.formBuilder.group({
    series: [null as string | null],
    portfolioId: this.formBuilder.nonNullable.control(
      { value: this.data.portfolioId ?? '', disabled: this.isEdit },
      [Validators.required],
    ),
    name: this.formBuilder.nonNullable.control(this.bond?.name ?? '', [
      Validators.required,
      Validators.maxLength(200),
    ]),
    seriesCode: this.formBuilder.nonNullable.control(this.bond?.seriesCode ?? '', [
      Validators.required,
      Validators.pattern(/^[A-Za-z]{3}\d{4}$/),
    ]),
    type: [
      (this.bond ? Number(this.bond.type) : null) as TreasuryBondType | null,
      [Validators.required],
    ],
    purchaseDate: [
      (this.bond ? fromDateOnly(this.bond.purchaseDate) : new Date()) as Date | null,
      [Validators.required],
    ],
    bondCount: [
      (this.bond ? Number(this.bond.bondCount) : null) as number | null,
      [Validators.required, Validators.min(1)],
    ],
    purchasePricePerBond: [
      (this.bond ? Number(this.bond.purchasePricePerBond) : null) as number | null,
      [Validators.required, Validators.min(0.01), Validators.max(100)],
    ],
    firstPeriodRatePercent: [
      (this.bond ? Number(this.bond.firstPeriodRatePercent) : null) as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    marginPercent: [
      emptyToNull(this.bond?.marginPercent),
      [Validators.min(0), Validators.max(100)],
    ],
    earlyRedemptionFeePerBond: [
      (this.bond ? Number(this.bond.earlyRedemptionFeePerBond) : null) as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    taxExempt: this.formBuilder.nonNullable.control(this.bond?.taxExempt ?? false),
    fundingAssetId: [
      { value: null as string | null, disabled: this.isEdit },
      [this.costWithinBalance],
    ],
  });

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly cost = computed(() => {
    const grosze = this.costInGrosze(this.formValue());
    return grosze === null ? null : grosze / 100;
  });

  private readonly purchaseDate = computed(() => {
    const date = this.formValue().purchaseDate;
    return date ? toDateOnly(date) : null;
  });

  protected readonly offerResource = resource({
    params: () => {
      const onSaleOn = this.purchaseDate();
      return onSaleOn ? { onSaleOn } : undefined;
    },
    loader: async ({ params, abortSignal }) => {
      const result = await getApiMarketdataBondSeries({
        query: { onSaleOn: params.onSaleOn },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.purchase.offerLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly portfoliosResource = resource({
    params: () => (this.choosesPortfolio ? {} : undefined),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfolios({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('bonds.purchase.portfoliosLoadFailed'),
        );
      }
      return (result.data ?? []).filter((portfolio) => !portfolio.isArchived);
    },
  });

  protected readonly fundingCandidatesResource = resource({
    params: () => (this.isEdit ? undefined : { currency: BOND_CURRENCY }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioTransferCandidates({
        query: { currency: params.currency, assetClass: ASSET_CLASS.Cash },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.purchase.fundingLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  constructor() {
    const controls = this.form.controls;
    controls.fundingAssetId.updateValueAndValidity({ emitEvent: false });
    controls.bondCount.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => controls.fundingAssetId.updateValueAndValidity());
    controls.purchasePricePerBond.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => controls.fundingAssetId.updateValueAndValidity());
    controls.series.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((series) => this.applySeries(series));

    effect(() => {
      if (!this.offerResource.hasValue()) {
        return;
      }
      const offer = this.offerResource.value();
      untracked(() => {
        const code = this.pendingSeriesCode;
        if (code && offer.some((series) => series.code === code)) {
          this.pendingSeriesCode = null;
          controls.series.setValue(code);
        }
      });
    });

    effect(() => {
      if (!this.fundingCandidatesResource.hasValue()) {
        return;
      }
      const candidates = this.fundingCandidatesResource.value();
      untracked(() => {
        const fundingAssetId = controls.fundingAssetId;
        if (
          fundingAssetId.value !== null &&
          !candidates.some((candidate) => candidate.assetId === fundingAssetId.value)
        ) {
          fundingAssetId.setValue(null);
        }
        fundingAssetId.updateValueAndValidity();
      });
    });
  }

  protected typeLabel(series: BondSeriesListItem): string {
    return bondTypeInfo(series.type)?.label ?? '';
  }

  private applySeries(code: string | null): void {
    const controls = this.form.controls;
    if (code === MANUAL_SERIES) {
      controls.seriesCode.setValue('');
      controls.type.setValue(null);
      controls.purchasePricePerBond.setValue(null);
      controls.firstPeriodRatePercent.setValue(null);
      controls.marginPercent.setValue(null);
      controls.earlyRedemptionFeePerBond.setValue(null);
      return;
    }

    const series = this.offerResource.hasValue()
      ? this.offerResource.value().find((candidate) => candidate.code === code)
      : undefined;
    if (!series) {
      return;
    }

    controls.seriesCode.setValue(series.code);
    controls.type.setValue(Number(series.type));
    controls.purchasePricePerBond.setValue(Number(series.issuePrice));
    controls.firstPeriodRatePercent.setValue(emptyToNull(series.firstPeriodRatePercent));
    controls.marginPercent.setValue(emptyToNull(series.marginPercent));
    controls.earlyRedemptionFeePerBond.setValue(
      bondTypeInfo(series.type)?.defaultEarlyRedemptionFee ?? null,
    );

    // The name follows the series until the user types one of their own.
    const name = controls.name.value.trim();
    if (!name || name === this.prefilledName) {
      controls.name.setValue(series.code);
      this.prefilledName = series.code;
    }
  }

  // Display and cap check only, in whole grosze; the server computes the stored amount.
  private costInGrosze(
    values: { bondCount?: number | null; purchasePricePerBond?: number | null } | undefined,
  ): number | null {
    const count = emptyToNull(values?.bondCount);
    const price = emptyToNull(values?.purchasePricePerBond);
    return count === null || price === null ? null : count * Math.round(price * 100);
  }

  private fundingSource(assetId: string): TransferCandidateResponse | undefined {
    return this.fundingCandidatesResource.hasValue()
      ? this.fundingCandidatesResource.value().find((candidate) => candidate.assetId === assetId)
      : undefined;
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

    const values = this.form.getRawValue();
    const terms: UpdateBondRequest = {
      name: values.name.trim(),
      seriesCode: values.seriesCode.trim().toUpperCase(),
      type: Number(values.type),
      purchaseDate: toDateOnly(values.purchaseDate!),
      bondCount: Number(values.bondCount),
      purchasePricePerBond: Number(values.purchasePricePerBond),
      firstPeriodRatePercent: Number(values.firstPeriodRatePercent),
      marginPercent: emptyToNull(values.marginPercent),
      earlyRedemptionFeePerBond: Number(values.earlyRedemptionFeePerBond),
      taxExempt: values.taxExempt,
    };

    const result = this.bond
      ? await putApiPortfolioPortfoliosByPortfolioIdBondsByAssetId({
          path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
          body: terms,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdBonds({
          path: { portfolioId: values.portfolioId },
          body: { ...terms, fundingAssetId: values.fundingAssetId ?? undefined },
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
    if (problem.errorCode === 'Validation.InsufficientFunds') {
      this.form.controls.fundingAssetId.setErrors({
        server: problem.detail ?? translate('bonds.purchase.insufficientFunds'),
      });
      this.form.controls.fundingAssetId.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
