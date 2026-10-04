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
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import { getApiMarketdataBondSeries, type BondSeriesListItem } from '../../../api/marketdata';
import {
  getApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdRedemptionPreview,
  postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdSwap,
  type BondResponse,
  type TreasuryBondType,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { formatDate, formatMoney } from '../../../shared/format';
import { loadCashCandidates } from '../bond-interest';
import { BOND_TYPES, bondTypeInfo } from '../bond-types';

export interface SwapBondDialogData {
  bond: BondResponse;
}

const BOND_CURRENCY = 'PLN';

// The series picker's "enter manually" entry; never a real series code, which is always 3 letters + 4 digits.
const MANUAL_SERIES = 'manual';

function emptyToNull(value: unknown): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

function toGrosze(value: unknown): number | null {
  const amount = emptyToNull(value);
  return amount === null || !Number.isFinite(amount) ? null : Math.round(amount * 100);
}

interface SwapAmounts {
  readonly proceeds: number;
  readonly cost: number | null;
  readonly leftover: number | null;
}

@Component({
  selector: 'app-swap-bond-dialog',
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
  templateUrl: './swap-bond-dialog.html',
  styleUrl: './swap-bond-dialog.scss',
})
export class SwapBondDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject<MatDialogRef<SwapBondDialog, boolean>>(MatDialogRef);
  protected readonly bond = inject<SwapBondDialogData>(MAT_DIALOG_DATA).bond;

  protected readonly heldCount = Number(this.bond.bondCount);
  protected readonly manualSeries = MANUAL_SERIES;
  protected readonly bondTypes = BOND_TYPES;
  protected readonly currency = BOND_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  private prefilledName: string | null = null;

  protected readonly offerResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiMarketdataBondSeries({
        query: { onSaleOn: this.bond.maturityDate },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.swap.offerLoadFailed'),
        );
      }
      // A series without a swap price can't be bought in a swap.
      return (result.data ?? []).filter(
        (series) => series.swapPrice !== null && series.swapPrice !== undefined,
      );
    },
  });

  protected readonly previewResource = resource({
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
    loader: ({ abortSignal }) => loadCashCandidates(abortSignal),
  });

  private readonly costWithinProceeds = (control: AbstractControl): ValidationErrors | null => {
    const leftover = this.amountsInGrosze(control.parent?.getRawValue())?.leftover ?? null;
    return leftover !== null && leftover < 0 ? { exceedsProceeds: true } : null;
  };

  private readonly destinationForLeftover = (control: AbstractControl): ValidationErrors | null => {
    const leftover = this.amountsInGrosze(control.parent?.getRawValue())?.leftover ?? null;
    return leftover !== null && leftover > 0 && !control.value ? { required: true } : null;
  };

  protected readonly form = this.formBuilder.group({
    series: [null as string | null],
    name: this.formBuilder.nonNullable.control('', [
      Validators.required,
      Validators.maxLength(200),
    ]),
    seriesCode: this.formBuilder.nonNullable.control('', [
      Validators.required,
      Validators.pattern(/^[A-Za-z]{3}\d{4}$/),
    ]),
    type: [null as TreasuryBondType | null, [Validators.required]],
    swapPricePerBond: [
      null as number | null,
      [Validators.required, Validators.min(0.01), Validators.max(100)],
    ],
    firstPeriodRatePercent: [
      null as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    marginPercent: [null as number | null, [Validators.min(0), Validators.max(100)]],
    earlyRedemptionFeePerBond: [
      null as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    bondCount: [
      this.heldCount as number | null,
      [
        Validators.required,
        Validators.min(1),
        Validators.max(this.heldCount),
        this.costWithinProceeds,
      ],
    ],
    destinationAssetId: [null as string | null, [this.destinationForLeftover]],
  });

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly amounts = computed<SwapAmounts | null>(() => {
    if (!this.previewResource.hasValue()) {
      return null;
    }
    const grosze = this.amountsInGrosze(this.formValue());
    if (!grosze) {
      return null;
    }
    return {
      proceeds: grosze.proceeds / 100,
      cost: grosze.cost === null ? null : grosze.cost / 100,
      leftover: grosze.leftover === null ? null : grosze.leftover / 100,
    };
  });

  protected readonly needsDestination = computed(() => (this.amounts()?.leftover ?? 0) > 0);

  constructor() {
    const controls = this.form.controls;
    controls.series.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((series) => this.applySeries(series));
    for (const control of [controls.bondCount, controls.swapPricePerBond]) {
      control.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.revalidateAmounts());
    }

    effect(() => {
      if (this.previewResource.hasValue()) {
        untracked(() => this.revalidateAmounts());
      }
    });

    // The Cash the purchase was paid from is where the leftover most likely goes.
    effect(() => {
      if (!this.candidatesResource.hasValue()) {
        return;
      }
      const candidates = this.candidatesResource.value();
      untracked(() => {
        const destination = controls.destinationAssetId;
        if (
          destination.value === null &&
          candidates.some((candidate) => candidate.assetId === this.bond.fundingAssetId)
        ) {
          destination.setValue(this.bond.fundingAssetId ?? null);
        }
      });
    });
  }

  protected typeLabel(series: BondSeriesListItem): string {
    return bondTypeInfo(series.type)?.label ?? '';
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid || !this.previewResource.hasValue()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const result = await postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdSwap({
      path: { portfolioId: this.bond.portfolioId, assetId: this.bond.assetId },
      body: {
        bondCount: Number(values.bondCount),
        newBond: {
          name: values.name.trim(),
          seriesCode: values.seriesCode.trim().toUpperCase(),
          type: Number(values.type),
          swapPricePerBond: Number(values.swapPricePerBond),
          firstPeriodRatePercent: Number(values.firstPeriodRatePercent),
          marginPercent: emptyToNull(values.marginPercent),
          earlyRedemptionFeePerBond: Number(values.earlyRedemptionFeePerBond),
        },
        destinationAssetId: this.needsDestination() ? values.destinationAssetId : null,
      },
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

  private applySeries(code: string | null): void {
    const controls = this.form.controls;
    if (code === MANUAL_SERIES) {
      controls.seriesCode.setValue('');
      controls.type.setValue(null);
      controls.swapPricePerBond.setValue(null);
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
    controls.swapPricePerBond.setValue(emptyToNull(series.swapPrice));
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

  private revalidateAmounts(): void {
    const controls = this.form.controls;
    controls.bondCount.updateValueAndValidity({ emitEvent: false });
    controls.destinationAssetId.updateValueAndValidity({ emitEvent: false });
  }

  // Display and checks only, in whole grosze; the server computes the stored amounts.
  private amountsInGrosze(
    values: { bondCount?: number | null; swapPricePerBond?: number | null } | undefined,
  ): { proceeds: number; cost: number | null; leftover: number | null } | null {
    if (!this.previewResource.hasValue()) {
      return null;
    }
    const proceeds = toGrosze(this.previewResource.value().proceeds);
    if (proceeds === null) {
      return null;
    }
    const count = emptyToNull(values?.bondCount);
    const price = toGrosze(values?.swapPricePerBond);
    const cost = count === null || price === null ? null : count * price;
    return { proceeds, cost, leftover: cost === null ? null : proceeds - cost };
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('bonds.swap.failed'));
  }
}
