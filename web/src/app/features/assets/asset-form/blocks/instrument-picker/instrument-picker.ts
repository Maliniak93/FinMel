import { Component, effect, inject, input, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  MatAutocompleteModule,
  type MatAutocompleteSelectedEvent,
} from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';

import {
  getApiMarketdataInstrumentsById,
  getApiMarketdataInstrumentsSearch,
  postApiMarketdataInstruments,
  type CustomInstrumentResponse,
  type InstrumentDetailsResponse,
  type InstrumentSearchResult,
} from '../../../../../api/marketdata';
import type { AssetClass } from '../../../../../api/portfolio';
import {
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../../../core/auth/problem-details';
import { formatMoney } from '../../../../../shared/format-money';

export type InstrumentOption =
  InstrumentSearchResult | InstrumentDetailsResponse | CustomInstrumentResponse;

// The selected instrument. Required: a market asset submits its InstrumentId, and without one
// AddAssetRequest/UpdateAssetRequest would reject the body anyway.
export function createInstrumentControl(): FormControl<InstrumentOption | null> {
  return new FormControl<InstrumentOption | null>(null, [Validators.required]);
}

// The banner the shell shows when a market asset is submitted with no instrument selected.
export const INSTRUMENT_REQUIRED_MESSAGE =
  'Verify a ticker, or pick one from search, before creating this asset.';

// ITickerVerifier's three outcomes (ADR-018) plus 'conflict' (409 already-in-dictionary) and a
// generic 'error' fallback — mirrored in instrument-picker.html's @if/@else-if chain.
type CustomInstrumentOutcome = 'idle' | 'notFound' | 'unreachable' | 'conflict' | 'error';

// Instrument autocomplete, the edit pre-fill through GET instruments/{id}, and — behind
// `allowCustomTicker` — the ADR-018 "verify a new ticker" panel. Writes the chosen instrument into
// `control`, and into `nameControl` too when that is still empty.
@Component({
  selector: 'app-instrument-picker',
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './instrument-picker.html',
  styleUrl: './instrument-picker.scss',
})
export class InstrumentPicker {
  private readonly formBuilder = inject(FormBuilder);

  readonly control = input.required<FormControl<InstrumentOption | null>>();
  readonly assetClass = input.required<AssetClass>();
  readonly allowCustomTicker = input(false);
  // The stored asset's instrument, pre-selected on edit: AssetResponse only carries InstrumentId,
  // not the ticker/name/currency needed to display it.
  readonly existingInstrumentId = input<string | null | undefined>();
  readonly nameControl = input<FormControl<string>>();

  protected readonly formatMoney = formatMoney;

  protected readonly selectedInstrument = signal<InstrumentOption | null>(null);

  // The text box: holds the typed query, or the picked option (rendered through displayInstrument).
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

  protected readonly existingInstrumentResource = resource({
    params: () => {
      const instrumentId = this.existingInstrumentId();
      return instrumentId ? { instrumentId } : undefined;
    },
    loader: async ({ params, abortSignal }) => {
      const result = await getApiMarketdataInstrumentsById({
        path: { id: params.instrumentId },
        signal: abortSignal,
      });
      return result.error ? null : (result.data ?? null);
    },
  });

  // resource().value() throws while the resource is in its error state — gated on hasValue() rather
  // than trusting the loader's own try/catch (a network exception, not just an API error, can still
  // land the resource there).
  private readonly prefillExistingInstrumentEffect = effect(() => {
    if (!this.existingInstrumentResource.hasValue()) {
      return;
    }
    const details = this.existingInstrumentResource.value();
    if (details) {
      this.selectInstrument(details);
    }
  });

  protected readonly showCustomInstrumentForm = signal(false);
  protected readonly customInstrumentSubmitting = signal(false);
  protected readonly customInstrumentOutcome = signal<CustomInstrumentOutcome>('idle');
  protected readonly customInstrumentMessage = signal<string | null>(null);

  // No Source field: AddCustomInstrumentRequest derives the provider from AssetClass (M1.6) — the
  // user never picks Stooq vs CoinGecko by hand anymore.
  protected readonly customInstrumentForm = this.formBuilder.nonNullable.group({
    ticker: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    quoteCurrency: ['', [Validators.required, Validators.pattern(/^[A-Z]{3}$/)]],
  });

  protected displayInstrument(value: InstrumentOption | string | null): string {
    if (!value || typeof value === 'string') {
      return value ?? '';
    }
    return `${value.ticker} — ${value.name}`;
  }

  protected onInstrumentOptionSelected(event: MatAutocompleteSelectedEvent): void {
    this.selectInstrument(event.option.value as InstrumentOption);
  }

  // Deliberately does NOT copy instrument.quoteCurrency into the asset's Currency (T1.11 did).
  // ValuationAlgorithm.ValueMarketAsset resolves FX off the *instrument's* quote currency
  // (price.QuoteCurrency), never off Asset.Currency — so for a market asset, Currency is just the
  // user's own PLN/EUR/USD denomination choice (SupportedCurrency, M1.3), unrelated to what the
  // instrument itself quotes in and never read by market valuation.
  private selectInstrument(instrument: InstrumentOption): void {
    this.selectedInstrument.set(instrument);
    this.control().setValue(instrument);
    this.instrumentControl.setValue(instrument, { emitEvent: false });
    const nameControl = this.nameControl();
    if (nameControl && !nameControl.value) {
      nameControl.setValue(instrument.name);
    }
  }

  protected toggleCustomInstrumentForm(): void {
    this.showCustomInstrumentForm.update((shown) => !shown);
    this.customInstrumentOutcome.set('idle');
    this.customInstrumentMessage.set(null);
  }

  protected async submitCustomInstrument(allowUnverified = false): Promise<void> {
    if (this.customInstrumentSubmitting()) {
      return;
    }

    if (!allowUnverified && this.customInstrumentForm.invalid) {
      this.customInstrumentForm.markAllAsTouched();
      return;
    }

    this.customInstrumentSubmitting.set(true);
    this.customInstrumentOutcome.set('idle');
    this.customInstrumentMessage.set(null);

    const values = this.customInstrumentForm.getRawValue();
    const result = await postApiMarketdataInstruments({
      body: {
        ticker: values.ticker,
        name: values.name,
        quoteCurrency: values.quoteCurrency,
        assetClass: this.assetClass(),
        allowUnverified,
      },
    });

    this.customInstrumentSubmitting.set(false);

    if (result.error) {
      const problem = readProblemDetails(result.error);
      this.customInstrumentMessage.set(problem.detail ?? 'Failed to add instrument.');
      this.customInstrumentOutcome.set(this.classifyCustomInstrumentError(problem));
      return;
    }

    this.customInstrumentOutcome.set('idle');
    this.customInstrumentMessage.set(null);
    this.selectInstrument(result.data!);
    this.showCustomInstrumentForm.set(false);
    this.customInstrumentForm.reset({ ticker: '', name: '', quoteCurrency: '' });
  }

  // Renders ITickerVerifier's three outcomes (ADR-018) as three distinct states, plus a fallback for
  // anything else (409 already-in-dictionary, or a genuinely unexpected error).
  private classifyCustomInstrumentError(problem: ApiProblemDetails): CustomInstrumentOutcome {
    switch (problem.errorCode) {
      case 'Validation.TickerNotFound':
        return 'notFound';
      case 'ServiceUnavailable.TickerVerificationUnreachable':
        return 'unreachable';
      case 'Conflict.InstrumentAlreadyExists':
        return 'conflict';
      default:
        return 'error';
    }
  }

  // The documented ADR-018 opt-in: when the provider couldn't be reached, create the instrument
  // Unverified anyway (HistoryBackfillJob resolves it later, off the request path) instead of
  // leaving the user stuck.
  protected addInstrumentAnyway(): void {
    void this.submitCustomInstrument(true);
  }
}
