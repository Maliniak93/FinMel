import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

import { GROUP_KIND, groupKindLabel } from '../../features/groups/group-kind';

@Component({
  selector: 'app-group-kind-marker',
  imports: [MatIconModule, TranslocoPipe],
  templateUrl: './group-kind-marker.html',
  styleUrl: './group-kind-marker.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupKindMarker {
  readonly kind = input.required<number | string>();

  protected readonly cushion = computed(() => Number(this.kind()) === GROUP_KIND.Cushion);
  protected readonly label = computed(() => groupKindLabel(this.kind()));
}
