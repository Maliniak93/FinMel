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
import { TranslocoPipe, translate } from '@jsverse/transloco';

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
import { formatMoney } from '../../../../../shared/format';

export type InstrumentOption =
  InstrumentSearchResult | InstrumentDetailsResponse | CustomInstrumentResponse;

export function createInstrumentControl(): FormControl<InstrumentOption | null> {
  return new FormControl<InstrumentOption | null>(null, [Validators.required]);
}

export const INSTRUMENT_REQUIRED_MESSAGE = 'assets.form.instrumentRequired';

type CustomInstrumentOutcome = 'idle' | 'notFound' | 'unreachable' | 'conflict' | 'error';

@Component({
  selector: 'app-instrument-picker',
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './instrument-picker.html',
  styleUrl: './instrument-picker.scss',
})
export class InstrumentPicker {
  private readonly formBuilder = inject(FormBuilder);

  readonly control = input.required<FormControl<InstrumentOption | null>>();
  readonly assetClass = input.required<AssetClass>();
  readonly allowCustomTicker = input(false);
  readonly existingInstrumentId = input<string | null | undefined>();
  readonly nameControl = input<FormControl<string>>();

  protected readonly formatMoney = formatMoney;

  protected readonly selectedInstrument = signal<InstrumentOption | null>(null);

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

  // The asset keeps its own currency: market valuation converts from the instrument's quote currency.
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
      this.customInstrumentMessage.set(
        problem.detail ?? translate('assets.form.addInstrumentFailed'),
      );
      this.customInstrumentOutcome.set(this.classifyCustomInstrumentError(problem));
      return;
    }

    this.customInstrumentOutcome.set('idle');
    this.customInstrumentMessage.set(null);
    this.selectInstrument(result.data!);
    this.showCustomInstrumentForm.set(false);
    this.customInstrumentForm.reset({ ticker: '', name: '', quoteCurrency: '' });
  }

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

  protected addInstrumentAnyway(): void {
    void this.submitCustomInstrument(true);
  }
}
