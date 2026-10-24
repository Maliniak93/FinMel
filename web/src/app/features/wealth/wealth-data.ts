import { Injectable, computed, inject, resource, signal } from '@angular/core';
import { translate } from '@jsverse/transloco';

import {
  getApiPortfolioAccounts,
  getApiPortfolioBonds,
  getApiPortfolioCashPlaces,
  getApiPortfolioDeposits,
  getApiPortfolioMetals,
  getApiPortfolioSecurities,
} from '../../api/portfolio';
import { getApiReportingDashboard } from '../../api/reporting';
import { readProblemDetails } from '../../core/auth/problem-details';
import { assetClassColor } from '../../shared/colors';
import { GroupList } from '../../shared/group-select/group-list';
import { formatDate, formatPercent, formatQuantity } from '../../shared/format';
import { ACCOUNT_TYPE } from '../accounts/account-type';
import { ASSET_CLASS } from '../assets/asset-class';
import { BOND_STATUS } from '../bonds/bond-interest';
import { DEPOSIT_STATUS } from '../deposits/deposit-terms';
import { METAL } from '../metals/metal';

export type WealthType =
  'securities' | 'bonds' | 'deposits' | 'bank' | 'accounts' | 'metals' | 'cash';

export type WealthFlag = 'stale' | 'rateUnavailable' | 'maturity' | 'interestDue';

export interface WealthSubtitle {
  text?: string;
  key?: string;
  params?: Record<string, string | number>;
}

export interface WealthChange {
  amountPln: number;
  percent: number | null;
}

export interface WealthRow {
  type: WealthType;
  id: string;
  assetId: string | null;
  name: string;
  subtitle: WealthSubtitle | null;
  accountLabel: string | null;
  groupId: string | null;
  valuePln: number | null;
  change: WealthChange | null;
  flags: WealthFlag[];
  maturityDate: string | null;
  isArchived: boolean;
  link: string[];
  historyLink: string[] | null;
}

export interface WealthSection {
  type: WealthType;
  labelKey: string;
  color: string;
  link: string[];
}

export const WEALTH_SECTIONS: readonly WealthSection[] = [
  {
    type: 'securities',
    labelKey: 'shell.nav.securities',
    color: assetClassColor(ASSET_CLASS.Stock),
    link: ['/wealth/securities'],
  },
  {
    type: 'bonds',
    labelKey: 'shell.nav.bonds',
    color: assetClassColor(ASSET_CLASS.Bond),
    link: ['/wealth/bonds'],
  },
  {
    type: 'deposits',
    labelKey: 'shell.nav.deposits',
    color: assetClassColor(ASSET_CLASS.Deposit),
    link: ['/wealth/deposits'],
  },
  {
    type: 'bank',
    labelKey: 'shell.nav.bankAccounts',
    color: assetClassColor(ASSET_CLASS.BankAccount),
    link: ['/wealth/bank'],
  },
  {
    type: 'accounts',
    labelKey: 'shell.nav.accounts',
    color: assetClassColor(ASSET_CLASS.Cash),
    link: ['/wealth/accounts'],
  },
  {
    type: 'metals',
    labelKey: 'shell.nav.metals',
    color: assetClassColor(ASSET_CLASS.PreciousMetal),
    link: ['/wealth/metals'],
  },
  {
    type: 'cash',
    labelKey: 'shell.nav.cash',
    color: assetClassColor(ASSET_CLASS.Cash),
    link: ['/wealth/cash'],
  },
];

interface SourceState {
  isLoading(): boolean;
  hasValue(): boolean;
  error(): unknown;
  reload(): boolean;
}

interface CallResult<T> {
  data?: T;
  error?: unknown;
}

function source<T>(call: (signal: AbortSignal) => Promise<CallResult<T>>) {
  return resource({
    loader: async ({ abortSignal }) => {
      const result = await call(abortSignal);
      if (result.error) {
        throw new Error(readProblemDetails(result.error).detail ?? translate('wealth.error'));
      }
      return result.data as T;
    },
  });
}

function toNumber(value: number | string | null | undefined): number | null {
  return value === null || value === undefined ? null : Number(value);
}

export function sumPln(rows: readonly { valuePln: number | null }[]): number | null {
  let grosze = 0;
  let any = false;
  for (const row of rows) {
    if (row.valuePln !== null) {
      grosze += Math.round(row.valuePln * 100);
      any = true;
    }
  }
  return any ? grosze / 100 : null;
}

