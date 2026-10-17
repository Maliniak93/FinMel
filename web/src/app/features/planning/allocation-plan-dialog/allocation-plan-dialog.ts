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
import {
  BUCKET_KIND,
  BUCKET_KINDS,
  MAX_TARGET_MONTHS,
  MIN_TARGET_MONTHS,
  SHARE_TYPE,
  SHARE_TYPES,
  isLimitedKind,
  wrapperForKind,
} from '../allocation';
import { formatMonthName } from '../../../shared/format';
import { MAX_MONTH, MIN_MONTH, monthInRange } from '../recurrence';

export interface AllocationPlanDialogData {
  month: string;
  allocation: AllocationResponse;
}

const MAX_NAME_LENGTH = 100;
const MAX_FIXED_SHARE = 1_000_000;
const MIN_SHARE = 0.01;

function isCushionRow(row: AbstractControl | null): boolean {
  return row?.get('kind')?.value === BUCKET_KIND.Cushion;
}

function targetMonthsInRange(control: AbstractControl): ValidationErrors | null {
  if (!isCushionRow(control.parent)) {
    return null;
  }

  const months = Number(control.value);
  return control.value !== null &&
    control.value !== '' &&
    Number.isInteger(months) &&
    months >= MIN_TARGET_MONTHS &&
    months <= MAX_TARGET_MONTHS
    ? null
    : { targetMonthsRange: true };
}

function startingBalanceNotNegative(control: AbstractControl): ValidationErrors | null {
  if (control.value === null || control.value === '') {
    return null;
  }

  const balance = Number(control.value);
  return Number.isFinite(balance) && balance >= 0 ? null : { startingBalanceRange: true };
}

function shareInRange(control: AbstractControl): ValidationErrors | null {
  const row = control.parent;
  if (!row || isCushionRow(row)) {
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

function createRow(bucket?: AllocationBucketResponse, cushion = false) {
  const row = new FormGroup({
    bucketId: new FormControl<string | null>(bucket?.bucketId ?? null),
    name: new FormControl(bucket?.name ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)],
    }),
    // A bucket's kind is fixed once it exists.
    kind: new FormControl(
      {
        value: bucket
          ? Number(bucket.kind)
          : cushion
            ? (BUCKET_KIND.Cushion as number)
            : (BUCKET_KIND.Investments as number),
        disabled: !!bucket || cushion,
      },
      { nonNullable: true },
    ),
    shareType: new FormControl(
      bucket?.shareType != null ? Number(bucket.shareType) : (SHARE_TYPE.Percent as number),
      { nonNullable: true },
    ),
    share: new FormControl<number | null>(bucket?.share != null ? Number(bucket.share) : null, {
      validators: [shareInRange],
    }),
    isRemainder: new FormControl(bucket?.isRemainder ?? false, { nonNullable: true }),
    accountId: new FormControl<string | null>(bucket?.accountId ?? null),
    targetMonths: new FormControl<number | null>(
      bucket?.cushion ? Number(bucket.cushion.targetMonths) : null,
      { validators: [targetMonthsInRange] },
    ),
    startingBalance: new FormControl<number | null>(null, {
      validators: [startingBalanceNotNegative],
    }),
  });
  row.controls.share.updateValueAndValidity({ emitEvent: false });
  row.controls.targetMonths.updateValueAndValidity({ emitEvent: false });

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
    reserveAccountId: new FormControl<string | null>(
      this.data.allocation.reserve?.accountId ?? null,
    ),
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
      .rows.filter(
        (row) => row.kind !== BUCKET_KIND.Cushion && row.shareType === SHARE_TYPE.Percent,
      )
      .reduce((sum, row) => sum + Math.round(Number(row.share ?? 0) * 100), 0),
  );

  protected readonly contributedPeriod = computed(() => {
    const month = this.formValue().effectiveFrom;
    return /^\d{4}-\d{2}$/.test(month)
      ? { year: month.slice(0, 4), month: formatMonthName(`${month}-01`) }
      : { year: '', month: '' };
  });

  protected readonly percentSum = computed(() => this.percentSumHundredths() / 100);

  protected readonly percentOver = computed(() => this.percentSumHundredths() > 10_000);

  protected readonly hasCushion = computed(() =>
    this.formValue().rows.some((row) => row.kind === BUCKET_KIND.Cushion),
  );

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
    const wrapper = wrapperForKind(row.controls.kind.value);
    return this.accountsResource
      .value()
      .filter(
        (account) =>
          (!account.isArchived || account.id === current) &&
          (wrapper === null || Number(account.wrapper) === wrapper),
      );
  }

  protected reserveAccountOptions(): { id: string; name: string }[] {
    if (!this.accountsResource.hasValue()) {
      return [];
    }

    const current = this.form.controls.reserveAccountId.value;
    return this.accountsResource
      .value()
      .filter((account) => !account.isArchived || account.id === current);
  }

  protected isLimited(row: PlanRow): boolean {
    return isLimitedKind(row.controls.kind.value);
  }

  protected asksContributed(row: PlanRow): boolean {
    return this.isLimited(row) && row.controls.bucketId.value === null;
  }

  protected onKindChanged(row: PlanRow): void {
    if (this.isLimited(row)) {
      row.controls.isRemainder.setValue(false, { emitEvent: false });
      const accountId = row.controls.accountId.value;
      if (!accountId || !this.accountOptions(row).some((account) => account.id === accountId)) {
        row.controls.accountId.setValue(null, { emitEvent: false });
      }
    }

    this.revalidateShares();
  }

  protected addRow(): void {
    this.rows.push(createRow());
  }

  protected addCushion(): void {
    if (!this.hasCushion()) {
      this.rows.insert(0, createRow(undefined, true));
    }
  }

  protected isCushion(row: PlanRow): boolean {
    return row.controls.kind.value === BUCKET_KIND.Cushion;
  }

  protected removeRow(row: PlanRow): void {
    this.rows.removeAt(this.rows.controls.indexOf(row));
  }

  protected canMove(row: PlanRow, delta: -1 | 1): boolean {
    const neighbour = this.rows.at(this.rows.controls.indexOf(row) + delta) as PlanRow | undefined;
    return (
      !!neighbour &&
      !this.isCushion(row) &&
      !this.isCushion(neighbour) &&
      neighbour.controls.shareType.value === row.controls.shareType.value
    );
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
    const leading = this.rows.controls.filter(
      (candidate) =>
        this.isCushion(candidate) || candidate.controls.shareType.value === SHARE_TYPE.Fixed,
    ).length;
    this.rows.insert(
      row.controls.shareType.value === SHARE_TYPE.Fixed ? leading : this.rows.length,
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
        reserveAccountId: values.reserveAccountId,
        buckets: values.rows.map((row) =>
          row.kind === BUCKET_KIND.Cushion
            ? {
                bucketId: row.bucketId,
                name: row.name.trim(),
                kind: row.kind,
                isRemainder: false,
                accountId: row.accountId,
                targetMonths: Number(row.targetMonths),
                startingBalance:
                  row.bucketId === null && row.startingBalance !== null
                    ? Number(row.startingBalance)
                    : null,
              }
            : {
                bucketId: row.bucketId,
                name: row.name.trim(),
                kind: row.kind,
                shareType: row.shareType,
                share: Number(row.share),
                isRemainder: row.isRemainder,
                accountId: row.accountId,
                startingBalance:
                  isLimitedKind(row.kind) && row.bucketId === null && row.startingBalance !== null
                    ? Number(row.startingBalance)
                    : null,
              },
        ),
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
