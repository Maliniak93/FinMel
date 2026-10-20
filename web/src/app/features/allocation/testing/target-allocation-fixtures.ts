import { vi } from 'vitest';

import type {
  SaveTargetAllocationRequest,
  TargetAllocationGroupResponse,
  TargetAllocationResponse,
} from '../../../api/reporting';
import type { AssetGroupResponse } from '../../../api/portfolio';
import { ASSET_CLASS } from '../../assets/asset-class';
import { jsonResponse, requestUrl } from '../../dashboard/testing/dashboard-fixtures';
import {
  blueGroup,
  blueGroupId,
  greenGroup,
  greenGroupId,
} from '../../groups/testing/group-fixtures';

export const TARGET_ALLOCATION_PATH = '/api/reporting/target-allocation';

export const byAssetClass: TargetAllocationResponse['byAssetClass'] = [
  { assetClass: ASSET_CLASS.Cash, valuePln: 12000 },
  { assetClass: ASSET_CLASS.Deposit, valuePln: 20000 },
  { assetClass: ASSET_CLASS.Stock, valuePln: 70000 },
  { assetClass: ASSET_CLASS.Etf, valuePln: 40000 },
  { assetClass: ASSET_CLASS.Bond, valuePln: 40000 },
  { assetClass: ASSET_CLASS.PreciousMetal, valuePln: 10000 },
  { assetClass: ASSET_CLASS.BankAccount, valuePln: 8000 },
];

export const savedGroups: TargetAllocationGroupResponse[] = [
  {
    name: 'Akcje/ETF',
    assetClasses: [ASSET_CLASS.Stock, ASSET_CLASS.Etf],
    targetPercent: 50,
    bandPp: 5,
    bandLowPercent: 45,
    bandHighPercent: 55,
    valuePln: 110000,
    currentPercent: 61.1,
    deviationPp: 11.1,
    targetValuePln: 90000,
    deviationPln: 20000,
    isInBand: false,
  },
  {
    name: 'Obligacje',
    assetClasses: [ASSET_CLASS.Bond, ASSET_CLASS.Deposit],
    targetPercent: 40,
    bandPp: 5,
    bandLowPercent: 35,
    bandHighPercent: 45,
    valuePln: 60000,
    currentPercent: 33.3,
    deviationPp: -6.7,
    targetValuePln: 72000,
    deviationPln: -12000,
    isInBand: false,
  },
  {
    name: 'Kruszce',
    assetClasses: [ASSET_CLASS.PreciousMetal],
    targetPercent: 10,
    bandPp: 5,
    bandLowPercent: 5,
    bandHighPercent: 15,
    valuePln: 10000,
    currentPercent: 5.6,
    deviationPp: -4.4,
    targetValuePln: 18000,
    deviationPln: -8000,
    isInBand: true,
  },
];

export const configuredAllocation: TargetAllocationResponse = {
  isConfigured: true,
  asOf: '2026-08-09',
  isStale: false,
  totalPln: 200000,
  basePln: 180000,
  outsidePln: 20000,
  outsideAssetClasses: [ASSET_CLASS.Cash, ASSET_CLASS.BankAccount],
  byAssetClass,
  groups: savedGroups,
};

export const notConfiguredAllocation: TargetAllocationResponse = {
  isConfigured: false,
  asOf: '2026-08-09',
  isStale: false,
  totalPln: 200000,
  basePln: 0,
  outsidePln: 0,
  outsideAssetClasses: [],
  byAssetClass,
  groups: [],
};

export function zeroBaseAllocation(
  overrides: Partial<TargetAllocationResponse> = {},
): TargetAllocationResponse {
  return {
    ...configuredAllocation,
    basePln: 0,
    outsidePln: 0,
    outsideAssetClasses: [],
    byAssetClass: [],
    groups: configuredAllocation.groups.map((group) => ({
      ...group,
      valuePln: 0,
      currentPercent: null,
      deviationPp: null,
      targetValuePln: null,
      deviationPln: null,
      isInBand: null,
    })),
    ...overrides,
  };
}

export function savedBody(request: SaveTargetAllocationRequest) {
  return request.groups.map((group) => ({
    name: group.name,
    assetClasses: group.assetClasses.map(Number).sort((a, b) => a - b),
    targetPercent: Number(group.targetPercent),
    bandPp: Number(group.bandPp),
  }));
}

export const emeryturaGroupId = blueGroupId;
export const inwestycjeGroupId = greenGroupId;
export const emeryturaGroup = blueGroup;
export const inwestycjeGroup: AssetGroupResponse = { ...greenGroup, name: 'Inwestycje' };
export const unknownGroupId = '00000000-0000-0000-0000-000000000000';

export const groupConfiguredAllocation: TargetAllocationResponse = {
  isConfigured: true,
  asOf: '2026-08-09',
  isStale: false,
  totalPln: 35000,
  basePln: 30000,
  outsidePln: 5000,
  outsideAssetClasses: [ASSET_CLASS.Cash],
  byAssetClass: [
    { assetClass: ASSET_CLASS.Bond, valuePln: 30000 },
    { assetClass: ASSET_CLASS.Cash, valuePln: 5000 },
  ],
  groups: [
    {
      name: 'Obligacje',
      assetClasses: [ASSET_CLASS.Bond],
      targetPercent: 60,
      bandPp: 5,
      bandLowPercent: 55,
      bandHighPercent: 65,
      valuePln: 30000,
      currentPercent: 100,
      deviationPp: 40,
      targetValuePln: 18000,
      deviationPln: 12000,
      isInBand: false,
    },
  ],
};

