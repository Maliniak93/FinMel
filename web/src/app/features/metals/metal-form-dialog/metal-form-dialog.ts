import { Component, inject, resource, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPortfolios,
  postApiPortfolioPortfoliosByPortfolioIdMetals,
  putApiPortfolioPortfoliosByPortfolioIdMetalsByAssetId,
  type Metal,
  type MetalResponse,
  type UpdateMetalRequest,
  type WeightUnit,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { toDateOnly } from '../../../shared/date-only';
import { METAL, METALS } from '../metal';

export interface MetalFormDialogData {
  portfolioId?: string;
  metal?: MetalResponse;
}

interface MetalForm {
  metal: FormControl<Metal>;
  name: FormControl<string>;
  fineWeight: FormControl<number | null>;
  weightUnit: FormControl<WeightUnit>;
  portfolioId?: FormControl<string>;
  pieces?: FormControl<number | null>;
  pricePerPiece?: FormControl<number | null>;
  purchaseDate?: FormControl<Date | null>;
}

const WEIGHT_UNITS: readonly { value: WeightUnit; label: string }[] = [
  { value: 'Gram', label: 'metals.form.unitGram' },
  { value: 'TroyOunce', label: 'metals.form.unitTroyOunce' },
];

function isBlank(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

function positive(control: AbstractControl): ValidationErrors | null {
  return !isBlank(control.value) && Number(control.value) <= 0 ? { positive: true } : null;
}

function piecesEntered(control: AbstractControl): boolean {
  return !isBlank(control.parent?.get('pieces')?.value);
}

function pricePerPieceValid(control: AbstractControl): ValidationErrors | null {
  if (isBlank(control.value)) {
    return piecesEntered(control) ? { required: true } : null;
  }
  return Number(control.value) < 0 ? { min: true } : null;
}

function purchaseDateValid(control: AbstractControl): ValidationErrors | null {
  const date = control.value as Date | null;
  if (!date) {
    return piecesEntered(control) ? { required: true } : null;
  }
  return toDateOnly(date) > toDateOnly(new Date()) ? { future: true } : null;
}

@Component({
  selector: 'app-metal-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './metal-form-dialog.html',
  styleUrl: './metal-form-dialog.scss',
})
export class MetalFormDialog {
  private readonly dialogRef = inject(MatDialogRef<MetalFormDialog>);
  protected readonly data = inject<MetalFormDialogData>(MAT_DIALOG_DATA);

  private readonly holding = this.data.metal;
  protected readonly isEdit = !!this.holding;
  protected readonly choosesPortfolio = !this.holding && !this.data.portfolioId;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly metals = METALS;
  protected readonly weightUnits = WEIGHT_UNITS;

  protected readonly form = new FormGroup<MetalForm>({
    metal: new FormControl<Metal>(this.holding?.metal ?? METAL.Gold, {
      nonNullable: true,
      validators: [Validators.required],
    }),
    name: new FormControl(this.holding?.name ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(200)],
    }),
    // An edit shows the stored grams: the server keeps no unit.
    fineWeight: new FormControl<number | null>(
      this.holding ? Number(this.holding.fineWeightGramsPerPiece) : null,
      [Validators.required, positive],
    ),
    weightUnit: new FormControl<WeightUnit>('Gram', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    ...(this.choosesPortfolio
      ? {
          portfolioId: new FormControl('', {
            nonNullable: true,
            validators: [Validators.required],
          }),
        }
      : {}),
    ...(this.holding
      ? {}
      : {
          pieces: new FormControl<number | null>(null, [positive]),
          pricePerPiece: new FormControl<number | null>(null, [pricePerPieceValid]),
          purchaseDate: new FormControl<Date | null>(new Date(), [purchaseDateValid]),
        }),
  });

  protected readonly portfoliosResource = resource({
    params: () => (this.choosesPortfolio ? {} : undefined),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfolios({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('deposits.form.portfoliosLoadFailed'),
        );
      }
      return (result.data ?? []).filter((portfolio) => !portfolio.isArchived);
    },
  });

  constructor() {
    this.form.controls.pieces?.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.form.controls.pricePerPiece?.updateValueAndValidity();
      this.form.controls.purchaseDate?.updateValueAndValidity();
    });
  }

  protected hasError(controlName: keyof MetalForm, errorCode: string): boolean {
    return this.form.get(controlName)?.hasError(errorCode) ?? false;
  }

  protected serverError(controlName: keyof MetalForm): string | null {
    return (this.form.get(controlName)?.getError('server') as string | undefined) ?? null;
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const product: UpdateMetalRequest = {
      name: values.name,
      metal: Number(values.metal),
      fineWeight: Number(values.fineWeight),
      weightUnit: values.weightUnit,
    };

    const result = this.holding
      ? await putApiPortfolioPortfoliosByPortfolioIdMetalsByAssetId({
          path: { portfolioId: this.holding.portfolioId, assetId: this.holding.assetId },
          body: product,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdMetals({
          path: { portfolioId: this.data.portfolioId ?? values.portfolioId! },
          body: {
            ...product,
            ...(isBlank(values.pieces)
              ? {}
              : {
                  firstPurchase: {
                    pieces: Number(values.pieces),
                    pricePerPiece: Number(values.pricePerPiece),
                    date: toDateOnly(values.purchaseDate!),
                  },
                }),
          },
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

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
