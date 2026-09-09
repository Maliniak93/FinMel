import { Component, computed, effect, inject, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  MatAutocompleteModule,
  type MatAutocompleteSelectedEvent,
} from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
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
import { SUPPORTED_CURRENCIES, DEFAULT_CURRENCY } from '../../../shared/currencies';
import { formatMoney } from '../../../shared/format-money';
import {
  isPricedTransactionType,
  quantityFieldLabel,
  showsFeeField,
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_DEPOSIT,
  TRANSACTION_TYPES,
} from '../../transactions/transaction-type';
import { ASSET_CLASSES } from '../asset-class';
import {
  canAddCustomInstrument,
  defaultValuationMode,
  VALUATION_MODE,
} from '../asset-valuation-mode';

export interface AssetFormDialogData {
  portfolioId: string;
  asset?: AssetResponse;
}

type InstrumentOption =
  InstrumentSearchResult | InstrumentDetailsResponse | CustomInstrumentResponse;

// ITickerVerifier's three outcomes (ADR-018) plus 'conflict' (409 already-in-dictionary) and a
// generic 'error' fallback — mirrored in asset-form-dialog.html's @if/@else-if chain (lines 147-165).
type CustomInstrumentOutcome = 'idle' | 'notFound' | 'unreachable' | 'conflict' | 'error';

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
    MatCheckboxModule,
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
  protected readonly currencies = SUPPORTED_CURRENCIES;
  protected readonly formatMoney = formatMoney;

  protected readonly form = this.formBuilder.nonNullable.group({
    assetClass: [this.data.asset?.assetClass ?? 0, [Validators.required]],
    name: [this.data.asset?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    currency: [this.data.asset?.currency ?? DEFAULT_CURRENCY, [Validators.required]],
    manualValue: [Number(this.data.asset?.manualValue ?? 0), [Validators.min(0)]],
    manualValueDate: [
      this.data.asset?.manualValueDate ? fromDateOnly(this.data.asset.manualValueDate) : new Date(),
      [],
    ],
  });

  // The asset class is what drives the whole form (Scope: "each choice adapts the form to add the
  // given asset") — the old manual/market button-toggle the user picked by hand is gone; the class's
  // AssetValuationModes.Default mirror (asset-valuation-mode.ts) decides instead.
  private readonly assetClassValue = toSignal(this.form.controls.assetClass.valueChanges, {
    initialValue: this.form.controls.assetClass.value,
  });
  protected readonly valuationMode = computed(() => defaultValuationMode(this.assetClassValue()));
  protected readonly isMarket = computed(() => this.valuationMode() === VALUATION_MODE.Market);
  protected readonly isManual = computed(() => this.valuationMode() === VALUATION_MODE.Manual);
  protected readonly isCurrencyValued = computed(
    () => this.valuationMode() === VALUATION_MODE.CurrencyValued,
  );
  protected readonly customInstrumentAvailable = computed(() =>
    canAddCustomInstrument(this.assetClassValue()),
  );

  protected readonly selectedInstrument = signal<InstrumentOption | null>(null);

  // Manual valuation fields are only required in manual mode; a market asset submits an
  // InstrumentId instead, and a currency-valued asset submits neither (AddAssetRequest/
  // UpdateAssetRequest's three-way mutually-exclusive rule, M1.4).
  private readonly modeValidatorEffect = effect(() => {
    const manualRequired = this.isManual();
    this.form.controls.manualValue.setValidators(
      manualRequired ? [Validators.required, Validators.min(0)] : [Validators.min(0)],
    );
    this.form.controls.manualValueDate.setValidators(manualRequired ? [Validators.required] : []);
    this.form.controls.manualValue.updateValueAndValidity({ emitEvent: false });
    this.form.controls.manualValueDate.updateValueAndValidity({ emitEvent: false });
  });

  // Leaving market mode drops whatever instrument was selected/verified — submitting it against a
  // now-manual or now-currency-valued class would be meaningless, and AddAssetRequest.Validate would
  // reject the combination anyway. Depends only on isMarket() (not on selectedInstrument() itself),
  // so this never reads the signal it writes — signal.set() is a no-op once it's already null.
  private readonly clearInstrumentOnModeChangeEffect = effect(() => {
    if (this.isMarket()) {
      return;
    }
    this.selectedInstrument.set(null);
    this.instrumentControl.setValue(null, { emitEvent: false });
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

  // resource().value() throws while the resource is in its error state — gated on hasValue() rather
  // than trusting the loader's own try/catch (a network exception, not just an API error, can still
  // land the resource there). Also re-checks isMarket(): guards the race where the user flips the
  // asset class away from Market before this async fetch resolves (clearInstrumentOnModeChangeEffect
  // would otherwise have already run and this could reintroduce a stale instrument behind its back).
  private readonly prefillExistingInstrumentEffect = effect(() => {
    if (!this.existingInstrumentResource.hasValue()) {
      return;
    }
    const details = this.existingInstrumentResource.value();
    if (details && this.isMarket()) {
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

  // "Add first transaction" (M1.5) only exists on create — AddAssetRequest.InitialTransaction has no
  // Update-side counterpart, so quantity on an existing asset only ever moves through the
  // transactions view.
  protected readonly addFirstTransaction = signal(false);
  protected readonly transactionTypes = TRANSACTION_TYPES;

  protected readonly transactionForm = this.formBuilder.nonNullable.group({
    type: [
      defaultValuationMode(this.data.asset?.assetClass ?? 0) === VALUATION_MODE.CurrencyValued
        ? TRANSACTION_TYPE_DEPOSIT
        : TRANSACTION_TYPE_BUY,
      [Validators.required],
    ],
    quantity: [0, [Validators.min(0)]],
    unitPrice: [1, [Validators.min(0)]],
    fee: [0, [Validators.min(0)]],
    date: [new Date(), [Validators.required]],
  });

  private readonly transactionType = toSignal(this.transactionForm.controls.type.valueChanges, {
    initialValue: this.transactionForm.controls.type.value,
  });
  protected readonly transactionIsPriced = computed(() =>
    isPricedTransactionType(this.transactionType()),
  );
  protected readonly transactionShowsFee = computed(() => showsFeeField(this.transactionType()));
  protected readonly transactionQuantityLabel = computed(() =>
    quantityFieldLabel(this.transactionType()),
  );

  protected toggleAddFirstTransaction(): void {
    const enabling = !this.addFirstTransaction();
    this.addFirstTransaction.set(enabling);

    // Prefill a sensible default only the first time the box is checked (pristine) — an already
    //-edited sub-form is left alone so unchecking/rechecking never clobbers what the user typed.
    if (enabling && this.transactionForm.pristine) {
      const isCash = this.isCurrencyValued();
      this.transactionForm.patchValue({
        type: isCash ? TRANSACTION_TYPE_DEPOSIT : TRANSACTION_TYPE_BUY,
        unitPrice: isCash ? 1 : 0,
      });
    }
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

  // Deliberately does NOT copy instrument.quoteCurrency into the Currency control (T1.11 did).
  // ValuationAlgorithm.ValueMarketAsset resolves FX off the *instrument's* quote currency
  // (price.QuoteCurrency), never off Asset.Currency — so for a market asset, Currency is just the
  // user's own PLN/EUR/USD denomination choice (SupportedCurrency, M1.3), unrelated to what the
  // instrument itself quotes in and never read by market valuation.
  private selectInstrument(instrument: InstrumentOption): void {
    this.selectedInstrument.set(instrument);
    this.instrumentControl.setValue(instrument, { emitEvent: false });
    if (!this.form.controls.name.value) {
      this.form.controls.name.setValue(instrument.name);
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
        assetClass: this.form.controls.assetClass.value,
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

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.isMarket() && !this.selectedInstrument()) {
      this.formError.set('Verify a ticker, or pick one from search, before creating this asset.');
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const recordingInitialTransaction = !this.isEdit && this.addFirstTransaction();
    if (recordingInitialTransaction && this.transactionForm.invalid) {
      this.transactionForm.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const modeFields = this.buildModeFields();

    const result = this.data.asset
      ? await putApiPortfolioPortfoliosByPortfolioIdAssetsById({
          path: { portfolioId: this.data.portfolioId, id: this.data.asset.id },
          body: {
            assetClass: values.assetClass,
            name: values.name,
            currency: values.currency,
            ...modeFields,
          },
        })
      : await postApiPortfolioPortfoliosByPortfolioIdAssets({
          path: { portfolioId: this.data.portfolioId },
          body: {
            assetClass: values.assetClass,
            name: values.name,
            currency: values.currency,
            ...modeFields,
            initialTransaction: recordingInitialTransaction ? this.buildInitialTransaction() : null,
          },
        });

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(readProblemDetails(result.error));
      return;
    }

    this.dialogRef.close(true);
  }

  private buildModeFields():
    | { instrumentId: string }
    | { manualValue: number; manualValueDate: string }
    | Record<string, never> {
    const mode = this.valuationMode();

    if (mode === VALUATION_MODE.Market) {
      // Guarded in onSubmit — selectedInstrument() is always set by the time this runs.
      return { instrumentId: this.selectedInstrument()!.id };
    }

    if (mode === VALUATION_MODE.Manual) {
      const values = this.form.getRawValue();
      return {
        manualValue: values.manualValue,
        manualValueDate: toDateOnly(values.manualValueDate),
      };
    }

    return {};
  }

  private buildInitialTransaction() {
    const values = this.transactionForm.getRawValue();
    return {
      type: values.type,
      quantity: values.quantity,
      // Unit price/fee are hidden (and meaningless) for non-trade types — same rule as
      // TransactionFormDialog: 1/0 keeps `quantity * unitPrice` a single Value formula end to end.
      unitPrice: isPricedTransactionType(values.type) ? values.unitPrice : 1,
      fee: showsFeeField(values.type) ? values.fee : 0,
      date: toDateOnly(values.date),
    };
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    const mainFieldMatched = applyFieldErrors(this.form, problem);
    const transactionFieldMatched = this.applyInitialTransactionFieldErrors(problem);

    if (mainFieldMatched || transactionFieldMatched) {
      return;
    }

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }

  // AddAssetRequest.InitialTransaction is a nested complex property — .NET 10's validation recurses
  // into it and reports keys like "InitialTransaction.Quantity", which applyFieldErrors (matching
  // top-level `form` control names only) can't route on its own; this matches the suffix against
  // transactionForm's own controls instead.
  private applyInitialTransactionFieldErrors(problem: ApiProblemDetails): boolean {
    if (!problem.errors) {
      return false;
    }

    let matched = false;
    for (const [field, messages] of Object.entries(problem.errors)) {
      if (!field.toLowerCase().includes('initialtransaction')) {
        continue;
      }
      const suffix = field.split('.').pop() ?? field;
      const controlName = Object.keys(this.transactionForm.controls).find(
        (name) => name.toLowerCase() === suffix.toLowerCase(),
      );
      if (controlName) {
        this.transactionForm.get(controlName)?.setErrors({ server: messages.join(' ') });
        matched = true;
      }
    }
    return matched;
  }
}
