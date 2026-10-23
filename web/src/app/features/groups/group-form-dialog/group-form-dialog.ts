import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  postApiPortfolioAssetGroups,
  putApiPortfolioAssetGroupsById,
  type AssetGroupResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { GROUP_COLOR_KEYS, groupColor } from '../../../shared/colors';
import { formatPercentNumber } from '../../../shared/format';
import { ASSET_CLASSES } from '../../assets/asset-class';
import { GROUP_KINDS, GROUP_KIND } from '../group-kind';

export type GroupFormDialogData =
  | { mode: 'create'; groups: readonly AssetGroupResponse[] }
  | { mode: 'edit'; group: AssetGroupResponse; groups: readonly AssetGroupResponse[] };

interface AllocationRowControls {
  assetClass: FormControl<number | null>;
  percent: FormControl<number | null>;
}

type AllocationRow = FormGroup<AllocationRowControls>;

function oneDecimal(control: AbstractControl): ValidationErrors | null {
  const value = control.value as number | null;
  if (value === null || value === undefined) {
    return null;
  }
  const tenths = Number(value) * 10;
  return Math.abs(tenths - Math.round(tenths)) < 1e-6 ? null : { decimals: true };
}

function allocationRows(array: AbstractControl): AllocationRow[] {
  return (array as FormArray<AllocationRow>).controls;
}

function tenthsOf(rows: AllocationRow[]): number {
  return rows.reduce(
    (sum, row) => sum + Math.round(Number(row.controls.percent.value ?? 0) * 10),
    0,
  );
}

function sumsToHundred(array: AbstractControl): ValidationErrors | null {
  const rows = allocationRows(array);
  return rows.length === 0 || tenthsOf(rows) === 1000 ? null : { allocationSum: true };
}

function uniqueClasses(array: AbstractControl): ValidationErrors | null {
  const classes = allocationRows(array)
    .map((row) => row.controls.assetClass.value)
    .filter((value) => value !== null);
  return new Set(classes).size === classes.length ? null : { duplicateClass: true };
}

@Component({
  selector: 'app-group-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './group-form-dialog.html',
  styleUrl: './group-form-dialog.scss',
})
export class GroupFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<GroupFormDialog>);
  protected readonly data = inject<GroupFormDialogData>(MAT_DIALOG_DATA);

  protected readonly mode = this.data.mode;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly kinds = GROUP_KINDS;
  protected readonly assetClasses = ASSET_CLASSES;

  private readonly ownColor = this.data.mode === 'edit' ? this.data.group.color : null;
  private readonly takenColors = new Set(
    this.data.groups.map((group) => group.color).filter((color) => color !== this.ownColor),
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [
      this.data.mode === 'edit' ? this.data.group.name : '',
      [Validators.required, Validators.maxLength(50)],
    ],
    color: [
      this.ownColor ?? GROUP_COLOR_KEYS.find((key) => !this.takenColors.has(key)) ?? '',
      [Validators.required],
    ],
    targetPln: this.formBuilder.nonNullable.control<number | null>(
      this.data.mode === 'edit' && this.data.group.targetPln !== null
        ? Number(this.data.group.targetPln)
        : null,
      [Validators.min(0.01)],
    ),
    kind: this.formBuilder.nonNullable.control<number>(
      this.data.mode === 'edit' ? Number(this.data.group.kind) : GROUP_KIND.Regular,
    ),
    monthlyContributionPln: this.formBuilder.nonNullable.control<number | null>(
      this.data.mode === 'edit' ? Number(this.data.group.monthlyContributionPln) : 0,
      [Validators.min(0), Validators.max(1_000_000_000)],
    ),
    allocation: new FormArray<AllocationRow>(
      this.data.mode === 'edit'
        ? this.data.group.classAllocation.map((share) =>
            this.newRow(Number(share.assetClass), Number(share.percent)),
          )
        : [],
      [sumsToHundred, uniqueClasses],
    ),
  });

  private readonly selectedColor = toSignal(this.form.controls.color.valueChanges, {
    initialValue: this.form.controls.color.value,
  });

  private readonly formChange = toSignal(this.form.valueChanges, { initialValue: null });

  protected readonly palette = computed(() =>
    GROUP_COLOR_KEYS.map((key) => ({
      key,
      css: groupColor(key),
      label: `groups.colors.${key}`,
      disabled: this.takenColors.has(key),
      selected: this.selectedColor() === key,
    })),
  );

  protected readonly sumPercent = computed(() => {
    this.formChange();
    return tenthsOf(allocationRows(this.form.controls.allocation)) / 10;
  });

  protected readonly formatPercentNumber = formatPercentNumber;

  constructor() {
    this.applyKind(this.form.controls.kind.value);
    this.form.controls.kind.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((kind) => this.applyKind(kind));
  }

  protected pickColor(key: string): void {
    this.form.controls.color.setValue(key);
    this.form.controls.color.markAsDirty();
  }

  protected addAllocationRow(): void {
    this.form.controls.allocation.push(this.newRow(null, null));
  }

  protected removeAllocationRow(index: number): void {
    this.form.controls.allocation.removeAt(index);
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
    const body = {
      name: values.name.trim(),
      color: values.color,
      targetPln: values.targetPln,
      kind: values.kind,
      monthlyContributionPln: values.monthlyContributionPln ?? 0,
      classAllocation: values.allocation.flatMap((row) =>
        row.assetClass === null || row.percent === null
          ? []
          : [{ assetClass: row.assetClass, percent: row.percent }],
      ),
    };
    const data = this.data;
    const result =
      data.mode === 'create'
        ? await postApiPortfolioAssetGroups({ body })
        : await putApiPortfolioAssetGroupsById({ path: { id: data.group.id }, body });

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

  private newRow(assetClass: number | null, percent: number | null): AllocationRow {
    return new FormGroup<AllocationRowControls>({
      assetClass: new FormControl<number | null>(assetClass, [Validators.required]),
      percent: new FormControl<number | null>(percent, [
        Validators.required,
        Validators.min(0.1),
        Validators.max(100),
        oneDecimal,
      ]),
    });
  }

  private applyKind(kind: number): void {
    const target = this.form.controls.targetPln;
    target.setValidators(
      kind === GROUP_KIND.Cushion
        ? [Validators.required, Validators.min(0.01)]
        : [Validators.min(0.01)],
    );
    target.updateValueAndValidity();
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    const mapped: Record<
      string,
      ['name' | 'color' | 'targetPln' | 'monthlyContributionPln', string]
    > = {
      'Conflict.DuplicateName': ['name', 'groups.errors.duplicateName'],
      'Conflict.GroupColorTaken': ['color', 'groups.errors.colorTaken'],
      'Validation.InvalidTarget': ['targetPln', 'groups.errors.invalidTarget'],
      'Validation.TargetRequiredForCushion': ['targetPln', 'groups.form.targetRequired'],
      'Validation.InvalidContribution': [
        'monthlyContributionPln',
        'groups.errors.invalidContribution',
      ],
    };
    const target = problem.errorCode ? mapped[problem.errorCode] : undefined;
    if (target) {
      const control = this.form.controls[target[0]];
      control.setErrors({ server: translate(target[1]) });
      control.markAsTouched();
      return;
    }

    if (problem.errorCode === 'Conflict.GroupLimitReached') {
      this.formError.set(translate('groups.errors.limitReached'));
      return;
    }

    if (problem.errorCode === 'Conflict.CushionGroupExists') {
      this.formError.set(translate('groups.errors.cushionExists'));
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
