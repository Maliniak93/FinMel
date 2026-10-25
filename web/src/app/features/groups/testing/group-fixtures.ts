import type { AssetGroupResponse, AssignableAssetResponse } from '../../../api/portfolio';
import type { DashboardResponse } from '../../../api/reporting';

export const blueGroupId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
export const greenGroupId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

export const blueGroup: AssetGroupResponse = {
  id: blueGroupId,
  name: 'Emerytura',
  color: 'blue',
  targetPln: null,
  assetCount: 3,
  kind: 0,
  monthlyContributionPln: 0,
  sharePercent: 100,
  classAllocation: [],
};

export const greenGroup: AssetGroupResponse = {
  id: greenGroupId,
  name: 'Poduszka',
  color: 'green',
  targetPln: 30000,
  assetCount: 1,
  kind: 1,
  monthlyContributionPln: 0,
  sharePercent: null,
  classAllocation: [],
};

export const assetGroups: AssetGroupResponse[] = [blueGroup, greenGroup];

export function isGroupListRequest(input: unknown): boolean {
  const request = input as Request | string;
  const url = typeof request === 'string' ? request : request.url;
  const method = typeof request === 'string' ? 'GET' : request.method;
  return method === 'GET' && /\/api\/portfolio\/asset-groups(\?|$)/.test(url);
}

export const memberAssetId = '11111111-1111-1111-1111-111111111111';
export const freeAssetId = '22222222-2222-2222-2222-222222222222';
export const otherGroupAssetId = '33333333-3333-3333-3333-333333333333';
export const archivedAssetId = '44444444-4444-4444-4444-444444444444';

export const assignableAssets: AssignableAssetResponse[] = [
  {
    assetId: memberAssetId,
    name: 'Member Bond',
    assetClass: 4,
    currency: 'PLN',
    accountId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    accountName: 'Obligacje',
    cashPlaceName: null,
    groupId: blueGroupId,
    isArchived: false,
  },
  {
    assetId: freeAssetId,
    name: 'Free Stock',
    assetClass: 2,
    currency: 'PLN',
    accountId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
    accountName: 'Broker',
    cashPlaceName: null,
    groupId: null,
    isArchived: false,
  },
  {
    assetId: otherGroupAssetId,
    name: 'Cushion Cash',
    assetClass: 0,
    currency: 'PLN',
    accountId: null,
    accountName: null,
    cashPlaceName: 'Safe',
    groupId: greenGroupId,
    isArchived: false,
  },
  {
    assetId: archivedAssetId,
    name: 'Old Bond',
    assetClass: 4,
    currency: 'PLN',
    accountId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    accountName: 'Obligacje',
    cashPlaceName: null,
    groupId: null,
    isArchived: true,
  },
];

export const groupsDashboard: DashboardResponse = {
  netWorthPln: 6300,
  asOf: '2026-10-24',
  isStale: false,
  byAssetClass: [],
  byAccount: [],
  byGroup: [
    {
      groupId: blueGroupId,
      valuePln: 4000,
      percentage: 63.5,
      byAssetClass: [{ assetClass: 4, valuePln: 4000 }],
    },
    {
      groupId: greenGroupId,
      valuePln: 1500,
      percentage: 23.8,
      byAssetClass: [{ assetClass: 0, valuePln: 1500 }],
    },
    {
      groupId: null,
      valuePln: 800,
      percentage: 12.7,
      byAssetClass: [{ assetClass: 2, valuePln: 800 }],
    },
  ],
  byAsset: [
    { assetId: memberAssetId, valuePln: 4000, priceDate: '2026-10-24', isStale: false },
    { assetId: freeAssetId, valuePln: 800, priceDate: '2026-10-24', isStale: false },
    { assetId: otherGroupAssetId, valuePln: 1500, priceDate: '2026-10-24', isStale: false },
  ],
};
