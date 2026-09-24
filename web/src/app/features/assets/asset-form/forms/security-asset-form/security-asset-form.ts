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

// Stock, Etf, Bond and Crypto: market-valued off an instrument, which the user picks from search or
// adds through the ADR-018 custom-ticker panel.
@Component({
  selector: 'app-security-asset-form',
  imports: [AssetBasicsFields, FirstTransactionFields, InstrumentPicker],
  templateUrl: './security-asset-form.html',
  styleUrl: './security-asset-form.scss',
  providers: [{ provide: ASSET_FORM, useExisting: forwardRef(() => SecurityAssetForm) }],
})
export class SecurityAssetForm implements AssetForm, OnInit {
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
