import { Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import type { ContributionLimitWarningResponse } from '../../../api/portfolio';
import { formatMoney } from '../../../shared/format';

@Component({
  selector: 'app-contribution-limit-warning',
  imports: [TranslocoPipe],
  templateUrl: './contribution-limit-warning.html',
  styleUrl: './contribution-limit-warning.scss',
})
export class ContributionLimitWarning {
  readonly warnings = input.required<readonly ContributionLimitWarningResponse[]>();

  protected readonly formatMoney = formatMoney;
}
