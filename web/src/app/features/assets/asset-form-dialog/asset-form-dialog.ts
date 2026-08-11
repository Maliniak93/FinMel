import { Component, effect, inject, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule, type MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';

import {
  getApiMarketdataInstrumentsById,
  getApiMarketdataInstrumentsSearch,
  postApiMarketdataInstruments,
  type CustomInstrumentResponse,
  type InstrumentDetailsResponse,
  type InstrumentSearchResult,
} from '../../../api/marketdata';
import {
  postApiPortfolioPortfoliosByPortfolioIdAssets,
  putApiPortfolioPortfoliosByPortfolioIdAssetsById,
  type AssetResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { DEFAULT_CURRENCY } from '../../../shared/currencies';
import { formatMoney } from '../../../shared/format-money';
import { ASSET_CLASSES } from '../asset-class';
import { CUSTOM_INSTRUMENT_SOURCES } from '../price-source';

export interface AssetFormDialogData {
  portfolioId: string;
  asset?: AssetResponse;
}

type Mode = 'manual' | 'market';
type InstrumentOption = InstrumentSearchResult | InstrumentDetailsResponse | CustomInstrumentResponse;

function toDateOnly(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Constructs local midnight for the given calendar date instead of `new Date(dateOnly)`, which
// parses as UTC midnight and shifts a day back in any negative-UTC-offset timezone — the exact
// inverse of toDateOnly() above, so the round-trip never drifts regardless of the user's offset.
function fromDateOnly(dateOnly: string): Date {
  const [year, month, day] = dateOnly.split('-').map(Number);
  return new Date(year, month - 1, day);
}

@Component({
  selector: 'app-asset-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './asset-form-dialog.html',
  styleUrl: './asset-form-dialog.scss',
})
export class AssetFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AssetFormDialog>);
  protected readonly data = inject<AssetFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.asset;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly assetClasses = ASSET_CLASSES;
  protected readonly formatMoney = formatMoney;

  protected readonly mode = signal<Mode>(this.data.asset?.instrumentId ? 'market' : 'manual');
  protected readonly selectedInstrument = signal<InstrumentOption | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    assetClass: [this.data.asset?.assetClass ?? 0, [Validators.required]],
    name: [this.data.asset?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    currency: [
      this.data.asset?.currency ?? DEFAULT_CURRENCY,
      [Validators.required, Validators.pattern(/^[A-Z]{3}$/)],
    ],
    quantity: [Number(this.data.asset?.quantity ?? 0), [Validators.min(0)]],
    manualValue: [Number(this.data.asset?.manualValue ?? 0), [Validators.min(0)]],
    manualValueDate: [
      this.data.asset?.manualValueDate ? fromDateOnly(this.data.asset.manualValueDate) : new Date(),
      [],
    ],
  });

  // Manual valuation fields are only required in manual mode — a market asset submits an
  // InstrumentId instead (AddAssetRequest/UpdateAssetRequest's mutually-exclusive rule).
  private readonly modeValidatorEffect = effect(() => {
    const manualRequired = this.mode() === 'manual';
    this.form.controls.manualValue.setValidators(
      manualRequired ? [Validators.required, Validators.min(0)] : [Validators.min(0)],
    );
    this.form.controls.manualValueDate.setValidators(manualRequired ? [Validators.required] : []);
    this.form.controls.manualValue.updateValueAndValidity({ emitEvent: false });
    this.form.controls.manualValueDate.updateValueAndValidity({ emitEvent: false });
  });

  protected readonly instrumentControl = new FormControl<InstrumentOption | string | null>(null);

  private readonly searchQuery = toSignal(
    this.instrumentControl.valueChanges.pipe(
      map((value) => (typeof value === 'string' ? value : '')),
      debounceTime(300),
      distinctUntilChanged(),
    ),
    { initialValue: '' },
  );

  protected readonly searchResource = resource({
    params: () => ({ query: this.searchQuery() }),
    loader: async ({ params, abortSignal }) => {
      if (params.query.trim().length === 0) {
        return [];
      }
      const result = await getApiMarketdataInstrumentsSearch({
        query: { q: params.query },
        signal: abortSignal,
      });
      return result.error ? [] : (result.data ?? []);
    },
  });

  // Pre-fills the picker when editing a market asset: AssetResponse only carries InstrumentId, not
  // the ticker/name/currency needed to display it.
  protected readonly existingInstrumentResource = resource({
    params: () =>
      this.data.asset?.instrumentId ? { instrumentId: this.data.asset.instrumentId } : undefined,
    loader: async ({ params, abortSignal }) => {
      const result = await getApiMarketdataInstrumentsById({
        path: { id: params.instrumentId },
        signal: abortSignal,
      });
      return result.error ? null : (result.data ?? null);
    },
  });

  private readonly prefillExistingInstrumentEffect = effect(() => {
    const details = this.existingInstrumentResource.value();
    if (details) {
      this.selectInstrument(details);
    }
  });

  protected readonly showCustomInstrumentForm = signal(false);
  protected readonly customInstrumentSubmitting = signal(false);
  protected readonly customInstrumentError = signal<string | null>(null);
  protected readonly customInstrumentSources = CUSTOM_INSTRUMENT_SOURCES;

  protected readonly customInstrumentForm = this.formBuilder.nonNullable.group({
    source: [1, [Validators.required]],
    ticker: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    quoteCurrency: ['', [Validators.required, Validators.pattern(/^[A-Z]{3}$/)]],
    assetClass: [this.data.asset?.assetClass ?? 0, [Validators.required]],
  });

  protected setMode(mode: Mode): void {
    this.mode.set(mode);
  }

  protected displayInstrument(value: InstrumentOption | string | null): string {
    if (!value || typeof value === 'string') {
      return value ?? '';
    }
    return `${value.ticker} — ${value.name}`;
  }

  protected onInstrumentOptionSelected(event: MatAutocompleteSelectedEvent): void {
    this.selectInstrument(event.option.value as InstrumentOption);
  }

  private selectInstrument(instrument: InstrumentOption): void {
    this.selectedInstrument.set(instrument);
    this.instrumentControl.setValue(instrument, { emitEvent: false });
    this.form.controls.currency.setValue(instrument.quoteCurrency);
    if (!this.form.controls.name.value) {
      this.form.controls.name.setValue(instrument.name);
    }
  }

  protected toggleCustomInstrumentForm(): void {
    this.showCustomInstrumentForm.update((shown) => !shown);
    this.customInstrumentError.set(null);
  }

  protected async submitCustomInstrument(): Promise<void> {
    if (this.customInstrumentSubmitting()) {
      return;
    }

    if (this.customInstrumentForm.invalid) {
      this.customInstrumentForm.markAllAsTouched();
      return;
    }

    this.customInstrumentSubmitting.set(true);
    this.customInstrumentError.set(null);

    const result = await postApiMarketdataInstruments({
      body: this.customInstrumentForm.getRawValue(),
    });

    this.customInstrumentSubmitting.set(false);

    if (result.error) {
      this.customInstrumentError.set(
        readProblemDetails(result.error).detail ?? 'Failed to add instrument.',
      );
      return;
    }

    this.selectInstrument(result.data!);
    this.showCustomInstrumentForm.set(false);
    this.customInstrumentForm.reset({
      source: 1,
      ticker: '',
      name: '',
      quoteCurrency: '',
      assetClass: this.customInstrumentForm.controls.assetClass.value,
    });
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.mode() === 'market' && !this.selectedInstrument()) {
      this.formError.set('Pick an instrument, or add a custom one, before saving.');
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const instrument = this.selectedInstrument();
    const body =
      this.mode() === 'market' && instrument
        ? {
            assetClass: values.assetClass,
            name: values.name,
            currency: values.currency,
            quantity: values.quantity,
            instrumentId: instrument.id,
          }
        : {
            assetClass: values.assetClass,
            name: values.name,
            currency: values.currency,
            quantity: values.quantity,
            manualValue: values.manualValue,
            manualValueDate: toDateOnly(values.manualValueDate),
          };

    const result = this.data.asset
      ? await putApiPortfolioPortfoliosByPortfolioIdAssetsById({
          path: { portfolioId: this.data.portfolioId, id: this.data.asset.id },
          body,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdAssets({
          path: { portfolioId: this.data.portfolioId },
          body,
        });

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
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }
}
