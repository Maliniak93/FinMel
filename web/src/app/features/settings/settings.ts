import { Component, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiMarketdataSyncStatus,
  postApiMarketdataSyncTrigger,
  type SyncRunStatus,
} from '../../api/marketdata';
import { readProblemDetails } from '../../core/auth/problem-details';
import { formatDateTime } from '../../shared/format';

// Backend enum (Skarbiec.MarketData.Data.SyncRunStatus) serializes as its underlying int, so the
// generated client types it as a bare `number` — labels maintained here in the C# enum's declaration
// order (Data/SyncRun.cs), same pattern as features/assets/asset-class.ts. Labels are translation
// keys, rendered through the `transloco` pipe.
const SYNC_RUN_STATUS_LABELS: readonly string[] = [
  'enums.syncRunStatus.running',
  'enums.syncRunStatus.completed',
  'enums.syncRunStatus.partial',
  'enums.syncRunStatus.failed',
];

export function syncRunStatusLabel(status: SyncRunStatus | null | undefined): string {
  return status === null || status === undefined
    ? 'enums.syncRunStatus.unknown'
    : (SYNC_RUN_STATUS_LABELS[Number(status)] ?? 'enums.syncRunStatus.unknown');
}

// The response also carries `backfill` (one per-instrument history run) for shape uniformity, but
// only the two daily jobs are shown here (spec-04 design decision 14).
export const RUN_KINDS = [
  { key: 'prices', label: 'enums.syncRunKind.prices' },
  { key: 'fx', label: 'enums.syncRunKind.fx' },
] as const;

@Component({
  selector: 'app-settings',
  imports: [MatButtonModule, MatChipsModule, MatProgressSpinnerModule, TranslocoPipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  protected readonly triggering = signal(false);
  protected readonly triggerError = signal<string | null>(null);
  protected readonly syncRunStatusLabel = syncRunStatusLabel;
  protected readonly runKinds = RUN_KINDS;
  protected readonly formatDateTime = formatDateTime;

  protected readonly statusResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiMarketdataSyncStatus({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('settings.loadFailed'),
        );
      }
      return result.data;
    },
  });

  protected async triggerSync(): Promise<void> {
    if (this.triggering()) {
      return;
    }

    this.triggering.set(true);
    this.triggerError.set(null);

    const result = await postApiMarketdataSyncTrigger();

    this.triggering.set(false);

    if (result.error) {
      this.triggerError.set(
        readProblemDetails(result.error).detail ?? translate('settings.triggerFailed'),
      );
      return;
    }

    this.statusResource.reload();
  }
}
