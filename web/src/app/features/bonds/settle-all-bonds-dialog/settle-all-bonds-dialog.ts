import { Component, computed, inject, resource, signal, type WritableSignal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlements,
  type BondResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { formatDate, formatPercent } from '../../../shared/format';
import {
  buildSettleSection,
  isCouponBond,
  isSectionValid,
  loadCashCandidates,
  loadSeriesRates,
  settleRequest,
  usesTermsRate,
  type BondSettleSection,
  type SeriesRates,
} from '../bond-interest';

export interface SettleAllBondsDialogData {
  bonds: BondResponse[];
}

interface SettleAllSection extends BondSettleSection {
  readonly included: FormControl<boolean>;
  readonly settled: WritableSignal<boolean>;
  readonly error: WritableSignal<string | null>;
}

@Component({
  selector: 'app-settle-all-bonds-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './settle-all-bonds-dialog.html',
  styleUrl: './settle-all-bonds-dialog.scss',
})
export class SettleAllBondsDialog {
  private readonly dialogRef = inject(MatDialogRef<SettleAllBondsDialog>);
  protected readonly bonds = inject<SettleAllBondsDialogData>(MAT_DIALOG_DATA).bonds;

  protected readonly formatDate = formatDate;
  protected readonly formatPercent = formatPercent;
  protected readonly usesTermsRate = usesTermsRate;

  protected readonly submitting = signal(false);
  private readonly settledAny = signal(false);

  protected readonly seriesResource = resource({
    loader: async ({ abortSignal }) => {
      const codes = [...new Set(this.bonds.map((bond) => bond.seriesCode))];
      const rates = await Promise.all(codes.map((code) => loadSeriesRates(code, abortSignal)));
      return new Map<string, SeriesRates | null>(codes.map((code, i) => [code, rates[i]]));
    },
  });

  protected readonly candidatesResource = resource({
    params: () => (this.bonds.some(isCouponBond) ? { coupon: true } : undefined),
    loader: ({ abortSignal }) => loadCashCandidates(abortSignal),
  });

  protected readonly sections = computed<SettleAllSection[] | null>(() => {
    if (this.seriesResource.isLoading() || this.candidatesResource.isLoading()) {
      return null;
    }
    const series = this.seriesResource.hasValue() ? this.seriesResource.value() : null;
    const candidates = this.candidatesResource.hasValue()
      ? (this.candidatesResource.value() ?? [])
      : [];
    return this.bonds.map((bond) => ({
      ...buildSettleSection(bond, series?.get(bond.seriesCode) ?? null, candidates),
      included: new FormControl(true, { nonNullable: true }),
      settled: signal(false),
      error: signal<string | null>(null),
    }));
  });

  // Each bond settles in its own request, one after another, so a failure stops only that bond.
  protected async onSubmit(): Promise<void> {
    const sections = this.sections();
    if (this.submitting() || !sections) {
      return;
    }

    const pending = sections.filter((section) => section.included.value && !section.settled());
    const invalid = pending.filter((section) => !isSectionValid(section));
    if (pending.length === 0 || invalid.length > 0) {
      for (const section of invalid) {
        section.rates.markAllAsTouched();
        section.destination.markAsTouched();
      }
      return;
    }

    this.submitting.set(true);

    for (const section of pending) {
      section.error.set(null);
      const result = await postApiPortfolioPortfoliosByPortfolioIdBondsByAssetIdInterestSettlements(
        {
          path: { portfolioId: section.bond.portfolioId, assetId: section.bond.assetId },
          body: settleRequest(section),
        },
      );
      if (result.error) {
        section.error.set(
          readProblemDetails(result.error).detail ?? translate('bonds.settle.failed'),
        );
        continue;
      }
      section.settled.set(true);
      section.included.disable();
      this.settledAny.set(true);
    }

    this.submitting.set(false);

    if (pending.every((section) => section.settled())) {
      this.dialogRef.close(true);
    }
  }

  protected cancel(): void {
    this.dialogRef.close(this.settledAny());
  }
}
