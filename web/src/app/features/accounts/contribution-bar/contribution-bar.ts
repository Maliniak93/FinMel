import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

import type { ContributionResponse } from '../../../api/portfolio';
import { formatAmount } from '../../../shared/format';

type ContributionState = 'calm' | 'warning' | 'error';

const STATE_ICONS: Record<ContributionState, string> = {
  calm: 'info',
  warning: 'warning',
  error: 'error',
};

const toGrosze = (amount: number | string): number => Math.round(Number(amount) * 100);

@Component({
  selector: 'app-contribution-bar',
  imports: [MatIconModule, TranslocoPipe],
  templateUrl: './contribution-bar.html',
  styleUrl: './contribution-bar.scss',
})
export class ContributionBar {
  readonly contribution = input.required<ContributionResponse>();

  protected readonly limit = computed(() => {
    const limit = this.contribution().limitPln;
    return limit === null || limit === undefined ? null : Number(limit);
  });

  protected readonly left = computed(() => {
    const limit = this.limit();
    return limit === null
      ? 0
      : Math.max(0, toGrosze(limit) - toGrosze(this.contribution().contributedPln)) / 100;
  });

  protected readonly percent = computed(() => {
    const limit = this.limit();
    if (limit === null || toGrosze(limit) <= 0) {
      return 0;
    }
    return Math.min(100, (toGrosze(this.contribution().contributedPln) * 100) / toGrosze(limit));
  });

  protected readonly state = computed<ContributionState>(() => {
    const limit = this.limit();
    if (limit === null) {
      return 'calm';
    }
    const paid = toGrosze(this.contribution().contributedPln);
    const cap = toGrosze(limit);
    if (paid > cap) {
      return 'error';
    }
    return paid * 10 >= cap * 9 ? 'warning' : 'calm';
  });

  protected readonly icon = computed(() => STATE_ICONS[this.state()]);

  protected readonly formatAmount = formatAmount;
}
