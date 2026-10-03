import { Component, effect, inject, resource, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
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
  type SavingsAccountResponse,
  type TransferCandidateResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';

export interface SavingsTransferDialogData {
  account: SavingsAccountResponse;
}

/** 'in' moves money from Cash into the account, 'out' from the account to Cash. */
export type SavingsTransferDirection = 'in' | 'out';

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

// Moves money between a Cash asset and a savings account as one transfer (savings-cash-transfers):
// "Into this account" takes it from the chosen Cash, "Out to cash" sends it there. The amount is capped
// by the source's balance — the Cash going in, the account going out; the server re-checks it against
// the source's whole history and answers InsufficientFunds, which lands on the amount field.
@Component({
  selector: 'app-savings-transfer-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './savings-transfer-dialog.html',
  styleUrl: './savings-transfer-dialog.scss',
})
export class SavingsTransferDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<SavingsTransferDialog>);
  protected readonly account = inject<SavingsTransferDialogData>(MAT_DIALOG_DATA).account;

  protected readonly formatMoney = formatMoney;
  protected readonly maxDate = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  // Reads its siblings `direction` and `cashAssetId`, so it is re-run whenever either, or the
  // candidates' balances, change.
  private readonly amountWithinBalance = (control: AbstractControl): ValidationErrors | null => {
    if (control.value === null || control.value === '') {
      return null;
    }
    const balance = this.sourceBalance(
      control.parent?.get('direction')?.value as SavingsTransferDirection | undefined,
      control.parent?.get('cashAssetId')?.value as string | null | undefined,
    );
    if (balance === undefined) {
      return null;
    }
    return Number(control.value) > balance ? { exceedsBalance: { balance } } : null;
  };

  protected readonly form = this.formBuilder.nonNullable.group({
    direction: ['in' as SavingsTransferDirection],
    cashAssetId: [null as string | null, [Validators.required]],
    amount: [
      null as number | null,
      [Validators.required, Validators.min(0.01), this.amountWithinBalance],
    ],
    date: [
      today() as Date | null,
      [
        Validators.required,
        (control: AbstractControl): ValidationErrors | null =>
          control.value && (control.value as Date) > this.maxDate ? { dateInFuture: true } : null,
      ],
    ],
  });

  protected readonly cashCandidatesResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioTransferCandidates({
        query: { currency: this.account.currency, assetClass: ASSET_CLASS.Cash },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('deposits.errors.cashAccountsLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  constructor() {
    // The cap had no parent to read its siblings from while the group was being built.
    this.form.controls.amount.updateValueAndValidity({ emitEvent: false });
    this.form.controls.direction.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.amount.updateValueAndValidity());
    this.form.controls.cashAssetId.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.amount.updateValueAndValidity());

    // The Cash balances just loaded cap the amount going in.
    effect(() => {
      if (this.cashCandidatesResource.hasValue()) {
        untracked(() => this.form.controls.amount.updateValueAndValidity());
      }
    });
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
    const cashAssetId = values.cashAssetId!;
    const into = values.direction === 'in';
    const result = await postApiPortfolioTransfers({
      body: {
        sourceAssetId: into ? cashAssetId : this.account.assetId,
        targetAssetId: into ? this.account.assetId : cashAssetId,
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

  // The balance the amount can't exceed: the selected Cash going in, the account going out;
  // undefined while unknown (no Cash selected yet, or the candidates still loading).
  private sourceBalance(
    direction: SavingsTransferDirection | undefined,
    cashAssetId: string | null | undefined,
  ): number | undefined {
    if (direction === 'out') {
      return Number(this.account.balance);
    }
    if (!cashAssetId) {
      return undefined;
    }
    const cash = this.cashCandidate(cashAssetId);
    return cash ? Number(cash.balance) : undefined;
  }

  private cashCandidate(assetId: string): TransferCandidateResponse | undefined {
    return this.cashCandidatesResource.hasValue()
      ? this.cashCandidatesResource.value().find((candidate) => candidate.assetId === assetId)
      : undefined;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    // The source can't cover the amount on that date — an amount error.
    if (problem.errorCode === 'Validation.InsufficientFunds') {
      this.form.controls.amount.setErrors({
        server: problem.detail ?? translate('savings.transfer.insufficientFunds'),
      });
      this.form.controls.amount.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
