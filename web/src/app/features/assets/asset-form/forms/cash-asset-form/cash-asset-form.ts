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

// Cash and Deposit: currency-valued (Quantity × FxRate), so there is no instrument and no manual
// value — the body is just the basics, plus an opening Deposit when the first transaction is added.
@Component({
  selector: 'app-cash-asset-form',
  imports: [AssetBasicsFields, FirstTransactionFields],
  templateUrl: './cash-asset-form.html',
  styleUrl: './cash-asset-form.scss',
  providers: [{ provide: ASSET_FORM, useExisting: forwardRef(() => CashAssetForm) }],
})
export class CashAssetForm implements AssetForm, OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly assetClass = input.required<AssetClass>();
  readonly asset = input<AssetResponse>();

  protected readonly isEdit = computed(() => !!this.asset());

  readonly form = this.formBuilder.nonNullable.group({
    basics: createAssetBasicsGroup(this.formBuilder),
    initialTransaction: createFirstTransactionGroup(this.formBuilder, true),
  });

  ngOnInit(): void {
    const asset = this.asset();
    if (asset) {
      fillAssetBasics(this.form.controls.basics, asset);
    }
  }

  toBody(): AssetFormBody {
    return assetFormBody(
      { assetClass: this.assetClass(), ...this.form.controls.basics.getRawValue() },
      this.form.controls.initialTransaction,
      this.isEdit(),
    );
  }
}
