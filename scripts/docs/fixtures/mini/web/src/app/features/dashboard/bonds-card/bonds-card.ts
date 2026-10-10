import { Component } from '@angular/core';

import { getApiPortfolioBondsById } from '../../../api/portfolio';

@Component({
  selector: 'app-bonds-card',
  template: '',
})
export class BondsCard {
  protected load(id: string) {
    return getApiPortfolioBondsById({ path: { id } });
  }
}
