import { Component, inject } from '@angular/core';
import { MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ASSET_LINKS } from '../nav';

@Component({
  selector: 'app-more-sheet',
  imports: [RouterLink, MatIconModule, MatListModule, TranslocoPipe],
  templateUrl: './more-sheet.html',
  styleUrl: './more-sheet.scss',
})
export class MoreSheet {
  private readonly sheetRef = inject<MatBottomSheetRef<MoreSheet>>(MatBottomSheetRef);
  protected readonly links = ASSET_LINKS;

  protected close(): void {
    this.sheetRef.dismiss();
  }
}
