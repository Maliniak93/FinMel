import { Component, input } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoPipe } from '@jsverse/transloco';

import { PageHeader } from '../../shared/page-header/page-header';
import { BondOffer } from './bond-offer/bond-offer';
import { MyBonds } from './my-bonds/my-bonds';

@Component({
  selector: 'app-bonds',
  imports: [BondOffer, MatTabsModule, MyBonds, PageHeader, TranslocoPipe],
  templateUrl: './bonds.html',
})
export class Bonds {
  readonly account = input<string>();
}
