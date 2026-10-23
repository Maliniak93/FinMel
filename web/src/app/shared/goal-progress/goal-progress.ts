import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type GoalProgressTone = 'default' | 'done' | 'warning';

const TONE_ICONS: Record<GoalProgressTone, string | null> = {
  default: null,
  done: 'check',
  warning: 'warning',
};

function percentOf(ratio: number): number {
  return Math.round(Math.min(1, Math.max(0, ratio)) * 10000) / 100;
}

@Component({
  selector: 'app-goal-progress',
  imports: [MatIconModule],
  templateUrl: './goal-progress.html',
  styleUrl: './goal-progress.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GoalProgress {
  readonly ratio = input.required<number>();
  readonly fill = input.required<string>();
  readonly tone = input<GoalProgressTone>('default');
  readonly mark = input<number>();

  protected readonly percent = computed(() => percentOf(this.ratio()));
  protected readonly valueNow = computed(() => Math.round(this.percent()));
  protected readonly markPercent = computed(() => {
    const mark = this.mark();
    return mark === undefined ? null : percentOf(mark);
  });
  protected readonly icon = computed(() => TONE_ICONS[this.tone()]);
}
