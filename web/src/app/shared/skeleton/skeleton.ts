import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';

const SHOW_AFTER_MS = 300;

@Component({
  selector: 'app-skeleton',
  templateUrl: './skeleton.html',
  styleUrl: './skeleton.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
})
export class Skeleton {
  readonly rows = input(3);

  protected readonly visible = signal(false);
  protected readonly rowIndexes = computed(() => Array.from({ length: this.rows() }, (_, i) => i));

  constructor() {
    const timer = setTimeout(() => this.visible.set(true), SHOW_AFTER_MS);
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));
  }
}
