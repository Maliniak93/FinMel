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
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { map } from 'rxjs';

import {
  getApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdSettlementPreview,
  postApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdSettle,
  type DepositResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format-money';
import { settlementAmounts } from '../deposit-terms';

export interface SettleDepositDialogData {
  deposit: DepositResponse;
}

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

// The tax can't exceed the gross interest (as SettleDepositRequest.Validate), so it reads its sibling
// and is re-run whenever the gross changes.
function taxWithinGross(control: AbstractControl): ValidationErrors | null {
  const gross = control.parent?.get('grossInterest')?.value as number | null | undefined;
  if (gross === null || gross === undefined || control.value === null || control.value === '') {
    return null;
  }
  return Number(control.value) > Number(gross) ? { taxOverGross: true } : null;
}

function isAmount(value: number | string | null): value is number | string {
  return value !== null && value !== '' && Number.isFinite(Number(value));
}

// Settles a Due term deposit (term-deposits-settlement): pre-filled from the server's settlement
// preview, with what the bank actually paid editable. Control names follow the SettleDepositRequest
// properties, so a server 400 keyed on a field lands on it.
@Component({
  selector: 'app-settle-deposit-dialog',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './settle-deposit-dialog.html',
  styleUrl: './settle-deposit-dialog.scss',
})
export class SettleDepositDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<SettleDepositDialog>);
  protected readonly deposit = inject<SettleDepositDialogData>(MAT_DIALOG_DATA).deposit;

  protected readonly formatMoney = formatMoney;
  protected readonly maturityDate = fromDateOnly(this.deposit.maturityDate);
  // The settlement date lies between the start date and today (Europe/Warsaw on the server; the
  // viewer's local date here).
  protected readonly minSettledOn = fromDateOnly(this.deposit.startDate);
  protected readonly maxSettledOn = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    settledOn: [
      null as Date | null,
      [
        Validators.required,
        (control: AbstractControl): ValidationErrors | null =>
          this.settledOnWithinRange(control.value as Date | null),
      ],
    ],
    grossInterest: [null as number | null, [Validators.required, Validators.min(0)]],
    tax: [null as number | null, [Validators.required, Validators.min(0), taxWithinGross]],
  });

  // Signal mirror of the form, so net interest and the final amount follow the edits live.
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly amounts = computed(() => {
    const { grossInterest, tax } = this.formValue();
    if (!isAmount(grossInterest) || !isAmount(tax)) {
      return null;
    }
    return settlementAmounts(this.deposit.principal, grossInterest, tax);
  });

  protected readonly previewResource = resource({
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

  // Pre-fills the form once the preview has loaded — gated on hasValue(), since value() throws while
  // the resource is in its error state.
  private readonly prefillFromPreviewEffect = effect(() => {
    if (!this.previewResource.hasValue()) {
      return;
    }
    const preview = this.previewResource.value();
    if (preview) {
      // Untracked: whatever the form reads while updating must not make this effect re-run and
      // overwrite the user's edits.
      untracked(() =>
        this.form.setValue({
          settledOn: fromDateOnly(preview.settledOn),
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
    const result = await postApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdSettle({
      path: { portfolioId: this.deposit.portfolioId, assetId: this.deposit.assetId },
      body: {
        settledOn: toDateOnly(values.settledOn!),
        grossInterest: Number(values.grossInterest),
        tax: Number(values.tax),
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

  private settledOnWithinRange(settledOn: Date | null): ValidationErrors | null {
    if (!settledOn) {
      return null;
    }
    if (settledOn < this.minSettledOn) {
      return { settledOnBeforeStart: true };
    }
    return settledOn > this.maxSettledOn ? { settledOnInFuture: true } : null;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }
}
