import { Component, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import type { AssetResponse } from '../../../../../api/portfolio';
import { DEFAULT_CURRENCY, SUPPORTED_CURRENCIES } from '../../../../../shared/currencies';

// Name + Currency — the two fields every asset form asks for.
export function createAssetBasicsGroup(fb: FormBuilder) {
  return fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    currency: [DEFAULT_CURRENCY, [Validators.required]],
  });
}

export type AssetBasicsGroup = ReturnType<typeof createAssetBasicsGroup>;

// Edit pre-fill. Called from the form's ngOnInit, because the `asset` input is not set yet while
// the form's FormGroup is being built.
export function fillAssetBasics(group: AssetBasicsGroup, asset: AssetResponse): void {
  group.patchValue({ name: asset.name, currency: asset.currency });
}

@Component({
  selector: 'app-asset-basics-fields',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './asset-basics-fields.html',
  styleUrl: './asset-basics-fields.scss',
})
export class AssetBasicsFields {
  readonly group = input.required<AssetBasicsGroup>();

  protected readonly currencies = SUPPORTED_CURRENCIES;
}
