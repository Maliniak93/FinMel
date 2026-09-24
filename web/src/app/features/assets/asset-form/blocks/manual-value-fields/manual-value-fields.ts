import { Component, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import type { AssetResponse } from '../../../../../api/portfolio';
import { fromDateOnly, toDateOnly } from '../../../../../shared/date-only';

// A manually valued asset's value and the date it was valued on — both required: the form that
// renders this block has no other way to value the asset (AddAssetRequest/UpdateAssetRequest's
// "ManualValue + ManualValueDate together" rule).
export function createManualValueGroup(fb: FormBuilder) {
  return fb.nonNullable.group({
    manualValue: [0, [Validators.required, Validators.min(0)]],
    manualValueDate: [new Date(), [Validators.required]],
  });
}

export type ManualValueGroup = ReturnType<typeof createManualValueGroup>;

// Edit pre-fill — the stored DateOnly becomes local midnight (fromDateOnly), never a UTC parse.
export function fillManualValue(group: ManualValueGroup, asset: AssetResponse): void {
  group.patchValue({
    manualValue: Number(asset.manualValue ?? 0),
    manualValueDate: asset.manualValueDate ? fromDateOnly(asset.manualValueDate) : new Date(),
  });
}

export function buildManualValueFields(group: ManualValueGroup): {
  manualValue: number;
  manualValueDate: string;
} {
  const values = group.getRawValue();
  return {
    manualValue: values.manualValue,
    manualValueDate: toDateOnly(values.manualValueDate),
  };
}

@Component({
  selector: 'app-manual-value-fields',
  imports: [ReactiveFormsModule, MatDatepickerModule, MatFormFieldModule, MatInputModule],
  templateUrl: './manual-value-fields.html',
  styleUrl: './manual-value-fields.scss',
})
export class ManualValueFields {
  readonly group = input.required<ManualValueGroup>();
}
