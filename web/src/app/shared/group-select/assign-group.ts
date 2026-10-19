import { translate } from '@jsverse/transloco';

import { putApiPortfolioAssetGroupsAssignment } from '../../api/portfolio';
import { readProblemDetails } from '../../core/auth/problem-details';

export async function assignGroup(assetId: string, groupId: string | null): Promise<string | null> {
  const result = await putApiPortfolioAssetGroupsAssignment({
    body: { assetIds: [assetId], groupId },
  });
  return result.error
    ? (readProblemDetails(result.error).detail ?? translate('errors.generic'))
    : null;
}
