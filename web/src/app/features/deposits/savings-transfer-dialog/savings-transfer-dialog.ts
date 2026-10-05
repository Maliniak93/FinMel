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
  type CashAccountResponse,
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

export type SavingsTransferDialogData =
  { account: SavingsAccountResponse } | { cash: CashAccountResponse };

interface TransferAnchor {
  assetId: string;
  name: string;
  currency: string;
  balance: number;
}

export type SavingsTransferDirection = 'in' | 'out';

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

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
  private readonly data = inject<SavingsTransferDialogData>(MAT_DIALOG_DATA);
  protected readonly anchoredOnCash = 'cash' in this.data;
  protected readonly anchor: TransferAnchor =
    'cash' in this.data
      ? { ...this.data.cash, balance: Number(this.data.cash.balance) }
      : { ...this.data.account, balance: Number(this.data.account.balance) };

  protected readonly formatMoney = formatMoney;
  protected readonly maxDate = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  private readonly amountWithinBalance = (control: AbstractControl): ValidationErrors | null => {
    if (control.value === null || control.value === '') {
      return null;
    }
    const balance = this.sourceBalance(
      control.parent?.get('direction')?.value as SavingsTransferDirection | undefined,
      control.parent?.get(this.counterpartControl)?.value as string | null | undefined,
    );
    if (balance === undefined) {
      return null;
    }
    return Number(control.value) > balance ? { exceedsBalance: { balance } } : null;
  };

  protected readonly form = this.formBuilder.nonNullable.group({
    direction: ['in' as SavingsTransferDirection],
    cashAssetId: [
      { value: null as string | null, disabled: this.anchoredOnCash },
      [Validators.required],
    ],
    savingsAssetId: [
      { value: null as string | null, disabled: !this.anchoredOnCash },
      [Validators.required],
    ],
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

  protected readonly candidatesResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioTransferCandidates({
        query: {
          currency: this.anchor.currency,
          assetClass: this.anchoredOnCash ? ASSET_CLASS.Savings : ASSET_CLASS.Cash,
        },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate(
              this.anchoredOnCash ? 'savings.loadFailed' : 'deposits.errors.cashAccountsLoadFailed',
            ),
        );
      }
      return result.data ?? [];
    },
  });

  constructor() {
    // The cap validators had no parent to read their siblings from while the group was being built.
    this.form.controls.amount.updateValueAndValidity({ emitEvent: false });
    this.form.controls.direction.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.amount.updateValueAndValidity());
    this.form.controls[this.counterpartControl].valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.amount.updateValueAndValidity());

    effect(() => {
      if (this.candidatesResource.hasValue()) {
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
    const counterpartId = values[this.counterpartControl]!;
    const cashAssetId = this.anchoredOnCash ? this.anchor.assetId : counterpartId;
    const savingsAssetId = this.anchoredOnCash ? counterpartId : this.anchor.assetId;
    const into = values.direction === 'in';
    const result = await postApiPortfolioTransfers({
      body: {
        sourceAssetId: into ? cashAssetId : savingsAssetId,
        targetAssetId: into ? savingsAssetId : cashAssetId,
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

  private get counterpartControl(): 'cashAssetId' | 'savingsAssetId' {
    return this.anchoredOnCash ? 'savingsAssetId' : 'cashAssetId';
  }

  // Direction is always relative to the savings account: 'in' moves cash into savings.
  private sourceBalance(
    direction: SavingsTransferDirection | undefined,
    counterpartId: string | null | undefined,
  ): number | undefined {
    const anchorIsSource = (direction === 'in') === this.anchoredOnCash;
    if (anchorIsSource) {
      return this.anchor.balance;
    }
    if (!counterpartId) {
      return undefined;
    }
    const counterpart = this.candidate(counterpartId);
    return counterpart ? Number(counterpart.balance) : undefined;
  }

  private candidate(assetId: string): TransferCandidateResponse | undefined {
    return this.candidatesResource.hasValue()
      ? this.candidatesResource.value().find((candidate) => candidate.assetId === assetId)
      : undefined;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
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