export const groupComparisonAllocation: TargetAllocationResponse = {
  ...groupConfiguredAllocation,
  totalPln: 105000,
  basePln: 100000,
  outsidePln: 5000,
  byAssetClass: [
    { assetClass: ASSET_CLASS.Bond, valuePln: 54500 },
    { assetClass: ASSET_CLASS.Stock, valuePln: 36400 },
    { assetClass: ASSET_CLASS.PreciousMetal, valuePln: 9100 },
    { assetClass: ASSET_CLASS.Cash, valuePln: 5000 },
  ],
  groups: [
    {
      name: 'Obligacje',
      assetClasses: [ASSET_CLASS.Bond],
      targetPercent: 50,
      bandPp: 5,
      bandLowPercent: 45,
      bandHighPercent: 55,
      valuePln: 54500,
      currentPercent: 54.5,
      deviationPp: 4.5,
      targetValuePln: 50000,
      deviationPln: 4500,
      isInBand: true,
    },
    {
      name: 'Akcje',
      assetClasses: [ASSET_CLASS.Stock],
      targetPercent: 40,
      bandPp: 5,
      bandLowPercent: 35,
      bandHighPercent: 45,
      valuePln: 36400,
      currentPercent: 36.4,
      deviationPp: -3.6,
      targetValuePln: 40000,
      deviationPln: -3600,
      isInBand: true,
    },
    {
      name: 'Złoto',
      assetClasses: [ASSET_CLASS.PreciousMetal],
      targetPercent: 10,
      bandPp: 3,
      bandLowPercent: 7,
      bandHighPercent: 13,
      valuePln: 9100,
      currentPercent: 9.1,
      deviationPp: -0.9,
      targetValuePln: 10000,
      deviationPln: -900,
      isInBand: true,
    },
  ],
};

export function emptyGroupAllocation(): TargetAllocationResponse {
  return zeroBaseAllocation({
    totalPln: 0,
    outsidePln: 0,
    outsideAssetClasses: [],
    groups: groupConfiguredAllocation.groups.map((group) => ({
      ...group,
      valuePln: 0,
      currentPercent: null,
      deviationPp: null,
      targetValuePln: null,
      deviationPln: null,
      isInBand: null,
    })),
  });
}

export interface TargetAllocationApi {
  fetchSpy: ReturnType<typeof vi.spyOn>;
  puts: SaveTargetAllocationRequest[];
  gets: () => number;
  groupGets: string[];
  groupPuts: { groupId: string; body: SaveTargetAllocationRequest }[];
  groupDeletes: string[];
}

const GROUP_ROUTE = /\/target-allocation\/groups\/([^/?]+)/;

// get and put build a fresh Response per call (a body is read once); put receives the 1-based attempt number.
// groups answers the asset-group list; groupGet/groupPut/groupDelete answer the per-group routes (default 404 / 204 / 204).
export function mockTargetAllocationApi(options: {
  get: (call: number) => Response;
  put?: (attempt: number) => Response;
  groups?: AssetGroupResponse[];
  groupGet?: (groupId: string, call: number) => Response;
  groupPut?: (groupId: string, attempt: number) => Response;
  groupDelete?: (groupId: string) => Response;
}): TargetAllocationApi {
  const puts: SaveTargetAllocationRequest[] = [];
  const groupGets: string[] = [];
  const groupPuts: TargetAllocationApi['groupPuts'] = [];
  const groupDeletes: string[] = [];
  let getCalls = 0;
  const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = requestUrl(input);
    const request = input as Request;
    if (/\/api\/portfolio\/asset-groups(\?|$)/.test(url)) {
      return jsonResponse(options.groups ?? []);
    }
    if (!url.includes(TARGET_ALLOCATION_PATH)) {
      throw new Error(`Unexpected request: ${url}`);
    }
    const groupId = GROUP_ROUTE.exec(url)?.[1];
    if (groupId !== undefined) {
      if (request.method === 'PUT') {
        groupPuts.push({
          groupId,
          body: (await request.clone().json()) as SaveTargetAllocationRequest,
        });
        return (options.groupPut ?? (() => new Response(null, { status: 204 })))(
          groupId,
          groupPuts.length,
        );
      }
      if (request.method === 'DELETE') {
        groupDeletes.push(groupId);
        return (options.groupDelete ?? (() => new Response(null, { status: 204 })))(groupId);
      }
      groupGets.push(groupId);
      return (options.groupGet ?? (() => jsonResponse({ detail: 'Not found.', status: 404 }, 404)))(
        groupId,
        groupGets.length,
      );
    }
    if (request.method === 'PUT') {
      puts.push((await request.clone().json()) as SaveTargetAllocationRequest);
      return (options.put ?? (() => jsonResponse(configuredAllocation)))(puts.length);
    }
    getCalls += 1;
    return options.get(getCalls);
  });
  return { fetchSpy, puts, gets: () => getCalls, groupGets, groupPuts, groupDeletes };
}
