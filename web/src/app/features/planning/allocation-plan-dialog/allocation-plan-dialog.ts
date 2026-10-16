import { Component, computed, inject, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import {
  getApiPortfolioAccounts,
  putApiPortfolioAllocationPlanVersionsByMonth,
  type AllocationBucketResponse,
  type AllocationResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { BUCKET_KIND, BUCKET_KINDS, SHARE_TYPE, SHARE_TYPES } from '../allocation';
import { MAX_MONTH, MIN_MONTH, monthInRange } from '../recurrence';

export interface AllocationPlanDialogData {
  month: string;
  allocation: AllocationResponse;
}

const MAX_NAME_LENGTH = 100;
const MAX_FIXED_SHARE = 1_000_000;
const MIN_SHARE = 0.01;

function shareInRange(control: AbstractControl): ValidationErrors | null {
  const row = control.parent;
  if (!row) {
    return null;
  }

  if (control.value === null || control.value === '') {
    return { required: true };
  }

  const share = Number(control.value);
  if (!Number.isFinite(share)) {
    return { shareRange: true };
  }

  if (row.get('shareType')!.value === SHARE_TYPE.Fixed) {
    return share >= MIN_SHARE && share <= MAX_FIXED_SHARE ? null : { shareRange: true };
  }

  const min = row.get('isRemainder')!.value ? 0 : MIN_SHARE;
  return share >= min && share <= 100 ? null : { shareRange: true };
}

function createRow(bucket?: AllocationBucketResponse) {
  const row = new FormGroup({
    bucketId: new FormControl<string | null>(bucket?.bucketId ?? null),
    name: new FormControl(bucket?.name ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)],
    }),
    // A bucket's kind is fixed once it exists.
    kind: new FormControl(
      {
        value: bucket ? Number(bucket.kind) : (BUCKET_KIND.Investments as number),
        disabled: !!bucket,
      },
      { nonNullable: true },
    ),
    shareType: new FormControl(bucket ? Number(bucket.shareType) : (SHARE_TYPE.Percent as number), {
      nonNullable: true,
    }),
    share: new FormControl<number | null>(bucket ? Number(bucket.share) : null, {
      validators: [shareInRange],
    }),
    isRemainder: new FormControl(bucket?.isRemainder ?? false, { nonNullable: true }),
    accountId: new FormControl<string | null>(bucket?.accountId ?? null),
  });
  row.controls.share.updateValueAndValidity({ emitEvent: false });

  return row;
}

type PlanRow = ReturnType<typeof createRow>;

@Component({
  selector: 'app-allocation-plan-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './allocation-plan-dialog.html',
  styleUrl: './allocation-plan-dialog.scss',
})
export class AllocationPlanDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AllocationPlanDialog>);
  private readonly data = inject<AllocationPlanDialogData>(MAT_DIALOG_DATA);

  protected readonly minMonth = MIN_MONTH;
  protected readonly maxMonth = MAX_MONTH;
  protected readonly bucketKinds = BUCKET_KINDS;
  protected readonly shareTypes = SHARE_TYPES;
  protected readonly fixedShareType = SHARE_TYPE.Fixed;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    effectiveFrom: [
      this.data.month,
      [Validators.required, Validators.pattern(/^\d{4}-\d{2}$/), monthInRange],
    ],
    rows: new FormArray(this.data.allocation.buckets.map((bucket) => createRow(bucket))),
  });

  private readonly rows = this.form.controls.rows;

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly rowControls = computed(() => {
    this.formValue();
    return [...this.rows.controls];
  });

  protected readonly percentSumHundredths = computed(() =>
    this.formValue()
      .rows.filter((row) => row.shareType === SHARE_TYPE.Percent)
      .reduce((sum, row) => sum + Math.round(Number(row.share ?? 0) * 100), 0),
  );

  protected readonly percentSum = computed(() => this.percentSumHundredths() / 100);

  protected readonly percentOver = computed(() => this.percentSumHundredths() > 10_000);

  protected readonly remainderMissing = computed(() => {
    const rows = this.formValue().rows;
    return rows.length > 0 && !rows.some((row) => row.isRemainder);
  });

  protected readonly duplicateNames = computed(() => {
    const names = this.formValue()
      .rows.map((row) => row.name.trim().toLowerCase())
      .filter((name) => name !== '');
    return new Set(names).size !== names.length;
  });

  protected readonly canSave = computed(() => {
    this.formValue();
    return (
      !this.submitting() &&
      this.form.valid &&
      !this.percentOver() &&
      !this.remainderMissing() &&
      !this.duplicateNames()
    );
  });

  protected readonly accountsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('assets.form.accountsLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected accountOptions(row: PlanRow): { id: string; name: string }[] {
    if (!this.accountsResource.hasValue()) {
      return [];
    }

    const current = row.controls.accountId.value;
    return this.accountsResource
      .value()
      .filter((account) => !account.isArchived || account.id === current);
  }

  protected addRow(): void {
    this.rows.push(createRow());
  }

  protected removeRow(row: PlanRow): void {
    this.rows.removeAt(this.rows.controls.indexOf(row));
  }

  protected canMove(row: PlanRow, delta: -1 | 1): boolean {
    const neighbour = this.rows.at(this.rows.controls.indexOf(row) + delta) as PlanRow | undefined;
    return !!neighbour && neighbour.controls.shareType.value === row.controls.shareType.value;
  }

  protected move(row: PlanRow, delta: -1 | 1): void {
    if (!this.canMove(row, delta)) {
      return;
    }

    const index = this.rows.controls.indexOf(row);
    this.rows.removeAt(index, { emitEvent: false });
    this.rows.insert(index + delta, row);
  }

  protected onShareTypeChanged(row: PlanRow): void {
    this.rows.removeAt(this.rows.controls.indexOf(row), { emitEvent: false });
    const fixedCount = this.rows.controls.filter(
      (candidate) => candidate.controls.shareType.value === SHARE_TYPE.Fixed,
    ).length;
    this.rows.insert(
      row.controls.shareType.value === SHARE_TYPE.Fixed ? fixedCount : this.rows.length,
      row,
      {
        emitEvent: false,
      },
    );
    this.revalidateShares();
  }

  protected setRemainder(row: PlanRow, checked: boolean): void {
    for (const other of this.rows.controls) {
      other.controls.isRemainder.setValue(other === row && checked, { emitEvent: false });
    }

    this.revalidateShares();
  }

  protected async onSubmit(): Promise<void> {
    if (!this.canSave()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const result = await putApiPortfolioAllocationPlanVersionsByMonth({
      path: { month: values.effectiveFrom },
      body: {
        buckets: values.rows.map((row) => ({
          bucketId: row.bucketId,
          name: row.name.trim(),
          kind: row.kind,
          shareType: row.shareType,
          share: Number(row.share),
          isRemainder: row.isRemainder,
          accountId: row.accountId,
        })),
      },
    });

    this.submitting.set(false);

    if (result.error) {
      this.formError.set(readProblemDetails(result.error).detail ?? translate('errors.generic'));
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private revalidateShares(): void {
    for (const row of this.rows.controls) {
      row.controls.share.updateValueAndValidity({ emitEvent: false });
    }

    this.form.updateValueAndValidity();
  }
}
