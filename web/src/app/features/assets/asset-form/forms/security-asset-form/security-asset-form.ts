import { Component, computed, forwardRef, inject, input, type OnInit } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule } from '@angular/forms';
import { translate } from '@jsverse/transloco';

import type { AssetClass, AssetResponse } from '../../../../../api/portfolio';
import { GroupSelect } from '../../../../../shared/group-select/group-select';
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

@Component({
  selector: 'app-security-asset-form',
  imports: [
    ReactiveFormsModule,
    AssetBasicsFields,
    FirstTransactionFields,
    GroupSelect,
    InstrumentPicker,
  ],
  templateUrl: './security-asset-form.html',
  styleUrl: './security-asset-form.scss',
  providers: [{ provide: ASSET_FORM, useExisting: forwardRef(() => SecurityAssetForm) }],
})
export class SecurityAssetForm implements AssetForm, OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly assetClass = input.required<AssetClass>();
  readonly asset = input<AssetResponse>();
  readonly instrumentId = input<string>();

  protected readonly isEdit = computed(() => !!this.asset());

  readonly form = this.formBuilder.nonNullable.group({
    basics: createAssetBasicsGroup(this.formBuilder),
    instrument: createInstrumentControl(),
    initialTransaction: createFirstTransactionGroup(this.formBuilder),
    groupId: new FormControl<string | null>(null),
  });

  protected readonly currency = toSignal(this.form.controls.basics.controls.currency.valueChanges, {
    initialValue: this.form.controls.basics.controls.currency.value,
  });

  constructor() {
    const currency = this.form.controls.basics.controls.currency;
    this.form.controls.instrument.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((instrument) => {
        if (instrument) {
          currency.setValue(instrument.quoteCurrency);
          currency.disable();
        } else {
          currency.enable();
        }
      });
  }

  ngOnInit(): void {
    const asset = this.asset();
    if (asset) {
      fillAssetBasics(this.form.controls.basics, asset);
      this.form.controls.groupId.setValue(asset.groupId ?? null);
    }
  }

  submitBlockedReason(): string | null {
    return this.form.controls.instrument.value ? null : translate(INSTRUMENT_REQUIRED_MESSAGE);
  }

  toBody(): AssetFormBody {
    const body = assetFormBody(
      {
        assetClass: this.assetClass(),
        ...this.form.controls.basics.getRawValue(),
        instrumentId: this.form.controls.instrument.value?.id ?? null,
      },
      this.form.controls.initialTransaction,
      this.isEdit(),
    );
    return this.isEdit() ? body : { ...body, groupId: this.form.controls.groupId.value };
  }
}
