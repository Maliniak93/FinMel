import { Component, computed, inject, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioAccounts,
  putApiPortfolioOneOffExpensesByIdSettlement,
  putApiPortfolioRecurringExpensesByIdPaymentsByMonthSettlement,
  type ExpensePaymentResponse,
  type OneOffExpenseResponse,
} from '../../../api/portfolio';
import { readProblemDetails, type ApiProblemDetails } from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { currencySuffix } from '../../../shared/format';
import { currentWarsawDate } from '../recurrence';

const MIN_DATE = new Date(2011, 0, 1);

export type SettleExpenseDialogData =
  | { payment: ExpensePaymentResponse; oneOffExpense?: undefined }
  | { oneOffExpense: OneOffExpenseResponse; payment?: undefined };

@Component({
  selector: 'app-settle-expense-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './settle-expense-dialog.html',
  styleUrl: './settle-expense-dialog.scss',
})
export class SettleExpenseDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<SettleExpenseDialog>);

  private readonly data = inject<SettleExpenseDialogData>(MAT_DIALOG_DATA);
  protected readonly expense: ExpensePaymentResponse | OneOffExpenseResponse =
    this.data.payment ?? this.data.oneOffExpense;
  protected readonly isEdit = this.expense.status === 'Settled';
  protected readonly minDate = MIN_DATE;
  protected readonly maxDate = currentWarsawDate();

  private readonly plannedAccountId = this.data.payment
    ? this.data.payment.plannedAccountId
    : this.data.oneOffExpense.accountId;

  protected readonly currencySuffix = currencySuffix;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    actualAmount: [
      Number(this.expense.actualAmount ?? this.expense.amount) as number | null,
      [Validators.required, Validators.min(0.01)],
    ],
    actualDate: [
      this.initialDate() as Date | null,
      [Validators.required, (control: AbstractControl) => this.withinRange(control)],
    ],
    accountId: [this.expense.accountId as string | null, [Validators.required]],
    createWithdrawal: [this.isEdit ? this.expense.withdrawalCreated : true],
    rememberAccount: [false],
  });

  protected readonly selectedAccountId = toSignal(this.form.controls.accountId.valueChanges, {
    initialValue: this.form.controls.accountId.value,
  });

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('planning.form.accountsLoadFailed'),
        );
      }
      return (result.data ?? []).filter(
        (account) => !account.isArchived || (this.isEdit && account.id === this.expense.accountId),
      );
    },
  });

  protected readonly accounts = computed(() =>
    this.accountsResource.hasValue() ? this.accountsResource.value() : [],
  );

  protected readonly currency = computed(
    () =>
      this.accounts().find((account) => account.id === this.selectedAccountId())?.currency ??
      this.expense.currency ??
      'PLN',
  );

  protected readonly canRemember = computed(() => {
    const accountId = this.selectedAccountId();
    return accountId !== null && accountId !== this.plannedAccountId;
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
    const body = {
      actualAmount: Number(values.actualAmount),
      actualDate: toDateOnly(values.actualDate!),
      accountId: values.accountId!,
      createWithdrawal: values.createWithdrawal,
      rememberAccount: this.canRemember() && values.rememberAccount,
    };
    const result = this.data.oneOffExpense
      ? await putApiPortfolioOneOffExpensesByIdSettlement({
          path: { id: this.data.oneOffExpense.id },
          body,
        })
      : await putApiPortfolioRecurringExpensesByIdPaymentsByMonthSettlement({
          path: {
            id: this.data.payment.recurringExpenseId,
            month: this.data.payment.nominalMonth,
          },
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

  private initialDate(): Date {
    const today = currentWarsawDate();
    const date = fromDateOnly(this.expense.actualDate ?? this.expense.date);
    return date > today ? today : date;
  }

  private withinRange(control: AbstractControl): ValidationErrors | null {
    const value = control.value as Date | null;
    if (!value) {
      return null;
    }
    if (value < MIN_DATE) {
      return { dateOutOfRange: true };
    }
    return value > currentWarsawDate() ? { dateInFuture: true } : null;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (problem.errorCode === 'Conflict.AccountArchived') {
      this.form.controls.accountId.setErrors({
        server: translate('planning.errors.accountArchived'),
      });
      this.form.controls.accountId.markAsTouched();
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
