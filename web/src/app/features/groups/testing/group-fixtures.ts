import type {
  AccountResponse,
  AssetGroupResponse,
  AssetGroupTreeChild,
  AssetGroupTreeItem,
} from '../../../api/portfolio';
import type { DashboardResponse } from '../../../api/reporting';
import { bankAccountResponse } from '../../../../testing/bank-account-fixtures';

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
  contributions: [],
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
  contributions: [],
  sharePercent: null,
  classAllocation: [],
};

export const accountAId = '66666666-6666-6666-6666-666666666666';
export const accountBId = '77777777-7777-7777-7777-777777777777';
export const archivedAccountId = '88888888-8888-8888-8888-888888888888';
export const otherArchivedAccountId = '99999999-9999-9999-9999-999999999999';

export const contributedGroup: AssetGroupResponse = {
  ...greenGroup,
  monthlyContributionPln: 2500,
  contributions: [
    { amountPln: 2000, accountId: accountAId },
    { amountPln: 500, accountId: null },
  ],
};

export const archivedContributionGroup: AssetGroupResponse = {
  ...blueGroup,
  monthlyContributionPln: 300,
  contributions: [{ amountPln: 300, accountId: archivedAccountId }],
};

export const groupAccounts: AccountResponse[] = [
  bankAccountResponse({ id: accountAId, name: 'Konto A' }),
  bankAccountResponse({ id: accountBId, name: 'Konto B' }),
  bankAccountResponse({ id: archivedAccountId, name: 'Stare konto', isArchived: true }),
  bankAccountResponse({
    id: otherArchivedAccountId,
    name: 'Zamknięte konto',
    isArchived: true,
  }),
];

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

export function treeChild(
  assetId: string,
  name: string,
  overrides: Partial<AssetGroupTreeChild> = {},
): AssetGroupTreeChild {
  return {
    assetId,
    name,
    assetClass: 2,
    groupId: null,
    isException: false,
    isArchived: false,
    ...overrides,
  };
}

export function treeAccount(
  accountId: string,
  name: string,
  children: AssetGroupTreeChild[],
  overrides: Partial<AssetGroupTreeItem> = {},
): AssetGroupTreeItem {
  return {
    kind: 'Account',
    accountId,
    assetId: null,
    name,
    accountType: 0,
    assetClass: null,
    groupId: null,
    cashAssetId: null,
    isArchived: false,
    children,
    ...overrides,
  };
}

export function treeAsset(
  assetId: string,
  name: string,
  overrides: Partial<AssetGroupTreeItem> = {},
): AssetGroupTreeItem {
  return {
    kind: 'Asset',
    accountId: null,
    assetId,
    name,
    accountType: null,
    assetClass: 0,
    groupId: null,
    cashAssetId: null,
    isArchived: false,
    children: [],
    ...overrides,
  };
}

export const treeAccountId = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';
export const treeChildOneId = 'f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1';
export const treeChildTwoId = 'f2f2f2f2-f2f2-f2f2-f2f2-f2f2f2f2f2f2';
export const treeChildThreeId = 'f3f3f3f3-f3f3-f3f3-f3f3-f3f3f3f3f3f3';

export function treeAccountWithThreeChildren(
  groupId: string | null = null,
  childGroupId: string | null = groupId,
): AssetGroupTreeItem {
  return treeAccount(
    treeAccountId,
    'IKE Obligacje',
    [
      treeChild(treeChildOneId, 'OTS0126', { assetClass: 4, groupId: childGroupId }),
      treeChild(treeChildTwoId, 'ROR0126', { assetClass: 4, groupId: childGroupId }),
      treeChild(treeChildThreeId, 'COI0126', { assetClass: 4, groupId: childGroupId }),
    ],
    { groupId },
  );
}

export function skarbonki(count: number): AssetGroupTreeItem[] {
  return Array.from({ length: count }, (_, index) =>
    treeAsset(
      `00000000-0000-0000-0000-${String(index + 1).padStart(12, '0')}`,
      `Skarbonka ${index + 1}`,
    ),
  );
}

export const groupTree: AssetGroupTreeItem[] = [treeAccountWithThreeChildren(), ...skarbonki(2)];

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