@Injectable({ providedIn: 'root' })
export class WealthData {
  private readonly groupList = inject(GroupList);

  readonly showArchived = signal(false);

  private readonly accounts = source((signal) => getApiPortfolioAccounts({ signal }));
  private readonly cashPlaces = source((signal) => getApiPortfolioCashPlaces({ signal }));
  private readonly deposits = source((signal) => getApiPortfolioDeposits({ signal }));
  private readonly bonds = source((signal) => getApiPortfolioBonds({ signal }));
  private readonly metals = source((signal) => getApiPortfolioMetals({ signal }));
  private readonly stocks = source((signal) =>
    getApiPortfolioSecurities({
      query: { assetClass: ASSET_CLASS.Stock, includeArchived: true },
      signal,
    }),
  );
  private readonly etfs = source((signal) =>
    getApiPortfolioSecurities({
      query: { assetClass: ASSET_CLASS.Etf, includeArchived: true },
      signal,
    }),
  );
  private readonly dashboard = source((signal) => getApiReportingDashboard({ signal }));

  private readonly sources: readonly SourceState[] = [
    this.accounts,
    this.cashPlaces,
    this.deposits,
    this.bonds,
    this.metals,
    this.stocks,
    this.etfs,
    this.dashboard,
  ];

  readonly loading = computed(() =>
    this.sources.some((entry) => entry.isLoading() && !entry.hasValue()),
  );

  readonly failed = computed(() => this.sources.some((entry) => entry.error()));

  readonly asOf = computed(() => (this.dashboard.hasValue() ? this.dashboard.value().asOf : null));

