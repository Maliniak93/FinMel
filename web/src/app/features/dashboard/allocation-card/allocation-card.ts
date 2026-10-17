import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { getApiReportingTargetAllocation } from '../../../api/reporting';
import { readProblemDetails } from '../../../core/auth/problem-details';

@Component({
  selector: 'app-allocation-card',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    RouterLink,
    TranslocoPipe,
  ],
  templateUrl: './allocation-card.html',
  styleUrl: './allocation-card.scss',
})
export class AllocationCard {
  protected readonly allocationResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiReportingTargetAllocation({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('dashboard.allocationCard.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected readonly summary = computed(() => {
    if (!this.allocationResource.hasValue()) {
      return null;
    }
    const allocation = this.allocationResource.value();
    if (!allocation.isConfigured) {
      return { kind: 'notConfigured' as const };
    }
    if (allocation.asOf === null) {
      return { kind: 'noValuation' as const };
    }
    if (allocation.basePln === 0) {
      return { kind: 'noAssets' as const };
    }
    const outside = allocation.groups.filter((group) => group.isInBand === false).length;
    if (outside === 0) {
      return { kind: 'allInBand' as const };
    }
    return { kind: 'outside' as const, outside, total: allocation.groups.length };
  });

  protected readonly isStale = computed(
    () => this.allocationResource.hasValue() && this.allocationResource.value().isStale,
  );
}
