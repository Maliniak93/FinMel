import { DatePipe } from '@angular/common';
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
import { map } from 'rxjs';

import {
  getApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdSettlementPreview,
  postApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdRollover,
  type DepositResponse,
  type RollOverDepositRequest,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format-money';
import {
  DEPOSIT_CAPITALIZATIONS,
  DEPOSIT_STATUS,
  DEPOSIT_TERM_UNIT,
  depositMaturityDate,
  settlementAmounts,
} from '../deposit-terms';

export interface RollOverDepositDialogData {
  deposit: DepositResponse;
}

// The tax can't exceed the gross interest (as RollOverDepositRequest.Validate), so it reads its
// sibling and is re-run whenever the gross changes.
function taxWithinGross(control: AbstractControl): ValidationErrors | null {
  const gross = control.parent?.get('grossInterest')?.value as number | null | undefined;
  if (gross === null || gross === undefined || control.value === null || control.value === '') {
    return null;
  }
  return Number(control.value) > Number(gross) ? { taxOverGross: true } : null;
}

// "3 months", "1 day" — the unchanged term, shown read-only.
function termLabel(termLength: number, termUnit: DepositResponse['termUnit']): string {
  const unit = Number(termUnit) === DEPOSIT_TERM_UNIT.Days ? 'day' : 'month';
  return `${termLength} ${unit}${termLength === 1 ? '' : 's'}`;
}

function isAmount(value: number | string | null): value is number | string {
  return value !== null && value !== '' && Number.isFinite(Number(value));
}

// Rolls a Due or Settled (not paid-out) deposit over into its next term on the same asset
// (deposit-rollover). The term and capitalisation stay, the next term starts on the old maturity date
// with the whole balance, and only the rate can change. A Due deposit is settled in the same request,
// so its gross interest and tax are pre-filled from the settlement preview and editable; a Settled one
// reuses its stored settlement. Control names follow the RollOverDepositRequest properties, so a
// server 400 keyed on a field lands on it.
@Component({
  selector: 'app-roll-over-deposit-dialog',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './roll-over-deposit-dialog.html',
  styleUrl: './roll-over-deposit-dialog.scss',
})
export class RollOverDepositDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<RollOverDepositDialog>);
  protected readonly deposit = inject<RollOverDepositDialogData>(MAT_DIALOG_DATA).deposit;

  // Due: settle and roll over in one request. Otherwise Settled: the stored settlement is reused.
  protected readonly isDue = Number(this.deposit.status) === DEPOSIT_STATUS.Due;

  protected readonly formatMoney = formatMoney;
  protected readonly term = termLabel(Number(this.deposit.termLength), this.deposit.termUnit);
  protected readonly capitalization =
    DEPOSIT_CAPITALIZATIONS.find(
      (option) => Number(option.value) === Number(this.deposit.capitalization),
    )?.label ?? '—';
  // The bank renews on the maturity day, so the next term starts then, however late the rollover.
  protected readonly newStartDate = fromDateOnly(this.deposit.maturityDate);
  protected readonly newMaturityDate = depositMaturityDate(
    this.newStartDate,
    Number(this.deposit.termLength),
    this.deposit.termUnit,
  );

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    annualInterestRatePercent: [
      Number(this.deposit.annualInterestRatePercent) as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    // Due mode only: a Settled deposit sends neither, so both stay disabled (and unrendered) there.
    grossInterest: [
      { value: null as number | null, disabled: !this.isDue },
      [Validators.required, Validators.min(0)],
    ],
    tax: [
      { value: null as number | null, disabled: !this.isDue },
      [Validators.required, Validators.min(0), taxWithinGross],
    ],
  });

  // Signal mirror of the form, so the new principal follows the settlement edits live.
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  // The whole balance rolls over: principal + the net interest of the term that ended — the one being
  // settled here (Due), or the stored settlement (Settled).
  protected readonly newPrincipal = computed(() => {
    if (!this.isDue) {
      return settlementAmounts(
        this.deposit.principal,
        this.deposit.settledGrossInterest ?? 0,
        this.deposit.settledTax ?? 0,
      ).finalAmount;
    }
    const { grossInterest, tax } = this.formValue();
    if (!isAmount(grossInterest) || !isAmount(tax)) {
      return null;
    }
    return settlementAmounts(this.deposit.principal, grossInterest, tax).finalAmount;
  });

  // Only a Due deposit has a preview to pre-fill from; a Settled one loads nothing.
  protected readonly previewResource = resource({
    params: () => (this.isDue ? {} : undefined),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdSettlementPreview(
        {
          path: { portfolioId: this.deposit.portfolioId, assetId: this.deposit.assetId },
          signal: abortSignal,
        },
      );
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? 'Failed to load the settlement preview.',
        );
      }
      return result.data ?? null;
    },
  });

  // Pre-fills the settlement once the preview has loaded — gated on hasValue(), since value() throws
  // while the resource is in its error state.
  private readonly prefillFromPreviewEffect = effect(() => {
    if (!this.previewResource.hasValue()) {
      return;
    }
    const preview = this.previewResource.value();
    if (preview) {
      // Untracked: whatever the form reads while updating must not make this effect re-run and
      // overwrite the user's edits.
      untracked(() =>
        this.form.patchValue({
          grossInterest: Number(preview.grossInterest),
          tax: Number(preview.tax),
        }),
      );
    }
  });

  constructor() {
    this.form.controls.grossInterest.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.tax.updateValueAndValidity());
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
    // A Settled deposit leaves grossInterest and tax out of the body altogether.
    const body: RollOverDepositRequest = this.isDue
      ? {
          annualInterestRatePercent: Number(values.annualInterestRatePercent),
          grossInterest: Number(values.grossInterest),
          tax: Number(values.tax),
        }
      : { annualInterestRatePercent: Number(values.annualInterestRatePercent) };

    const result = await postApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdRollover({
      path: { portfolioId: this.deposit.portfolioId, assetId: this.deposit.assetId },
      body,
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

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }
}
