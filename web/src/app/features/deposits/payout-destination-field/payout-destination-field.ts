import {
  ChangeDetectorRef,
  Component,
  inject,
  input,
  resource,
  signal,
  viewChild,
  type DoCheck,
} from '@angular/core';
import { NgControl, type ControlValueAccessor } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelect, MatSelectModule, type MatSelectChange } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioTransferCandidates,
  type TransferCandidateResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';

@Component({
  selector: 'app-payout-destination-field',
  imports: [MatFormFieldModule, MatSelectModule, TranslocoPipe],
  templateUrl: './payout-destination-field.html',
  styleUrl: './payout-destination-field.scss',
})
export class PayoutDestinationField implements ControlValueAccessor, DoCheck {
  readonly currency = input.required<string>();
  readonly offerKeep = input(false);
  readonly hint = input<string | null>(null);

  private readonly changeDetector = inject(ChangeDetectorRef);
  // Set below rather than provided, so it can read the control's errors without a circular dependency.
  protected readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly select = viewChild(MatSelect);

  protected readonly value = signal<string | null>(null);
  protected readonly disabled = signal(false);

  protected readonly errorStateMatcher: ErrorStateMatcher = {
    isErrorState: (_control, form) => {
      const control = this.ngControl?.control;
      return !!control && control.invalid && (control.touched || !!form?.submitted);
    },
  };

  protected readonly candidatesResource = resource({
    params: () => ({ currency: this.currency() }),
    loader: async ({ params, abortSignal }) => {
      const [cash, bank] = await Promise.all(
        [ASSET_CLASS.Cash, ASSET_CLASS.BankAccount].map(async (assetClass) => {
          const result = await getApiPortfolioTransferCandidates({
            query: { currency: params.currency, assetClass },
            signal: abortSignal,
          });
          if (result.error) {
            throw new Error(
              readProblemDetails(result.error).detail ??
                translate('deposits.errors.payoutDestinationsLoadFailed'),
            );
          }
          return result.data ?? [];
        }),
      );
      return { cash, bank };
    },
  });

  private onChange: (value: string | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  constructor() {
    if (this.ngControl) {
      this.ngControl.valueAccessor = this;
    }
  }

  // The inner select has no control of its own and never re-reads the bound control's error state.
  ngDoCheck(): void {
    this.select()?.updateErrorState();
    this.changeDetector.markForCheck();
  }

  writeValue(value: string | null): void {
    this.value.set(value ?? null);
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected optionLabel(candidate: TransferCandidateResponse): string {
    return `${candidate.name} (${formatMoney(candidate.balance, this.currency())})`;
  }

  protected hasError(errorCode: string): boolean {
    return this.ngControl?.control?.hasError(errorCode) ?? false;
  }

  protected serverError(): string | null {
    return (this.ngControl?.control?.getError('server') as string | undefined) ?? null;
  }

  protected onSelectionChange(change: MatSelectChange): void {
    this.choose(change.value as string | null);
  }

  protected onOpenedChange(opened: boolean): void {
    if (!opened) {
      this.onTouched();
    }
  }

  private choose(value: string | null): void {
    this.value.set(value);
    this.onChange(value);
  }
}
