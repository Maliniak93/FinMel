import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  type ValidationErrors,
  type ValidatorFn,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { DateAdapter, MAT_DATE_FORMATS } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';

import { activeLocale } from '../../../core/i18n/language';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatDate } from '../../../shared/format';
import { RANGE_DATE_FORMATS, RangeDateAdapter } from './range-date';

export interface RangeDialogResult {
  readonly from: string;
  readonly to: string;
}

export interface RangeDialogData {
  readonly firstDate: string;
  readonly lastDate: string;
  readonly current: RangeDialogResult | null;
}

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

@Component({
  selector: 'app-range-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    TranslocoPipe,
  ],
  providers: [
    { provide: DateAdapter, useClass: RangeDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: RANGE_DATE_FORMATS },
  ],
  templateUrl: './range-dialog.html',
  styleUrl: './range-dialog.scss',
})
export class RangeDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject<MatDialogRef<RangeDialog, RangeDialogResult>>(MatDialogRef);
  private readonly data = inject<RangeDialogData>(MAT_DIALOG_DATA);

  private readonly firstDate = fromDateOnly(this.data.firstDate);
  private readonly lastDate = fromDateOnly(this.data.lastDate);

  protected readonly formatDate = formatDate;
  protected readonly firstDateText = this.data.firstDate;
  protected readonly lastDateText = this.data.lastDate;

  protected readonly years = Array.from(
    { length: this.lastDate.getFullYear() - this.firstDate.getFullYear() + 1 },
    (_, index) => this.firstDate.getFullYear() + index,
  );

  private readonly fromRule: ValidatorFn = (control): ValidationErrors | null => {
    const from = control.value as Date | null;
    if (!from) {
      return null;
    }
    const to = control.parent?.get('to')?.value as Date | null | undefined;
    if (to && from > to) {
      return { rangeOrder: true };
    }
    return from > this.lastDate ? { rangeOutside: true } : null;
  };

  private readonly toRule: ValidatorFn = (control): ValidationErrors | null => {
    const to = control.value as Date | null;
    return to && to < this.firstDate ? { rangeOutside: true } : null;
  };

  protected readonly form = this.formBuilder.nonNullable.group({
    from: [
      this.data.current ? fromDateOnly(this.data.current.from) : (null as Date | null),
      [Validators.required, this.fromRule],
    ],
    to: [
      this.data.current ? fromDateOnly(this.data.current.to) : (null as Date | null),
      [Validators.required, this.toRule],
    ],
  });

  constructor() {
    inject(DateAdapter).setLocale(activeLocale());
    this.form.controls.to.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.from.updateValueAndValidity({ emitEvent: false }));
  }

  protected pickYear(year: number): void {
    const now = today();
    this.form.patchValue({
      from: new Date(year, 0, 1),
      to: year === now.getFullYear() ? now : new Date(year, 11, 31),
    });
  }

  protected apply(): void {
    const { from, to } = this.form.getRawValue();
    if (this.form.invalid || !from || !to) {
      this.form.markAllAsTouched();
      return;
    }
    this.dialogRef.close({ from: toDateOnly(from), to: toDateOnly(to) });
  }
}
