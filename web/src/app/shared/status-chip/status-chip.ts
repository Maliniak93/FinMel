import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type StatusChipTone = 'neutral' | 'positive' | 'negative' | 'warning';

@Component({
  selector: 'app-status-chip',
  imports: [MatIconModule],
  templateUrl: './status-chip.html',
  styleUrl: './status-chip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusChip {
  readonly label = input.required<string>();
  readonly icon = input.required<string>();
  readonly tone = input<StatusChipTone>('neutral');
  readonly uncertain = input(false);
}
