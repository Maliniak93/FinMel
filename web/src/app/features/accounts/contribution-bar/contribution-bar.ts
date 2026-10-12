import { Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe } from '@jsverse/transloco';

import type { ContributionResponse } from '../../../api/portfolio';
import { formatAmount } from '../../../shared/format';

@Component({
  selector: 'app-contribution-bar',
  imports: [MatProgressBarModule, TranslocoPipe],
  templateUrl: './contribution-bar.html',
  styleUrl: './contribution-bar.scss',
})
export class ContributionBar {
  readonly contribution = input.required<ContributionResponse>();

  protected readonly limit = computed(() => {
    const limit = this.contribution().limitPln;
    return limit === null || limit === undefined ? null : Number(limit);
  });

  protected readonly percent = computed(() => {
    const limit = this.limit();
    return limit === null
      ? 0
      : Math.min(100, (Number(this.contribution().contributedPln) / limit) * 100);
  });

  protected readonly exceeded = computed(() => {
    const limit = this.limit();
    return limit !== null && Number(this.contribution().contributedPln) > limit;
  });

  protected readonly formatAmount = formatAmount;
}
