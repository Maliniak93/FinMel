import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { formatPercent, formatSignedPoints } from '../format';

export const ALLOCATION_DEVIATION_PP = 5;

export interface AllocationBarRow {
  label: string;
  color: string;
  /** Target share in percent. */
  target: number;
  /** Actual share in percent. */
  actual: number;
}

@Component({
  selector: 'app-allocation-bar',
  imports: [MatIconModule],
  templateUrl: './allocation-bar.html',
  styleUrl: './allocation-bar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllocationBar {
  readonly rows = input.required<readonly AllocationBarRow[]>();
  readonly targetLabel = input.required<string>();
  readonly actualLabel = input.required<string>();

  protected readonly formatPercent = formatPercent;
  protected readonly formatSignedPoints = formatSignedPoints;

  protected readonly legend = computed(() =>
    this.rows().map((row) => {
      const deviation = Math.round((row.actual - row.target) * 10) / 10;
      return { ...row, deviation, highlighted: Math.abs(deviation) >= ALLOCATION_DEVIATION_PP };
    }),
  );
}
