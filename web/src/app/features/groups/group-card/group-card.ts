import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import type { AssetGroupResponse } from '../../../api/portfolio';
import { activeLocale } from '../../../core/i18n/language';
import {
  AllocationBar,
  type AllocationBarRow,
} from '../../../shared/allocation-bar/allocation-bar';
import { assetClassColor, groupColor } from '../../../shared/colors';
import { formatMoney, formatPercentNumber } from '../../../shared/format';
import { GoalProgress } from '../../../shared/goal-progress/goal-progress';
import { GroupKindMarker } from '../../../shared/group-kind-marker/group-kind-marker';
import { Money } from '../../../shared/money/money';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { assetClassLabel } from '../../assets/asset-class';
import type { GroupMetrics } from '../group-metrics';

@Component({
  selector: 'app-group-card',
  imports: [
    AllocationBar,
    GoalProgress,
    GroupKindMarker,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    Money,
    StatusChip,
    TranslocoPipe,
  ],
  templateUrl: './group-card.html',
  styleUrl: './group-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupCard {
  readonly group = input.required<AssetGroupResponse>();
  readonly metrics = input.required<GroupMetrics>();
  readonly selected = input(false);

  readonly assets = output<void>();
  readonly edit = output<void>();
  readonly remove = output<void>();

  protected readonly formatMoney = formatMoney;
  protected readonly formatPercentNumber = formatPercentNumber;

  protected readonly color = computed(() => groupColor(this.group().color));

  protected readonly targetPln = computed(() => {
    const target = this.group().targetPln;
    return target === null || target === undefined ? null : Number(target);
  });

  protected readonly contributionPln = computed(() => Number(this.group().monthlyContributionPln));

  protected readonly allocationRows = computed<AllocationBarRow[]>(() => {
    activeLocale();
    return this.metrics().classRows.map((row) => ({
      label: translate(assetClassLabel(row.assetClass)),
      color: assetClassColor(row.assetClass),
      target: row.target,
      actual: row.actual,
    }));
  });

  protected readonly flagged = computed(() => this.metrics().classRows.some((row) => row.flagged));

  protected readonly targetLabel = computed(() => {
    activeLocale();
    return translate('groups.card.allocationTarget');
  });

  protected readonly actualLabel = computed(() => {
    activeLocale();
    return translate('groups.card.allocationActual');
  });
}
