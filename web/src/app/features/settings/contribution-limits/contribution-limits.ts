import { Component, resource, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  deleteApiPortfolioContributionLimitsByYear,
  getApiPortfolioContributionLimits,
  putApiPortfolioContributionLimitsByYear,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { SectionError } from '../../../shared/section-error/section-error';
import { Skeleton } from '../../../shared/skeleton/skeleton';

type LimitForm = FormGroup<{
  ike: FormControl<number | null>;
  ikze: FormControl<number | null>;
}>;

interface LimitRow {
  readonly year: number;
  readonly form: LimitForm;
}

const positiveAmount = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value as number | null;
  return value !== null && !(value > 0) ? { positive: true } : null;
};

const toAmount = (value: number | string | null | undefined): number | null =>
  value === null || value === undefined ? null : Number(value);

const createRow = (year: number, ike: number | null, ikze: number | null): LimitRow => ({
  year,
  form: new FormGroup({
    ike: new FormControl<number | null>(ike, { validators: [positiveAmount] }),
    ikze: new FormControl<number | null>(ikze, { validators: [positiveAmount] }),
  }),
});

@Component({
  selector: 'app-contribution-limits',
  imports: [
    EmptyState,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    ReactiveFormsModule,
    SectionError,
    Skeleton,
    TranslocoPipe,
  ],
  templateUrl: './contribution-limits.html',
  styleUrl: './contribution-limits.scss',
})
export class ContributionLimits {
  protected readonly rows = signal<LimitRow[]>([]);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly limitsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioContributionLimits({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('settings.limits.loadFailed'),
        );
      }
      const saved = result.data ?? [];
      this.rows.set(
        saved
          .map((limit) =>
            createRow(Number(limit.year), toAmount(limit.ikeAmount), toAmount(limit.ikzeAmount)),
          )
          .sort((a, b) => b.year - a.year),
      );
      return saved;
    },
  });

  protected addYear(): void {
    const rows = this.rows();
    const newest = rows[0];
    const year = newest ? newest.year + 1 : new Date().getFullYear();
    const values = newest?.form.getRawValue();
    this.rows.set([createRow(year, values?.ike ?? null, values?.ikze ?? null), ...rows]);
  }

  protected async save(row: LimitRow): Promise<void> {
    row.form.markAllAsTouched();
    if (row.form.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);
    const { ike, ikze } = row.form.getRawValue();
    const result = await putApiPortfolioContributionLimitsByYear({
      path: { year: row.year },
      body: { ikeAmount: ike, ikzeAmount: ikze },
    });
    this.busy.set(false);

    if (result.error) {
      this.actionError.set(
        readProblemDetails(result.error).detail ?? translate('settings.limits.saveFailed'),
      );
    }
  }

  protected async remove(row: LimitRow): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);
    const result = await deleteApiPortfolioContributionLimitsByYear({ path: { year: row.year } });
    this.busy.set(false);

    if (result.error && result.response?.status !== 404) {
      this.actionError.set(
        readProblemDetails(result.error).detail ?? translate('settings.limits.removeFailed'),
      );
      return;
    }

    this.rows.update((rows) => rows.filter((candidate) => candidate !== row));
  }
}