  private readonly allRows = computed<WealthRow[]>(() => {
    const byAsset = new Map(
      this.dashboard.hasValue()
        ? this.dashboard.value().byAsset.map((line) => [line.assetId, line])
        : [],
    );
    const line = (assetId: string) => {
      const found = byAsset.get(assetId);
      return { value: found ? Number(found.valuePln) : null, stale: found?.isStale ?? false };
    };
    const rows: WealthRow[] = [];

    if (this.accounts.hasValue()) {
      for (const account of this.accounts.value()) {
        const isBank = Number(account.type) === ACCOUNT_TYPE.Bank;
        const valued = line(account.cashAssetId);
        rows.push({
          type: isBank ? 'bank' : 'accounts',
          id: account.id,
          assetId: null,
          name: account.name,
          subtitle: null,
          accountLabel: null,
          groupId: account.groupId ?? null,
          valuePln: isBank ? valued.value : Number(account.cashBalance),
          change: null,
          flags: isBank && valued.stale ? ['stale'] : [],
          maturityDate: null,
          isArchived: account.isArchived,
          link: [isBank ? '/wealth/bank' : '/wealth/accounts', account.id],
          historyLink: null,
        });
      }
    }

    if (this.cashPlaces.hasValue()) {
      for (const place of this.cashPlaces.value().places) {
        const value = toNumber(place.valuePln);
        rows.push({
          type: 'cash',
          id: place.id,
          assetId: null,
          name: place.name,
          subtitle: null,
          accountLabel: null,
          groupId: place.groupId ?? null,
          valuePln: value,
          change: null,
          flags: value === null ? ['rateUnavailable'] : [],
          maturityDate: null,
          isArchived: place.isArchived,
          link: ['/wealth/cash'],
          historyLink: null,
        });
      }
    }

    if (this.deposits.hasValue()) {
      for (const deposit of this.deposits.value()) {
        const valued = line(deposit.assetId);
        const due = Number(deposit.status) === DEPOSIT_STATUS.Due;
        const flags: WealthFlag[] = [];
        if (valued.stale) {
          flags.push('stale');
        }
        if (due) {
          flags.push('maturity');
        }
        rows.push({
          type: 'deposits',
          id: deposit.assetId,
          assetId: deposit.assetId,
          name: deposit.name,
          subtitle: {
            key: 'wealth.subtitle.deposit',
            params: {
              rate: formatPercent(deposit.annualInterestRatePercent),
              date: formatDate(deposit.maturityDate),
            },
          },
          accountLabel: deposit.bankName ?? null,
          groupId: deposit.groupId ?? null,
          valuePln: valued.value,
          change: null,
          flags,
          maturityDate: due ? deposit.maturityDate : null,
          isArchived: deposit.isArchived,
          link: ['/wealth/deposits'],
          historyLink: ['/wealth/assets', deposit.assetId, 'transactions'],
        });
      }
    }

    if (this.bonds.hasValue()) {
      for (const bond of this.bonds.value()) {
        const valued = line(bond.assetId);
        const flags: WealthFlag[] = [];
        if (valued.stale) {
          flags.push('stale');
        }
        if (Number(bond.status) === BOND_STATUS.InterestDue) {
          flags.push('interestDue');
        }
        rows.push({
          type: 'bonds',
          id: bond.assetId,
          assetId: bond.assetId,
          name: bond.name,
          subtitle: {
            key: 'wealth.subtitle.bond',
            params: { series: bond.seriesCode, count: formatQuantity(bond.bondCount) },
          },
          accountLabel: bond.accountName,
          groupId: bond.groupId ?? null,
          valuePln: valued.value,
          change: null,
          flags,
          maturityDate: null,
          isArchived: bond.isArchived,
          link: ['/wealth/bonds'],
          historyLink: ['/wealth/assets', bond.assetId, 'transactions'],
        });
      }
    }

    if (this.metals.hasValue()) {
      for (const metal of this.metals.value()) {
        const valued = line(metal.assetId);
        rows.push({
          type: 'metals',
          id: metal.assetId,
          assetId: metal.assetId,
          name: metal.name,
          subtitle: {
            key:
              Number(metal.metal) === METAL.Gold
                ? 'wealth.subtitle.gold'
                : 'wealth.subtitle.silver',
            params: {
              pieces: formatQuantity(metal.pieces),
              grams: formatQuantity(metal.totalFineGrams),
            },
          },
          accountLabel: null,
          groupId: metal.groupId ?? null,
          valuePln: valued.value,
          change: null,
          flags: valued.stale ? ['stale'] : [],
          maturityDate: null,
          isArchived: metal.isArchived,
          link: ['/wealth/metals'],
          historyLink: ['/wealth/assets', metal.assetId, 'transactions'],
        });
      }
    }

    for (const securities of [this.stocks, this.etfs]) {
      if (!securities.hasValue()) {
        continue;
      }
      for (const holding of securities.value().holdings) {
        const valued = line(holding.assetId);
        const value = toNumber(holding.valuePln);
        const gain = toNumber(holding.unrealizedPlPln);
        rows.push({
          type: 'securities',
          id: holding.assetId,
          assetId: holding.assetId,
          name: holding.name,
          subtitle: holding.ticker ? { text: holding.ticker } : null,
          accountLabel: holding.accountName,
          groupId: holding.groupId,
          valuePln: value,
          change:
            gain === null
              ? null
              : { amountPln: gain, percent: toNumber(holding.unrealizedPlPercent) },
          flags:
            holding.priceUnavailableReason !== null || holding.lastPrice === null || valued.stale
              ? ['stale']
              : [],
          maturityDate: null,
          isArchived: holding.isArchived,
          link: ['/wealth/securities'],
          historyLink: ['/wealth/assets', holding.assetId, 'transactions'],
        });
      }
    }

    return rows;
  });

  readonly rows = computed(() =>
    this.showArchived() ? this.allRows() : this.allRows().filter((row) => !row.isArchived),
  );

  readonly total = computed(() => sumPln(this.rows()));

  readonly typeSums = computed(() => {
    const sums = {} as Record<WealthType, number | null>;
    for (const section of WEALTH_SECTIONS) {
      sums[section.type] = sumPln(this.rows().filter((row) => row.type === section.type));
    }
    return sums;
  });

  readonly accountsCount = computed(
    () => this.rows().filter((row) => row.type === 'accounts').length,
  );

  readonly accountsLoaded = computed(() => this.accounts.hasValue());

  retryFailed(): void {
    for (const entry of this.sources) {
      if (entry.error()) {
        entry.reload();
      }
    }
  }

  refresh(): void {
    for (const entry of this.sources) {
      if (!entry.isLoading()) {
        entry.reload();
      }
    }
  }

  reload(): void {
    for (const entry of this.sources) {
      entry.reload();
    }
    this.groupList.reload();
  }
}
