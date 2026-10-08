import { Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoPipe } from '@jsverse/transloco';

import { ASSET_CLASS } from '../assets/asset-class';
import { SecuritiesTable } from './securities-table/securities-table';

@Component({
  selector: 'app-securities',
  imports: [MatTabsModule, SecuritiesTable, TranslocoPipe],
  templateUrl: './securities.html',
  styleUrl: './securities.scss',
})
export class Securities {
  protected readonly stockClass = ASSET_CLASS.Stock;
  protected readonly etfClass = ASSET_CLASS.Etf;
}
