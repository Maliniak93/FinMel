import { Component, computed, inject, resource, signal } from '@angular/core';
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
  putApiPortfolioRecurringIncomesByIdOccurrencesByMonthSettlement,
  type IncomeOccurrenceResponse,
} from '../../../api/portfolio';
import { readProblemDetails, type ApiProblemDetails } from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { currentWarsawDate } from '../recurrence';

const MIN_DATE = new Date(2011, 0, 1);

export interface SettleOccurrenceDialogData {
  occurrence: IncomeOccurrenceResponse;
}

@Component({
  selector: 'app-settle-occurrence-dialog',
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
  templateUrl: './settle-occurrence-dialog.html',
  styleUrl: './settle-occurrence-dialog.scss',
})
export class SettleOccurrenceDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<SettleOccurrenceDialog>);

  protected readonly occurrence = inject<SettleOccurrenceDialogData>(MAT_DIALOG_DATA).occurrence;
  protected readonly isEdit = this.occurrence.status === 'Settled';
  protected readonly minDate = MIN_DATE;
  protected readonly maxDate = currentWarsawDate();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    actualAmount: [
      Number(this.occurrence.actualAmount ?? this.occurrence.amount) as number | null,
      [Validators.required, Validators.min(0.01)],
    ],
    actualDate: [
      (this.occurrence.actualDate
        ? fromDateOnly(this.occurrence.actualDate)
        : currentWarsawDate()) as Date | null,
      [Validators.required, (control: AbstractControl) => this.withinRange(control)],
    ],
    accountId: [this.occurrence.accountId as string | null, [Validators.required]],
    createDeposit: [this.isEdit ? this.occurrence.depositCreated : true],
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
        (account) =>
          account.currency === this.occurrence.currency &&
          (!account.isArchived || (this.isEdit && account.id === this.occurrence.accountId)),
      );
    },
  });

  protected readonly accounts = computed(() =>
    this.accountsResource.hasValue() ? this.accountsResource.value() : [],
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
    const result = await putApiPortfolioRecurringIncomesByIdOccurrencesByMonthSettlement({
      path: { id: this.occurrence.recurringIncomeId, month: this.occurrence.nominalMonth },
      body: {
        actualAmount: Number(values.actualAmount),
        actualDate: toDateOnly(values.actualDate!),
        accountId: values.accountId!,
        createDeposit: values.createDeposit,
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
