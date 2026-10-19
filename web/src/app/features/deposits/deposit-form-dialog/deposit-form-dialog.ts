import { Component, computed, inject, resource, signal } from '@angular/core';
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
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import {
  getApiPortfolioAccounts,
  postApiPortfolioDeposits,
  putApiPortfolioDepositsByAssetId,
  type DepositCapitalization,
  type DepositResponse,
  type DepositTermUnit,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatAmount, formatDate } from '../../../shared/format';
import { assignGroup } from '../../../shared/group-select/assign-group';
import { GroupSelect } from '../../../shared/group-select/group-select';
import { ACCOUNT_TYPE } from '../../accounts/account-type';
import {
  DEPOSIT_CAPITALIZATIONS,
  DEPOSIT_TERM_UNIT,
  DEPOSIT_TERM_UNITS,
  depositMaturityDate,
  maxTermLength,
} from '../deposit-terms';

export interface DepositFormDialogData {
  deposit?: DepositResponse;
}

function termLengthWithinCap(control: AbstractControl): ValidationErrors | null {
  const termUnit = control.parent?.get('termUnit')?.value as DepositTermUnit | undefined;
  if (termUnit === undefined || control.value === null || control.value === '') {
    return null;
  }
  const max = maxTermLength(termUnit);
  return Number(control.value) > max ? { termMax: { max } } : null;
}

@Component({
  selector: 'app-deposit-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    GroupSelect,
    TranslocoPipe,
  ],
  templateUrl: './deposit-form-dialog.html',
  styleUrl: './deposit-form-dialog.scss',
})
export class DepositFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<DepositFormDialog>);
  protected readonly data = inject<DepositFormDialogData>(MAT_DIALOG_DATA);

  private readonly deposit = this.data.deposit;
  protected readonly isEdit = !!this.deposit;
  private readonly isRolledOver = Number(this.deposit?.rolloverCount ?? 0) > 0;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly termUnits = DEPOSIT_TERM_UNITS;
  protected readonly capitalizations = DEPOSIT_CAPITALIZATIONS;
  protected readonly formatAmount = formatAmount;
  protected readonly formatDate = formatDate;

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [this.deposit?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    groupId: [this.deposit?.groupId ?? (null as string | null)],
    bankName: [this.deposit?.bankName ?? '', [Validators.maxLength(100)]],
    sourceAccountId: [
      { value: null as string | null, disabled: this.isEdit },
      [Validators.required],
    ],
    returnAccountId: [this.deposit?.returnAccountId ?? (null as string | null)],
    principal: [
      {
        value: this.deposit ? Number(this.deposit.principal) : (null as number | null),
        disabled: this.isRolledOver,
      },
      [Validators.required, Validators.min(0.01)],
    ],
    startDate: [
      {
        value: this.deposit ? fromDateOnly(this.deposit.startDate) : (new Date() as Date | null),
        disabled: this.isRolledOver,
      },
      [Validators.required],
    ],
    termLength: [
      this.deposit ? Number(this.deposit.termLength) : (null as number | null),
      [Validators.required, Validators.min(1), termLengthWithinCap],
    ],
    termUnit: [
      (this.deposit ? Number(this.deposit.termUnit) : DEPOSIT_TERM_UNIT.Months) as DepositTermUnit,
      [Validators.required],
    ],
    annualInterestRatePercent: [
      this.deposit ? Number(this.deposit.annualInterestRatePercent) : (null as number | null),
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    capitalization: [
      (this.deposit ? Number(this.deposit.capitalization) : 0) as DepositCapitalization,
      [Validators.required],
    ],
    earlyBreakInterestLossPercent: [
      this.deposit ? Number(this.deposit.earlyBreakInterestLossPercent) : (100 as number | null),
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
  });

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly maturityDate = computed(() => {
    const { startDate, termLength, termUnit } = this.formValue();
    const length = Number(termLength);
    if (!startDate || termLength === null || !Number.isInteger(length) || length < 1) {
      return null;
    }
    return depositMaturityDate(startDate, length, termUnit);
  });

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({
        query: { type: ACCOUNT_TYPE.Bank },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('deposits.form.accountsLoadFailed'),
        );
      }
      return (result.data ?? []).filter((account) => !account.isArchived);
    },
  });

  protected readonly accounts = computed(() =>
    this.accountsResource.hasValue() ? this.accountsResource.value() : [],
  );

  protected readonly currency = computed(
    () =>
      this.deposit?.currency ??
      this.accounts().find((account) => account.id === this.formValue().sourceAccountId)
        ?.currency ??
      null,
  );

  protected readonly returnAccounts = computed(() => {
    const currency = this.currency();
    return currency ? this.accounts().filter((account) => account.currency === currency) : [];
  });

  constructor() {
    // The cap validators had no parent to read their siblings from while the group was being built.
    this.form.controls.termLength.updateValueAndValidity({ emitEvent: false });
    this.form.controls.termUnit.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.termLength.updateValueAndValidity());
    this.form.controls.sourceAccountId.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((sourceAccountId) => this.dropForeignCurrencyReturn(sourceAccountId));
  }

  private dropForeignCurrencyReturn(sourceAccountId: string | null): void {
    const returnControl = this.form.controls.returnAccountId;
    const accounts = this.accounts();
    const source = accounts.find((account) => account.id === sourceAccountId);
    const chosen = accounts.find((account) => account.id === returnControl.value);
    if (chosen && chosen.currency !== source?.currency) {
      returnControl.setValue(null);
    }
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
    const terms = {
      name: values.name,
      bankName: values.bankName.trim() || null,
      principal: Number(values.principal),
      startDate: toDateOnly(values.startDate!),
      termLength: Number(values.termLength),
      termUnit: values.termUnit,
      annualInterestRatePercent: Number(values.annualInterestRatePercent),
      capitalization: values.capitalization,
      earlyBreakInterestLossPercent: Number(values.earlyBreakInterestLossPercent),
    };

    const result = this.deposit
      ? await putApiPortfolioDepositsByAssetId({
          path: { assetId: this.deposit.assetId },
          body: { ...terms, returnAccountId: values.returnAccountId ?? undefined },
        })
      : await postApiPortfolioDeposits({
          body: {
            ...terms,
            sourceAccountId: values.sourceAccountId!,
            returnAccountId: values.returnAccountId ?? undefined,
            groupId: values.groupId,
          },
        });

    if (!result.error && this.deposit && values.groupId !== (this.deposit.groupId ?? null)) {
      const assignmentError = await assignGroup(this.deposit.assetId, values.groupId);
      if (assignmentError) {
        this.submitting.set(false);
        this.formError.set(assignmentError);
        return;
      }
    }

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
      this.form.controls.principal.setErrors({ insufficientFunds: true });
      this.form.controls.principal.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
