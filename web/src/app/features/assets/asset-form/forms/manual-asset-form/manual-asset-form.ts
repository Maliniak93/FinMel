import { Component, computed, forwardRef, inject, input, type OnInit } from '@angular/core';
import { FormBuilder } from '@angular/forms';

import type { AssetClass, AssetResponse } from '../../../../../api/portfolio';
import { ASSET_FORM, assetFormBody, type AssetForm, type AssetFormBody } from '../../asset-form';
import {
  AssetBasicsFields,
  createAssetBasicsGroup,
  fillAssetBasics,
} from '../../blocks/asset-basics-fields/asset-basics-fields';
import {
  createFirstTransactionGroup,
  FirstTransactionFields,
} from '../../blocks/first-transaction-fields/first-transaction-fields';
import {
  buildManualValueFields,
  createManualValueGroup,
  fillManualValue,
  ManualValueFields,
} from '../../blocks/manual-value-fields/manual-value-fields';

// RealEstate and Other: valued by hand — a value and the date it was valued on.
@Component({
  selector: 'app-manual-asset-form',
  imports: [AssetBasicsFields, FirstTransactionFields, ManualValueFields],
  templateUrl: './manual-asset-form.html',
  styleUrl: './manual-asset-form.scss',
  providers: [{ provide: ASSET_FORM, useExisting: forwardRef(() => ManualAssetForm) }],
})
export class ManualAssetForm implements AssetForm, OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly assetClass = input.required<AssetClass>();
  readonly asset = input<AssetResponse>();

  protected readonly isEdit = computed(() => !!this.asset());

  readonly form = this.formBuilder.nonNullable.group({
    basics: createAssetBasicsGroup(this.formBuilder),
    valuation: createManualValueGroup(this.formBuilder),
    initialTransaction: createFirstTransactionGroup(this.formBuilder),
  });

  ngOnInit(): void {
    const asset = this.asset();
    if (asset) {
      fillAssetBasics(this.form.controls.basics, asset);
      fillManualValue(this.form.controls.valuation, asset);
    }
  }

  toBody(): AssetFormBody {
    return assetFormBody(
      {
        assetClass: this.assetClass(),
        ...this.form.controls.basics.getRawValue(),
        ...buildManualValueFields(this.form.controls.valuation),
      },
      this.form.controls.initialTransaction,
      this.isEdit(),
    );
  }
}
