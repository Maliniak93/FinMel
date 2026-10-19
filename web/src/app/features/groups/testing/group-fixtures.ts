import type { AssetGroupResponse, AssignableAssetResponse } from '../../../api/portfolio';

export const blueGroupId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
export const greenGroupId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

export const blueGroup: AssetGroupResponse = {
  id: blueGroupId,
  name: 'Emerytura',
  color: 'blue',
  targetPln: null,
  assetCount: 3,
};

export const greenGroup: AssetGroupResponse = {
  id: greenGroupId,
  name: 'Poduszka',
  color: 'green',
  targetPln: 30000,
  assetCount: 1,
};

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
