import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
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
import { GROUP_COLORS } from '../group-color';

export type GroupFormDialogData =
  | { mode: 'create'; groups: readonly AssetGroupResponse[] }
  | { mode: 'edit'; group: AssetGroupResponse; groups: readonly AssetGroupResponse[] };

@Component({
  selector: 'app-group-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
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
      this.ownColor ?? GROUP_COLORS.find((color) => !this.takenColors.has(color.key))?.key ?? '',
      [Validators.required],
    ],
    targetPln: this.formBuilder.nonNullable.control<number | null>(
      this.data.mode === 'edit' && this.data.group.targetPln !== null
        ? Number(this.data.group.targetPln)
        : null,
      [Validators.min(0.01)],
    ),
  });

  private readonly selectedColor = toSignal(this.form.controls.color.valueChanges, {
    initialValue: this.form.controls.color.value,
  });

  protected readonly palette = computed(() =>
    GROUP_COLORS.map((color) => ({
      ...color,
      label: `groups.colors.${color.key}`,
      disabled: this.takenColors.has(color.key),
      selected: this.selectedColor() === color.key,
    })),
  );

  protected pickColor(key: string): void {
    this.form.controls.color.setValue(key);
    this.form.controls.color.markAsDirty();
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

  private applyServerErrors(problem: ApiProblemDetails): void {
    const mapped: Record<string, ['name' | 'color' | 'targetPln', string]> = {
      'Conflict.DuplicateName': ['name', 'groups.errors.duplicateName'],
      'Conflict.GroupColorTaken': ['color', 'groups.errors.colorTaken'],
      'Validation.InvalidTarget': ['targetPln', 'groups.errors.invalidTarget'],
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

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
