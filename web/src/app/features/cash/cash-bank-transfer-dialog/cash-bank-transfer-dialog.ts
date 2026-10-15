import { Component, inject, resource, signal } from '@angular/core';
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
  getApiPortfolioAccounts,
  getApiPortfolioTransferCandidates,
  postApiPortfolioTransfers,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { ACCOUNT_TYPE } from '../../accounts/account-type';

export type CashBankTransferDirection = 'atm' | 'bankDeposit';

export interface CashBankTransferDialogData {
  direction: CashBankTransferDirection;
  anchor: 'place' | 'bank';
  anchorAssetId: string;
  anchorName: string;
  currency: string;
  anchorBalance: number | string;
}

interface Counterpart {
  assetId: string;
  name: string;
  balance: number | string;
}

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

@Component({
  selector: 'app-cash-bank-transfer-dialog',
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
  templateUrl: './cash-bank-transfer-dialog.html',
  styleUrl: './cash-bank-transfer-dialog.scss',
})
export class CashBankTransferDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<CashBankTransferDialog>);
  protected readonly data = inject<CashBankTransferDialogData>(MAT_DIALOG_DATA);

  protected readonly formatMoney = formatMoney;
  protected readonly maxDate = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    counterpartAssetId: [null as string | null, [Validators.required]],
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

  protected readonly counterpartsResource = resource({
    loader: async ({ abortSignal }): Promise<Counterpart[]> => {
      if (this.data.anchor === 'bank') {
        const result = await getApiPortfolioTransferCandidates({
          query: { currency: this.data.currency, cashPlaces: true },
          signal: abortSignal,
        });
        if (result.error) {
          throw new Error(
            readProblemDetails(result.error).detail ?? translate('cash.transfer.loadFailed'),
          );
        }
        return (result.data ?? []).map((candidate) => ({
          assetId: candidate.assetId,
          name: `${candidate.name} · ${this.data.currency}`,
          balance: candidate.balance,
        }));
      }

      const result = await getApiPortfolioAccounts({
        query: { type: ACCOUNT_TYPE.Bank },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('cash.transfer.loadFailed'),
        );
      }
      return (result.data ?? [])
        .filter((account) => !account.isArchived && account.currency === this.data.currency)
        .map((account) => ({
          assetId: account.cashAssetId,
          name: account.name,
          balance: account.cashBalance,
        }));
    },
  });

  protected get counterparts(): Counterpart[] {
    return this.counterpartsResource.hasValue() ? this.counterpartsResource.value() : [];
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
    const anchorIsSource =
      (this.data.direction === 'bankDeposit') === (this.data.anchor === 'place');
    const counterpartId = values.counterpartAssetId!;
    const result = await postApiPortfolioTransfers({
      body: {
        sourceAssetId: anchorIsSource ? this.data.anchorAssetId : counterpartId,
        targetAssetId: anchorIsSource ? counterpartId : this.data.anchorAssetId,
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
      this.form.controls.amount.setErrors({ server: translate('cash.insufficientFunds') });
      this.form.controls.amount.markAsTouched();
      return;
    }

    if (problem.errorCode === 'Validation.TransferCurrencyMismatch') {
      this.form.controls.counterpartAssetId.setErrors({
        server: translate('cash.transfer.currencyMismatch'),
      });
      this.form.controls.counterpartAssetId.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
