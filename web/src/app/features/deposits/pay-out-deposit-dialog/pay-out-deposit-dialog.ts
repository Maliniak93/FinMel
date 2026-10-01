import { Component, inject, signal } from '@angular/core';
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
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  postApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdPayout,
  type DepositResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { settlementAmounts } from '../deposit-terms';
import { PayoutDestinationField } from '../payout-destination-field/payout-destination-field';

export interface PayOutDepositDialogData {
  deposit: DepositResponse;
}

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

// Pays a Settled deposit out (deposit-payout-to-cash, deposit-payout-to-savings): its whole balance —
// principal + the settled net interest, never a part of it — moves to one of the Cash or Savings
// transfer candidates in its currency (or a savings account opened from the destination select), on a
// date between the settlement date and today. Control names follow the
// PayOutDepositRequest properties, so a server 400 keyed on a field lands on it.
@Component({
  selector: 'app-pay-out-deposit-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    PayoutDestinationField,
    TranslocoPipe,
  ],
  templateUrl: './pay-out-deposit-dialog.html',
  styleUrl: './pay-out-deposit-dialog.scss',
})
export class PayOutDepositDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<PayOutDepositDialog>);
  protected readonly deposit = inject<PayOutDepositDialogData>(MAT_DIALOG_DATA).deposit;

  protected readonly formatMoney = formatMoney;
  // Read-only: a payout always moves the whole balance.
  protected readonly amount = settlementAmounts(
    this.deposit.principal,
    this.deposit.settledGrossInterest ?? 0,
    this.deposit.settledTax ?? 0,
  ).finalAmount;
  // The payout date lies between the settlement date and today (Europe/Warsaw on the server; the
  // viewer's local date here).
  protected readonly minDate = fromDateOnly(this.deposit.settledOn ?? this.deposit.maturityDate);
  protected readonly maxDate = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    destinationAssetId: [null as string | null, [Validators.required]],
    date: [
      today() as Date | null,
      [
        Validators.required,
        (control: AbstractControl): ValidationErrors | null =>
          this.dateWithinRange(control.value as Date | null),
      ],
    ],
  });

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
    const result = await postApiPortfolioPortfoliosByPortfolioIdDepositsByAssetIdPayout({
      path: { portfolioId: this.deposit.portfolioId, assetId: this.deposit.assetId },
      body: {
        destinationAssetId: values.destinationAssetId!,
        date: toDateOnly(values.date!),
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

  private dateWithinRange(date: Date | null): ValidationErrors | null {
    if (!date) {
      return null;
    }
    if (date < this.minDate) {
      return { dateBeforeSettlement: true };
    }
    return date > this.maxDate ? { dateInFuture: true } : null;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
