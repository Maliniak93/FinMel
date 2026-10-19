import { Injectable, computed, resource } from '@angular/core';
import { translate } from '@jsverse/transloco';

import { getApiPortfolioAssetGroups } from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';

@Injectable({ providedIn: 'root' })
export class GroupList {
  private readonly groupsResource = resource({
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAssetGroups({ signal: abortSignal });
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('groups.loadFailed'));
      }
      return result.data;
    },
  });

  readonly groups = computed(() =>
    this.groupsResource.hasValue() ? this.groupsResource.value() : [],
  );

  reload(): void {
    this.groupsResource.reload();
  }
}
