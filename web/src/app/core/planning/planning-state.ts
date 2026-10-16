import { Injectable, computed, resource, signal } from '@angular/core';

import { getApiPortfolioPlanningOverdue } from '../../api/portfolio';
import { readProblemDetails } from '../auth/problem-details';

@Injectable({ providedIn: 'root' })
export class PlanningState {
  private readonly session = signal(0);

  private readonly overdueResource = resource({
    params: () => this.session(),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPlanningOverdue({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load.');
      }
      return result.data?.items ?? [];
    },
  });

  readonly overdue = computed(() =>
    this.overdueResource.hasValue() ? this.overdueResource.value() : [],
  );

  readonly overdueCount = computed(() => this.overdue().length);

  startSession(): void {
    this.session.update((value) => value + 1);
  }

  reloadOverdue(): void {
    this.overdueResource.reload();
  }
}
