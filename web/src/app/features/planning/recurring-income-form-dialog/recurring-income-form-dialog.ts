import { Component, computed, inject, resource, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  Validators,
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
  postApiPortfolioRecurringIncomes,
  putApiPortfolioRecurringIncomesById,
  type IncomeDefinitionResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { RecurrenceRuleFields } from '../recurrence-rule-fields/recurrence-rule-fields';
import {
  DAY_KIND,
  MAX_MONTH,
  MIN_MONTH,
  SHIFT,
  bindDayControls,
  currentWarsawMonth,
  monthInRange,
} from '../recurrence';

const MIN_DATE = new Date(2011, 0, 1);
const MAX_DATE = new Date(2099, 11, 31);
export interface RecurringIncomeFormDialogData {
  definition: IncomeDefinitionResponse;
}

@Component({
  selector: 'app-recurring-income-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    RecurrenceRuleFields,
    TranslocoPipe,
  ],
  templateUrl: './recurring-income-form-dialog.html',
  styleUrl: './recurring-income-form-dialog.scss',
})
export class RecurringIncomeFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<RecurringIncomeFormDialog>);

  private readonly editing =
    inject<RecurringIncomeFormDialogData | null>(MAT_DIALOG_DATA, { optional: true })?.definition ??
    null;

  protected readonly isEdit = this.editing !== null;
  protected readonly minDate = MIN_DATE;
  protected readonly maxDate = MAX_DATE;
  protected readonly minMonth = MIN_MONTH;
  protected readonly maxMonth = MAX_MONTH;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [this.editing?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    amount: [
      this.editing ? Number(this.editing.amount) : (null as number | null),
      [Validators.required, Validators.min(0.01)],
    ],
    accountId: [this.editing?.accountId ?? (null as string | null), [Validators.required]],
    effectiveFrom: [
      currentWarsawMonth(),
      [Validators.required, Validators.pattern(/^\d{4}-\d{2}$/), monthInRange],
    ],
    startDate: [
      this.editing ? fromDateOnly(this.editing.startDate) : (null as Date | null),
      [Validators.required, (control: AbstractControl) => this.withinRange(control)],
    ],
    endDate: [
      this.editing?.endDate ? fromDateOnly(this.editing.endDate) : (null as Date | null),
      [
        (control: AbstractControl) => this.withinRange(control),
        (c: AbstractControl) => this.endBeforeStart(c),
      ],
    ],
    intervalMonths: [
      this.editing ? Number(this.editing.intervalMonths) : 1,
      [Validators.required, Validators.min(1), Validators.max(12)],
    ],
    dayKind: [this.editing?.dayKind ?? (DAY_KIND.DayOfMonth as number)],
    dayOfMonth: [
      this.editing?.dayOfMonth != null ? Number(this.editing.dayOfMonth) : (1 as number | null),
      [Validators.required, Validators.min(1), Validators.max(31)],
    ],
    shift: [this.editing?.shift ?? (SHIFT.None as number)],
  });

  protected readonly dayKind = toSignal(this.form.controls.dayKind.valueChanges, {
    initialValue: this.form.controls.dayKind.value,
  });
  private readonly selectedAccountId = toSignal(this.form.controls.accountId.valueChanges, {
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
        (account) => !account.isArchived || account.id === this.editing?.accountId,
      );
    },
  });

  protected readonly accounts = computed(() =>
    this.accountsResource.hasValue() ? this.accountsResource.value() : [],
  );

  protected readonly selectedCurrency = computed(
    () => this.accounts().find((account) => account.id === this.selectedAccountId())?.currency,
  );

  constructor() {
    this.form.controls.startDate.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.endDate.updateValueAndValidity());

    bindDayControls(this.form.controls);

    if (!this.isEdit) {
      this.form.controls.effectiveFrom.disable();
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
    const usesDay = values.dayKind === DAY_KIND.DayOfMonth;
    const body = {
      name: values.name.trim(),
      amount: Number(values.amount),
      accountId: values.accountId!,
      startDate: toDateOnly(values.startDate!),
      endDate: values.endDate ? toDateOnly(values.endDate) : null,
      intervalMonths: Number(values.intervalMonths),
      dayKind: values.dayKind,
      dayOfMonth: usesDay ? Number(values.dayOfMonth) : null,
      shift: usesDay ? values.shift : SHIFT.None,
    };
    const result = this.editing
      ? await putApiPortfolioRecurringIncomesById({
          path: { id: this.editing.id },
          body: { ...body, effectiveFrom: values.effectiveFrom },
        })
      : await postApiPortfolioRecurringIncomes({ body });

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
    return value && (value < MIN_DATE || value > MAX_DATE) ? { dateOutOfRange: true } : null;
  }

  private endBeforeStart(control: AbstractControl): ValidationErrors | null {
    const end = control.value as Date | null;
    if (!end) {
      return null;
    }

    const start = this.form.controls.startDate.value;
    return start && end < start ? { endBeforeStart: true } : null;
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (problem.errorCode === 'Conflict.DuplicateName') {
      this.form.controls.name.setErrors({ server: translate('planning.errors.duplicateName') });
      this.form.controls.name.markAsTouched();
      return;
    }

    if (problem.errorCode === 'Conflict.AccountArchived') {
      this.form.controls.accountId.setErrors({
        server: translate('planning.errors.accountArchived'),
      });
      this.form.controls.accountId.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
