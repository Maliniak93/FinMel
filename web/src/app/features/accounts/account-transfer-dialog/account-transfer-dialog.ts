import { Component, computed, inject, resource, signal } from '@angular/core';
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

import {
  getApiPortfolioTransferCandidates,
  postApiPortfolioTransfers,
  type AccountResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';

export interface AccountTransferDialogData {
  account: AccountResponse;
}

const ACCOUNT_CURRENCY = 'PLN';

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

@Component({
  selector: 'app-account-transfer-dialog',
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
  templateUrl: './account-transfer-dialog.html',
  styleUrl: './account-transfer-dialog.scss',
})
export class AccountTransferDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AccountTransferDialog>);
  private readonly data = inject<AccountTransferDialogData>(MAT_DIALOG_DATA);

  protected readonly account = this.data.account;
  protected readonly formatMoney = formatMoney;
  protected readonly maxDate = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    targetAssetId: [null as string | null, [Validators.required]],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    date: [
      today() as Date | null,
      [
        Validators.required,
        (control: AbstractControl): ValidationErrors | null =>
          control.value && (control.value as Date) > this.maxDate ? { dateInFuture: true } : null,
      ],
    ],
  });

  protected readonly candidatesResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioTransferCandidates({
        query: { currency: ACCOUNT_CURRENCY, assetClass: ASSET_CLASS.Cash, accountCash: true },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('accounts.transfer.loadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly targets = computed(() =>
    this.candidatesResource.hasValue()
      ? this.candidatesResource
          .value()
          .filter((candidate) => candidate.assetId !== this.account.cashAssetId)
      : [],
  );

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
    const result = await postApiPortfolioTransfers({
      body: {
        sourceAssetId: this.account.cashAssetId,
        targetAssetId: values.targetAssetId!,
        amount: Number(values.amount),
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

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (problem.errorCode === 'Validation.InsufficientFunds') {
      this.form.controls.amount.setErrors({ server: translate('accounts.cash.insufficientFunds') });
      this.form.controls.amount.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
