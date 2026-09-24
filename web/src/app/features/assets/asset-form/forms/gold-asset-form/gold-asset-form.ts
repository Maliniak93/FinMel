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
  createInstrumentControl,
  INSTRUMENT_REQUIRED_MESSAGE,
  InstrumentPicker,
} from '../../blocks/instrument-picker/instrument-picker';

// PreciousMetal: market-valued like a security, but with no custom-ticker panel. Its only provider
// (NBP) has no notion of an arbitrary user-typed ticker — AddCustomInstrumentHandler always rejects
// it with Validation.UnsupportedInstrumentSource (AssetClassPriceSourceMapping, M1.6) — and the one
// real gold instrument is already seeded Verified and reachable through search.
@Component({
  selector: 'app-gold-asset-form',
  imports: [AssetBasicsFields, FirstTransactionFields, InstrumentPicker],
  templateUrl: './gold-asset-form.html',
  styleUrl: './gold-asset-form.scss',
  providers: [{ provide: ASSET_FORM, useExisting: forwardRef(() => GoldAssetForm) }],
})
export class GoldAssetForm implements AssetForm, OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly assetClass = input.required<AssetClass>();
  readonly asset = input<AssetResponse>();

  protected readonly isEdit = computed(() => !!this.asset());

  readonly form = this.formBuilder.nonNullable.group({
    basics: createAssetBasicsGroup(this.formBuilder),
    instrument: createInstrumentControl(),
    initialTransaction: createFirstTransactionGroup(this.formBuilder),
  });

  ngOnInit(): void {
    const asset = this.asset();
    if (asset) {
      fillAssetBasics(this.form.controls.basics, asset);
    }
  }

  submitBlockedReason(): string | null {
    return this.form.controls.instrument.value ? null : INSTRUMENT_REQUIRED_MESSAGE;
  }

  toBody(): AssetFormBody {
    return assetFormBody(
      {
        assetClass: this.assetClass(),
        ...this.form.controls.basics.getRawValue(),
        instrumentId: this.form.controls.instrument.value?.id ?? null,
      },
      this.form.controls.initialTransaction,
      this.isEdit(),
    );
  }
}
