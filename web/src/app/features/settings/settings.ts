import { DatePipe } from '@angular/common';
import { Component, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import {
  getApiMarketdataSyncStatus,
  postApiMarketdataSyncTrigger,
  type SyncRunStatus,
} from '../../api/marketdata';
import { readProblemDetails } from '../../core/auth/problem-details';

// Backend enum (Skarbiec.MarketData.Data.SyncRunStatus) serializes as its underlying int, so the
// generated client types it as a bare `number` — labels maintained here in the C# enum's declaration
// order (Data/SyncRun.cs), same pattern as features/assets/asset-class.ts.
const SYNC_RUN_STATUS_LABELS: readonly string[] = ['Running', 'Completed', 'Partial', 'Failed'];

function syncRunStatusLabel(status: SyncRunStatus | null | undefined): string {
  return status === null || status === undefined
    ? 'Unknown'
    : (SYNC_RUN_STATUS_LABELS[Number(status)] ?? 'Unknown');
}

@Component({
  selector: 'app-settings',
  imports: [DatePipe, MatButtonModule, MatChipsModule, MatProgressSpinnerModule],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  protected readonly triggering = signal(false);
  protected readonly triggerError = signal<string | null>(null);
  protected readonly syncRunStatusLabel = syncRunStatusLabel;

  protected readonly statusResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiMarketdataSyncStatus({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load sync status.');
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
      this.triggerError.set(readProblemDetails(result.error).detail ?? 'Failed to trigger sync.');
      return;
    }

    this.statusResource.reload();
  }
}
