import { Component, input, signal } from '@angular/core';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoPipe } from '@jsverse/transloco';

import { ASSET_CLASS } from '../assets/asset-class';
import { SecuritiesTable } from './securities-table/securities-table';
import { WatchlistTable } from './watchlist-table/watchlist-table';

@Component({
  selector: 'app-securities',
  imports: [MatSlideToggleModule, MatTabsModule, SecuritiesTable, WatchlistTable, TranslocoPipe],
  templateUrl: './securities.html',
  styleUrl: './securities.scss',
})
export class Securities {
  readonly account = input<string>();

  protected readonly showArchived = signal(false);

  protected readonly stockClass = ASSET_CLASS.Stock;
  protected readonly etfClass = ASSET_CLASS.Etf;
}
