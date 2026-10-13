import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  postApiPortfolioWatchlist,
  putApiPortfolioWatchlistById,
  type AssetClass,
  type WatchlistItemResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import {
  createInstrumentControl,
  INSTRUMENT_REQUIRED_MESSAGE,
  InstrumentPicker,
} from '../../assets/asset-form/blocks/instrument-picker/instrument-picker';

export interface WatchlistItemDialogData {
  assetClass: AssetClass;
  item?: WatchlistItemResponse;
}

@Component({
  selector: 'app-watchlist-item-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    InstrumentPicker,
    TranslocoPipe,
  ],
  templateUrl: './watchlist-item-dialog.html',
  styleUrl: './watchlist-item-dialog.scss',
})
export class WatchlistItemDialog {
  private readonly dialogRef = inject(MatDialogRef<WatchlistItemDialog>);
  private readonly formBuilder = inject(FormBuilder);
  protected readonly data = inject<WatchlistItemDialogData>(MAT_DIALOG_DATA);

  protected readonly item = this.data.item;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    instrument: createInstrumentControl(),
    note: this.formBuilder.nonNullable.control(this.item?.note ?? '', [Validators.maxLength(500)]),
    targetPrice: this.formBuilder.control<number | null>(
      this.item?.targetPrice === null || this.item?.targetPrice === undefined
        ? null
        : Number(this.item.targetPrice),
      [Validators.min(0)],
    ),
  });

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }
    const instrument = this.form.controls.instrument.value;
    if (!this.item && !instrument) {
      this.formError.set(translate(INSTRUMENT_REQUIRED_MESSAGE));
      return;
    }
    if (this.form.controls.note.invalid || this.form.controls.targetPrice.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const note = this.form.controls.note.value.trim() || null;
    const targetPrice = this.form.controls.targetPrice.value;
    const result = this.item
      ? await putApiPortfolioWatchlistById({
          path: { id: this.item.id },
          body: { note, targetPrice },
        })
      : await postApiPortfolioWatchlist({
          body: { instrumentId: instrument!.id!, note, targetPrice },
        });

    this.submitting.set(false);

    if (result.error) {
      this.formError.set(
        result.response?.status === 409
          ? translate('securities.watchlist.alreadyWatched')
          : (readProblemDetails(result.error).detail ?? translate('errors.generic')),
      );
      return;
    }

    this.dialogRef.close(true);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    void this.onSubmit();
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}
